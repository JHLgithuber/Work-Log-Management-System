from fastapi import FastAPI, Response, status
from fastapi.middleware.cors import CORSMiddleware

from app.database import Database
from app.routes import TaskRouter


database: Database = Database()
app: FastAPI = FastAPI(title="Work Log Management System", version="0.1.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.on_event("startup")
def initialize_application() -> None:
    database.initialize()


app.include_router(TaskRouter(database).router)


@app.get("/health", status_code=status.HTTP_204_NO_CONTENT)
def health_check() -> Response:
    return Response(status_code=status.HTTP_204_NO_CONTENT)
