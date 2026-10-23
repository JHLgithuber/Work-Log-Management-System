import base64
import hashlib
import hmac
from io import BytesIO
from xml.etree import ElementTree
from zipfile import ZipFile

import pytest
from sqlalchemy import select, text

from app.auth import AuthService
from app.domain import Task, User


def test_restart_preserves_only_registered_user_and_owned_tasks(database, client, user_headers, task_payload):
    response = client.post("/tasks", json=task_payload, headers=user_headers)
    assert response.status_code == 201
    task = response.json()
    with database.session() as session:
        before = session.scalar(select(User).where(User.username == "qa-user"))
        user_id = before.id
        password_hash = before.password_hash

    database.initialize()

    with database.session() as session:
        user = session.get(User, user_id)
        assert user is not None, "Restart must not delete a real registered account"
        assert user.password_hash == password_hash
        assert session.get(Task, task["id"]).user_id == user_id
    assert client.get("/tasks", headers=user_headers).status_code == 200
    assert client.get(f"/tasks/{task['id']}", headers=user_headers).json()["id"] == task["id"]


@pytest.mark.parametrize("method", ["POST", "PUT"])
def test_whitespace_only_title_is_rejected(client, user_headers, task_payload, method):
    path = "/tasks"
    if method == "PUT":
        created = client.post(path, json=task_payload, headers=user_headers)
        path += f"/{created.json()['id']}"
    response = client.request(method, path, json={**task_payload, "title": " \t\n "}, headers=user_headers)
    assert response.status_code == 422
    if method == "PUT":
        assert client.get(path, headers=user_headers).json()["title"] == task_payload["title"]
    else:
        assert client.get(path, headers=user_headers).json() == []


@pytest.mark.parametrize("endpoint", ["/tasks", "/auth/me"])
@pytest.mark.parametrize("token", ["x.y.a", "x.y.é"])
def test_malformed_bearer_token_is_unauthorized_not_server_error(client, endpoint, token):
    # An invalid base64 signature must be a normal unauthorized response.
    # HTTP headers are latin-1, so the second case is a non-ASCII latin-1 byte.
    response = client.get(endpoint, headers={"Authorization": f"Bearer {token}".encode("latin-1")})
    assert response.status_code == 401
    assert response.headers["www-authenticate"] == "Bearer"


def test_export_handles_pasted_control_characters(client, user_headers, task_payload, future_date):
    task_payload["content"] = "한글 😀 copied\x00\x0b\x1f from another app & <tag>\t\n"
    created = client.post("/tasks", json=task_payload, headers=user_headers)
    assert created.status_code == 201
    task_id = created.json()["id"]
    entry = client.put(
        f"/tasks/{task_id}/entries/{future_date}",
        json={"performed_content": "Synthetic entry\x07 completed", "retrospective": "\t Good\n"},
        headers=user_headers,
    )
    assert entry.status_code == 200
    response = client.get(
        "/tasks/export.xlsx",
        params={"start_date": str(future_date), "end_date": str(future_date)},
        headers=user_headers,
    )
    assert response.status_code == 200
    with ZipFile(BytesIO(response.content)) as archive:
        for name in archive.namelist():
            if name.endswith(".xml") or name.endswith(".rels"):
                ElementTree.fromstring(archive.read(name))
        worksheet = ElementTree.fromstring(archive.read("xl/worksheets/sheet1.xml"))
        ns = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}
        cell_texts = [element.text for element in worksheet.findall(".//s:t", ns)]
        assert "한글 😀 copied��� from another app & <tag>\t\n" in cell_texts
        assert "\t Good\n" in cell_texts
        assert all(element.get("{http://www.w3.org/XML/1998/namespace}space") == "preserve"
                   for element in worksheet.findall(".//s:t", ns))
    assert client.get(f"/tasks/{task_id}", headers=user_headers).json()["content"] == task_payload["content"]


def test_restart_preserves_account_without_tasks(database, user_headers, client):
    database.initialize()
    database.initialize()
    assert client.get("/auth/me", headers=user_headers).status_code == 200


def test_legacy_user_conversion_runs_once(database, client, user_headers, task_payload):
    # Build a synthetic pre-authentication schema by removing its new column.
    # The genuine one-time legacy conversion remains supported.
    created = client.post("/tasks", json=task_payload, headers=user_headers)
    task_id = created.json()["id"]
    with database._engine.begin() as connection:
        connection.execute(text("ALTER TABLE users DROP COLUMN password_hash"))
    database.initialize()
    with database.session() as session:
        assert session.scalar(select(User)) is None
        assert session.get(Task, task_id).user_id is None

    # Remove only the synthetic admin task, then add a real post-migration user.
    # Without the schema guard, its next restart would repeat the conversion.
    with database.session() as session:
        session.delete(session.get(Task, task_id))
        session.commit()
    auth = AuthService(database)
    auth.register("qa-after-migration", "synthetic-after-migration-password")
    database.initialize()
    assert auth.authenticate("qa-after-migration", "synthetic-after-migration-password").username == "qa-after-migration"


@pytest.mark.parametrize(
    "header,payload",
    [
        (b"[]", b'{}'),
        (b'{"alg":"HS256","typ":"JWT"}', b'[]'),
        (b'{"alg":"HS256","typ":"JWT"}', b'not-json'),
        (b'{"alg":"HS256","typ":"JWT"}', b'\xff'),
    ],
)
def test_signed_invalid_token_payload_is_unauthorized(client, header, payload):
    def encode(value):
        return base64.urlsafe_b64encode(value).rstrip(b"=")

    message = encode(header) + b"." + encode(payload)
    signature = hmac.new(b"synthetic-test-key-not-for-production", message, hashlib.sha256).digest()
    token = (message + b"." + encode(signature)).decode()
    response = client.get("/auth/me", headers={"Authorization": f"Bearer {token}"})
    assert response.status_code == 401
