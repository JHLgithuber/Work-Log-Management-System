from datetime import timedelta
from io import BytesIO
from xml.etree import ElementTree
from zipfile import ZipFile

from app.auth import AuthService


def create_task(client, headers, payload):
    response = client.post("/tasks", json=payload, headers=headers)
    assert response.status_code == 201, response.text
    return response.json()


def test_task_entry_and_daily_note_round_trip(client, user_headers, task_payload, future_date):
    task = create_task(client, user_headers, {**task_payload, "title": "  Trimmed title  "})
    assert task["title"] == "Trimmed title"
    assert task["start_at"] == f"{future_date}T00:00:00+09:00"
    assert task["due_at"].endswith("T23:59:59.999999+09:00")
    path = f"/tasks/{task['id']}/entries/{future_date}"
    empty = client.get(path, headers=user_headers)
    assert empty.status_code == 200 and empty.json()["id"] == 0
    first = client.put(path, json={"performed_content": "First entry"}, headers=user_headers)
    second = client.put(
        path, json={"performed_content": "Updated entry", "retrospective": "Test reflection"}, headers=user_headers
    )
    assert first.status_code == second.status_code == 200
    assert second.json()["id"] == first.json()["id"]
    assert second.json()["created_at"] == first.json()["created_at"]
    loaded = client.get(f"/tasks/{task['id']}", headers=user_headers).json()
    assert len(loaded["entries"]) == 1
    assert loaded["entries"][0]["performed_content"] == "Updated entry"
    assert client.delete(path, headers=user_headers).status_code == 204
    assert client.get(path, headers=user_headers).json()["id"] == 0

    note_path = f"/tasks/daily-notes/{future_date}"
    assert client.get(note_path, headers=user_headers).json()["id"] == 0
    first_note = client.put(note_path, json={"content": "Synthetic note"}, headers=user_headers)
    second_note = client.put(note_path, json={"content": "Updated note"}, headers=user_headers)
    assert first_note.status_code == second_note.status_code == 200
    assert first_note.json()["id"] == second_note.json()["id"]
    assert client.get(note_path, headers=user_headers).json()["content"] == "Updated note"


def test_hierarchy_reparent_cycle_rejection_and_cascade_delete(client, user_headers, task_payload, future_date):
    first = create_task(client, user_headers, {**task_payload, "title": "First root"})
    second = create_task(client, user_headers, {**task_payload, "title": "Second root"})
    child = create_task(client, user_headers, {**task_payload, "parent_id": first["id"], "title": "Child"})
    grandchild = create_task(client, user_headers, {**task_payload, "parent_id": child["id"], "title": "Grandchild"})
    cycle = client.put(
        f"/tasks/{first['id']}", json={**task_payload, "parent_id": grandchild["id"]}, headers=user_headers
    )
    assert cycle.status_code == 400
    assert client.get(f"/tasks/{first['id']}", headers=user_headers).json()["parent_id"] is None
    moved = client.put(
        f"/tasks/{child['id']}", json={**task_payload, "parent_id": second["id"]}, headers=user_headers
    )
    assert moved.status_code == 200
    assert client.get(f"/tasks/{first['id']}", headers=user_headers).json()["children"] == []
    assert client.get(f"/tasks/{second['id']}", headers=user_headers).json()["children"][0]["id"] == child["id"]
    entry_path = f"/tasks/{grandchild['id']}/entries/{future_date}"
    assert client.put(entry_path, json={"performed_content": "Synthetic child work"}, headers=user_headers).status_code == 200
    assert client.delete(f"/tasks/{second['id']}", headers=user_headers).status_code == 204
    for task in [second, child, grandchild]:
        assert client.get(f"/tasks/{task['id']}", headers=user_headers).status_code == 404
    assert client.get(entry_path, headers=user_headers).status_code == 404
    assert [task["id"] for task in client.get("/tasks", headers=user_headers).json()] == [first["id"]]


