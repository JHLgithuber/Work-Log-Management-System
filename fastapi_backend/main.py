from fastapi import FastAPI, Response, status
from fastapi.middleware.cors import CORSMiddleware

from app.auth_routes import AuthRouter
from app.database import Database
from app.routes import TaskRouter
from app.settings import settings


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
