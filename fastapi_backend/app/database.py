from __future__ import annotations

import sqlite3
from pathlib import Path
from typing import Final


class Database:
    _DEFAULT_PATH: Final[Path] = Path(__file__).resolve().parent.parent / "worklog.sqlite3"

    def __init__(self, database_path: Path | None = None) -> None:
        self._database_path: Path = database_path or self._DEFAULT_PATH

    def connect(self) -> sqlite3.Connection:
        connection: sqlite3.Connection = sqlite3.connect(self._database_path)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA foreign_keys = ON")
        return connection

    def initialize(self) -> None:
        with self.connect() as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS tasks (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    parent_id INTEGER NULL REFERENCES tasks(id) ON DELETE CASCADE,
                    title TEXT NOT NULL,
                    content TEXT NOT NULL DEFAULT '',
                    start_at TEXT NOT NULL,
                    due_at TEXT NOT NULL,
                    actual_end_at TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS work_entries (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                    entry_date TEXT NOT NULL,
                    performed_content TEXT NOT NULL DEFAULT '',
                    retrospective TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    UNIQUE(task_id, entry_date)
                );

                CREATE INDEX IF NOT EXISTS idx_tasks_parent_id ON tasks(parent_id);
                CREATE INDEX IF NOT EXISTS idx_tasks_date_range ON tasks(start_at, due_at, actual_end_at);
                CREATE INDEX IF NOT EXISTS idx_work_entries_task_id ON work_entries(task_id);
                CREATE INDEX IF NOT EXISTS idx_work_entries_entry_date ON work_entries(entry_date);
                """
            )
