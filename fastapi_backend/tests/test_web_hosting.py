"""Exercise the real entry point using synthetic files and an isolated test DB."""
from pathlib import Path
import runpy
import shutil
from unittest.mock import patch

from fastapi.testclient import TestClient
import pytest


@pytest.fixture
def application_factory(tmp_path, database):
    # A temporary entry point resolves its adjacent web directory to synthetic
    # assets. Never inspect a developer's bundle, .env, or production database.
    entry_point = tmp_path / "main.py"
    shutil.copyfile(Path(__file__).resolve().parents[1] / "main.py", entry_point)

    def create_application(*, bundled=True):
        if bundled:
            web_root = tmp_path / "web"
            (web_root / "_framework").mkdir(parents=True)
            (web_root / "index.html").write_text("<html>Synthetic browser UI</html>")
            (web_root / "main.js").write_text("export const synthetic = true;")
            (web_root / "_framework" / "runtime.wasm").write_bytes(b"\x00asm\x01\x00\x00\x00")
            # Verify that real API routes win even over colliding asset names.
            (web_root / "health").write_text("not a health response")
            (web_root / "docs").write_text("not API documentation")
            (web_root / "openapi.json").write_text("not an OpenAPI document")
            (web_root / "tasks").write_text("not a task response")
            (web_root / "auth").mkdir()
            (web_root / "auth" / "me").write_text("not a user response")
            (web_root / "auth" / "login").write_text("not a login response")
            (web_root / "assets").mkdir()
            (web_root / "assets" / "index.html").write_text("Synthetic asset directory")
        with patch("app.database.Database", return_value=database):
            return runpy.run_path(str(entry_point))["app"]

    return create_application


def test_bundle_serves_index_assets_and_wasm_mime_type(application_factory):
    app = application_factory()
    assert app.routes[-1].name == "web"
    with TestClient(app) as client:
        index = client.get("/")
        assert index.status_code == 200
        assert index.headers["content-type"].startswith("text/html")
        assert "Synthetic browser UI" in index.text
        script = client.get("/main.js")
        assert script.status_code == 200
        assert "javascript" in script.headers["content-type"]
        wasm = client.get("/_framework/runtime.wasm")
        assert wasm.status_code == 200
        assert wasm.headers["content-type"] == "application/wasm"
        assert wasm.content.startswith(b"\x00asm")
        assert client.head("/main.js").status_code == 200


def test_bundle_does_not_shadow_health_documentation_or_protected_api(application_factory, user_headers):
    with TestClient(application_factory()) as client:
        health = client.get("/health")
        assert health.status_code == 204 and health.content == b""
        assert "swagger-ui" in client.get("/docs").text
        assert "/tasks" in client.get("/openapi.json").json()["paths"]
        for path in ["/tasks", "/auth/me"]:
            response = client.get(path)
            assert response.status_code == 401
            assert response.headers["content-type"] == "application/json"
        assert client.get("/tasks", headers=user_headers).json() == []
        assert client.get("/auth/me", headers=user_headers).json()["username"] == "qa-user"
        login = client.post(
            "/auth/login", json={"username": "qa-user", "password": "synthetic-user-password"}
        )
        assert login.status_code == 200 and login.json()["access_token"]


@pytest.mark.parametrize("path", ["/missing.js", "/unknown", "/auth/unknown", "/tasks/1/unknown"])
def test_unknown_paths_do_not_fall_back_to_index_html(application_factory, path):
    with TestClient(application_factory()) as client:
        response = client.get(path)
        assert response.status_code == 404
        assert response.headers["content-type"] == "application/json"
        assert "Synthetic browser UI" not in response.text


def test_static_mount_does_not_expose_parent_files_or_accept_writes(application_factory, tmp_path):
    with TestClient(application_factory()) as client:
        (tmp_path / "private.txt").write_text("synthetic private marker")
        for path in ["/%2e%2e/private.txt", "/_framework/%2e%2e/%2e%2e/private.txt"]:
            response = client.get(path)
            assert response.status_code == 404
            assert "synthetic private marker" not in response.text
        assert client.post("/main.js", content="replacement").status_code == 405
        assert client.get("/main.js").text == "export const synthetic = true;"


def test_missing_bundle_reports_unavailable_without_breaking_api(application_factory):
    with TestClient(application_factory(bundled=False)) as client:
        response = client.get("/")
        assert response.status_code == 503
        assert response.headers["content-type"].startswith("text/plain")
        assert "repository-root Dockerfile" in response.text
        assert client.get("/health").status_code == 204
        assert client.get("/tasks").status_code == 401
        assert client.get("/auth/me").status_code == 401
        assert client.get("/main.js").status_code == 404
        assert "/" not in client.get("/openapi.json").json()["paths"]


@pytest.mark.parametrize("bundled", [False, True])
@pytest.mark.parametrize(
    "method,path",
    [("GET", "/tasks"), ("GET", "/auth/me"), ("POST", "/auth/login"),
     ("GET", "/health"), ("GET", "/docs"), ("GET", "/openapi.json")],
)
def test_registered_routes_retain_slash_redirects_and_query(application_factory, bundled, method, path):
    with TestClient(application_factory(bundled=bundled)) as client:
        response = client.request(method, path + "/?probe=value", follow_redirects=False)
        assert response.status_code == 307
        assert response.headers["location"] == "http://testserver" + path + "?probe=value"


@pytest.mark.parametrize("bundled", [False, True])
def test_login_redirect_preserves_post_body_and_wrong_method_is_not_static(application_factory, bundled, user_headers):
    with TestClient(application_factory(bundled=bundled)) as client:
        response = client.post(
            "/auth/login/", json={"username": "qa-user", "password": "synthetic-user-password"},
            follow_redirects=True,
        )
        assert [item.status_code for item in response.history] == [307]
        assert response.status_code == 200 and response.json()["access_token"]
        wrong_method = client.get("/auth/login")
        assert wrong_method.status_code == 405
        assert wrong_method.headers["content-type"] == "application/json"


def test_static_directory_redirects_remain_owned_by_static_files(application_factory):
    with TestClient(application_factory()) as client:
        response = client.get("/assets?probe=value", follow_redirects=False)
        assert response.status_code == 307
        assert response.headers["location"] == "http://testserver/assets/?probe=value"
        assert client.get("/assets/").text == "Synthetic asset directory"
