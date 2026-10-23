from pathlib import Path

from fastapi import FastAPI, Response, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import PlainTextResponse

from app.auth_routes import AuthRouter
from app.database import Database
from app.routes import TaskRouter
from app.settings import settings
from app.web_hosting import WebStaticMount


database: Database = Database(database_url=settings.database_url)
app: FastAPI = FastAPI(title=settings.app_title, version=settings.app_version)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins,
    allow_credentials=settings.cors_allow_credentials,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.on_event("startup")
def initialize_application() -> None:
    database.initialize()


app.include_router(AuthRouter(database).router)
app.include_router(TaskRouter(database).router)


@app.get("/health", status_code=status.HTTP_204_NO_CONTENT)
def health_check() -> Response:
    return Response(status_code=status.HTTP_204_NO_CONTENT)


# Mount last: API, health, and documentation routes must take precedence.
# Keep API-only development usable when the browser bundle is not present.
web_root = Path(__file__).resolve().parent / "web"
if (web_root / "index.html").is_file():
    app.router.routes.append(WebStaticMount(web_root, app.router.routes))
else:
    @app.get("/", include_in_schema=False, response_class=PlainTextResponse, status_code=503)
    def web_bundle_missing() -> str:
        return "Avalonia browser UI is not installed. Build the repository-root Dockerfile for the bundled application."
