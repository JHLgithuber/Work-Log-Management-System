from pathlib import Path
from collections.abc import Sequence

from starlette.routing import BaseRoute, Match, Mount
from starlette.staticfiles import StaticFiles
from starlette.types import Scope


class WebStaticMount(Mount):
    """Let FastAPI handle registered API paths before the catch-all UI mount.

    A normal root Mount is a full match even when an earlier API route only
    matched the path (wrong method), or would match after a slash redirect.
    Declining those paths preserves the parent router's 405/307 behavior.
    """

    def __init__(self, directory: Path, api_routes: Sequence[BaseRoute]) -> None:
        super().__init__("/", app=StaticFiles(directory=directory, html=True), name="web")
        self._api_routes = tuple(api_routes)

    def matches(self, scope: Scope) -> tuple[Match, Scope]:
        if scope["type"] == "http":
            candidates = [scope]
            if scope["path"] != "/":
                redirect_scope = dict(scope)
                path = scope["path"]
                redirect_scope["path"] = path.rstrip("/") if path.endswith("/") else path + "/"
                candidates.append(redirect_scope)
            if any(
                route.matches(candidate)[0] != Match.NONE
                for candidate in candidates
                for route in self._api_routes
            ):
                return Match.NONE, {}
        return super().matches(scope)
