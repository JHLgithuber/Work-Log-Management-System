from fastapi import FastAPI
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


@app.get("/health")
def health_check() -> dict[str, str]:
    return {"status": "ok"}
