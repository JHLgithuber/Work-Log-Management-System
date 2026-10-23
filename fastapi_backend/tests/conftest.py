"""Hermetic tests: no .env loading, production DB, network server, or real records."""
from datetime import timedelta
from unittest.mock import patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

# Settings normally read the adjacent .env on import. Never read it in tests.
with patch("dotenv.load_dotenv", return_value=False), patch.dict(
    "os.environ",
    {
        "WORKLOG_ADMIN_USERNAME": "qa-admin",
        "WORKLOG_ADMIN_PASSWORD": "synthetic-admin-password",
        "WORKLOG_JWT_SECRET_KEY": "synthetic-test-key-not-for-production",
        "WORKLOG_DATABASE_URL": "",
    },
):
    from app.auth import AuthService
    from app.auth_routes import AuthRouter
    from app.database import Database
    from app.domain import DateBounds
    from app.routes import TaskRouter


@pytest.fixture
def future_date():
    return DateBounds.now().date() + timedelta(days=365)


@pytest.fixture
def database(tmp_path):
    database = Database(database_url=f"sqlite:///{tmp_path / 'synthetic.sqlite3'}")
    database.initialize()
    yield database
    database._engine.dispose()


@pytest.fixture
def client(database):
    app = FastAPI()
    app.include_router(AuthRouter(database).router)
    app.include_router(TaskRouter(database).router)
    with TestClient(app, raise_server_exceptions=False) as client:
        yield client


@pytest.fixture
def user_headers(database):
    auth = AuthService(database)
    auth.register("qa-user", "synthetic-user-password")
    token = auth.login("qa-user", "synthetic-user-password").access_token
    return {"Authorization": f"Bearer {token}"}


@pytest.fixture
def task_payload(future_date):
    return {
        "title": "Synthetic future task",
        "start_at": f"{future_date}T00:00:00+09:00",
        "due_at": f"{future_date + timedelta(days=2)}T23:59:59+09:00",
    }
