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