def test_date_validation_and_korean_day_filtering(client, user_headers, task_payload, future_date):
    for overrides in [
        {"due_at": f"{future_date - timedelta(days=1)}T23:59:59+09:00"},
        {"actual_end_at": f"{future_date - timedelta(days=1)}T23:59:59+09:00"},
        {"priority": -1},
        {"priority": 256},
    ]:
        response = client.post("/tasks", json={**task_payload, **overrides}, headers=user_headers)
        assert response.status_code in (400, 422)
    assert client.get("/tasks", headers=user_headers).json() == []
    # 15:00 UTC belongs to the following calendar day in Korea.
    payload = {
        **task_payload,
        "start_at": f"{future_date}T15:00:00Z",
        "actual_end_at": f"{future_date + timedelta(days=1)}T08:00:00+09:00",
    }
    task = create_task(client, user_headers, payload)
    assert task["start_at"] == f"{future_date + timedelta(days=1)}T00:00:00+09:00"
    for delta, expected in [(0, []), (1, [task["id"]]), (2, [])]:
        response = client.get(
            "/tasks", params={"target_date": str(future_date + timedelta(days=delta))}, headers=user_headers
        )
        assert response.status_code == 200
        assert [task["id"] for task in response.json()] == expected


def test_cross_user_data_isolation(database, client, user_headers, task_payload, future_date):
    task = create_task(client, user_headers, task_payload)
    entry_path = f"/tasks/{task['id']}/entries/{future_date}"
    note_path = f"/tasks/daily-notes/{future_date}"
    client.put(entry_path, json={"performed_content": "Owner-only entry"}, headers=user_headers)
    client.put(note_path, json={"content": "Owner-only note"}, headers=user_headers)
    auth = AuthService(database)
    auth.register("qa-other-user", "synthetic-other-password")
    token = auth.login("qa-other-user", "synthetic-other-password").access_token
    other_headers = {"Authorization": f"Bearer {token}"}
    assert client.get("/tasks", headers=other_headers).json() == []
    assert client.get(note_path, headers=other_headers).json()["content"] == ""
    for method, path, body in [
        ("GET", f"/tasks/{task['id']}", None),
        ("PUT", f"/tasks/{task['id']}", task_payload),
        ("DELETE", f"/tasks/{task['id']}", None),
        ("GET", entry_path, None),
        ("PUT", entry_path, {"performed_content": "Not allowed"}),
        ("DELETE", entry_path, None),
    ]:
        assert client.request(method, path, json=body, headers=other_headers).status_code == 404
    child_attempt = client.post(
        "/tasks", json={**task_payload, "parent_id": task["id"]}, headers=other_headers
    )
    assert child_attempt.status_code == 400
    assert client.get(entry_path, headers=user_headers).json()["performed_content"] == "Owner-only entry"
    assert client.get(note_path, headers=user_headers).json()["content"] == "Owner-only note"


def test_export_date_range_notes_only_and_empty_range(client, user_headers, future_date):
    for delta, content in [(0, "Included note"), (1, "Excluded note")]:
        response = client.put(
            f"/tasks/daily-notes/{future_date + timedelta(days=delta)}",
            json={"content": content}, headers=user_headers,
        )
        assert response.status_code == 200
    response = client.get(
        "/tasks/export.xlsx", params={"start_date": str(future_date), "end_date": str(future_date)}, headers=user_headers
    )
    assert response.status_code == 200
    assert response.headers["content-type"] == "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    with ZipFile(BytesIO(response.content)) as archive:
        workbook = ElementTree.fromstring(archive.read("xl/workbook.xml"))
        ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
        assert [sheet.attrib["name"] for sheet in workbook.findall("s:sheets/s:sheet", ns)] == [str(future_date)]
        assert "Included note" in archive.read("xl/worksheets/sheet1.xml").decode()
        assert "Excluded note" not in archive.read("xl/worksheets/sheet1.xml").decode()
    for start, end in [(future_date + timedelta(days=2), future_date + timedelta(days=3)),
                       (future_date + timedelta(days=1), future_date)]:
        response = client.get(
            "/tasks/export.xlsx", params={"start_date": str(start), "end_date": str(end)}, headers=user_headers
        )
        assert response.status_code == 400
