from __future__ import annotations

from collections.abc import Iterator
from contextlib import contextmanager
from pathlib import Path
from typing import Final

from sqlalchemy import create_engine, event
from sqlalchemy.engine import Engine
from sqlalchemy.orm import Session, sessionmaker

from app.domain import Base


class Database:
    _DEFAULT_PATH: Final[Path] = Path(__file__).resolve().parent.parent / "worklog.sqlite3"

    def __init__(self, database_path: Path | None = None) -> None:
        self._database_path: Path = database_path or self._DEFAULT_PATH
        self._engine: Engine = create_engine(
            f"sqlite:///{self._database_path}",
            connect_args={"check_same_thread": False},
        )
        self._session_factory: sessionmaker[Session] = sessionmaker(
            bind=self._engine,
            expire_on_commit=False,
        )
        event.listen(self._engine, "connect", self._enable_foreign_keys)

    @contextmanager
    def session(self) -> Iterator[Session]:
        with self._session_factory() as session:
            yield session

    def initialize(self) -> None:
        Base.metadata.create_all(self._engine)

    def _enable_foreign_keys(self, dbapi_connection: object, connection_record: object) -> None:
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA foreign_keys = ON")
        cursor.close()
