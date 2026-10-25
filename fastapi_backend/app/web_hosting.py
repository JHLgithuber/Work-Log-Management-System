from pathlib import Path
from functools import lru_cache
import hashlib
import os

import anyio
from collections.abc import Sequence

from starlette.routing import BaseRoute, Match, Mount
from starlette.staticfiles import StaticFiles, NotModifiedResponse
from starlette.datastructures import Headers
from starlette.responses import FileResponse, Response
from starlette.types import Scope


@lru_cache(maxsize=512)
def _content_etag(path: str, identity: tuple[int, ...]) -> str:
    # identity includes ctime/inode so replacing same-size, same-mtime bytes
    # invalidates this hash cache. File hashing runs outside the event loop.
    with open(path, "rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    return f'"sha256-{digest}"'


class RevalidatedWebFiles(StaticFiles):
    """Revalidate static UI files using their bytes, not mtime and size."""

    def file_response(self, full_path, stat_result, scope, status_code=200) -> Response:
        # Do not allow the parent class's stat-based conditional response.
        return FileResponse(full_path, status_code=status_code, stat_result=stat_result)

    async def get_response(self, path: str, scope: Scope) -> Response:
        response = await super().get_response(path, scope)
        response.headers["Cache-Control"] = "no-cache"
        if isinstance(response, FileResponse) and response.status_code == 200:
            stat = response.stat_result
            if stat is None:
                stat = await anyio.to_thread.run_sync(os.stat, response.path)
            identity = (stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns)
            etag = await anyio.to_thread.run_sync(_content_etag, str(response.path), identity)
            response.headers["ETag"] = etag
            # Last-Modified alone cannot distinguish equal-mtime replacements.
            # Prefer a full response for legacy IMS-only requests.
            supplied = Headers(scope=scope).get("if-none-match")
            if supplied and any(tag.strip().removeprefix("W/") in ("*", etag)
                                for tag in supplied.split(",")):
                return NotModifiedResponse(response.headers)
        return response


class WebStaticMount(Mount):
    """Let FastAPI handle registered API paths before the catch-all UI mount.

    A normal root Mount is a full match even when an earlier API route only
    matched the path (wrong method), or would match after a slash redirect.
    Declining those paths preserves the parent router's 405/307 behavior.
    """

    def __init__(self, directory: Path, api_routes: Sequence[BaseRoute]) -> None:
        super().__init__("/", app=RevalidatedWebFiles(directory=directory, html=True), name="web")
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
