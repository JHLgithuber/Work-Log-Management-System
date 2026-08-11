from pathlib import Path

from fastapi import FastAPI, Response, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import PlainTextResponse
from fastapi.staticfiles import StaticFiles

from app.auth_routes import AuthRouter
from app.database import Database
from app.routes import TaskRouter
from app.settings import settings


database: Database = Database(database_url=settings.database_url)
app: FastAPI = FastAPI(title=settings.app_title, version=settings.app_version)
web_root: Path = Path(__file__).resolve().parent / "web"

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


if (web_root / "index.html").is_file():
    app.mount("/", StaticFiles(directory=web_root, html=True), name="web")
else:
    @app.get("/", include_in_schema=False, response_class=PlainTextResponse)
    def frontend_not_built() -> PlainTextResponse:
        return PlainTextResponse(
            "Avalonia browser UI has not been built. Run: sh fastapi_backend/build-web.sh",
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
        )
