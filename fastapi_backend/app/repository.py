from __future__ import annotations

import sqlite3
from datetime import date, datetime, timezone
from typing import Iterable

from app.database import Database
from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, WorkEntry, WorkEntryUpsert


class TaskNotFoundError(Exception):
    pass


class InvalidTaskHierarchyError(Exception):
    pass


class TaskRepository:
    def __init__(self, database: Database) -> None:
        self._database: Database = database

    def create(self, command: TaskCreate) -> Task:
        now: datetime = datetime.now(timezone.utc)
        with self._database.connect() as connection:
            self._assert_parent_exists(connection, command.parent_id)
            cursor: sqlite3.Cursor = connection.execute(
                """
                INSERT INTO tasks (
                    parent_id, title, content, start_at, due_at,
                    actual_end_at, created_at, updated_at
                )
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    command.parent_id,
                    command.title,
                    command.content,
                    self._to_storage(command.start_at),
                    self._to_storage(command.due_at),
                    self._to_optional_storage(command.actual_end_at),
                    self._to_storage(now),
                    self._to_storage(now),
                ),
            )
            connection.commit()
            return self.get_required(int(cursor.lastrowid))

    def update(self, task_id: int, command: TaskUpdate) -> Task:
        now: datetime = datetime.now(timezone.utc)
        with self._database.connect() as connection:
            self._assert_task_exists(connection, task_id)
            self._assert_parent_exists(connection, command.parent_id)
            self._assert_not_descendant_parent(connection, task_id, command.parent_id)
            connection.execute(
                """
                UPDATE tasks
                SET parent_id = ?,
                    title = ?,
                    content = ?,
                    start_at = ?,
                    due_at = ?,
                    actual_end_at = ?,
                    updated_at = ?
                WHERE id = ?
                """,
                (
                    command.parent_id,
                    command.title,
                    command.content,
                    self._to_storage(command.start_at),
                    self._to_storage(command.due_at),
                    self._to_optional_storage(command.actual_end_at),
                    self._to_storage(now),
                    task_id,
                ),
            )
            connection.commit()
            return self.get_required(task_id)

    def delete(self, task_id: int) -> None:
        with self._database.connect() as connection:
            cursor: sqlite3.Cursor = connection.execute("DELETE FROM tasks WHERE id = ?", (task_id,))
            if cursor.rowcount == 0:
                raise TaskNotFoundError(f"Task {task_id} was not found.")
            connection.commit()

    def get_required(self, task_id: int) -> Task:
        with self._database.connect() as connection:
            row: sqlite3.Row | None = connection.execute(
                "SELECT * FROM tasks WHERE id = ?",
                (task_id,),
            ).fetchone()
            if row is None:
                raise TaskNotFoundError(f"Task {task_id} was not found.")
            children: list[Task] = self._load_children(connection, task_id)
            entries: list[WorkEntry] = self._load_entries(connection, task_id)
            return self._map_row(row, children, entries)

    def list_for_date(self, target_start: datetime, target_end: datetime) -> list[Task]:
        start_value: str = self._to_storage(target_start)
        end_value: str = self._to_storage(target_end)
        with self._database.connect() as connection:
            rows: list[sqlite3.Row] = connection.execute(
                """
                SELECT DISTINCT root.*
                FROM tasks matched
                JOIN tasks root
                    ON root.id = COALESCE(
                        (
                            WITH RECURSIVE ancestors(id, parent_id) AS (
                                SELECT id, parent_id FROM tasks WHERE id = matched.id
                                UNION ALL
                                SELECT tasks.id, tasks.parent_id
                                FROM tasks
                                JOIN ancestors ON tasks.id = ancestors.parent_id
                            )
                            SELECT id FROM ancestors WHERE parent_id IS NULL LIMIT 1
                        ),
                        matched.id
                    )
                WHERE matched.start_at <= ?
                  AND COALESCE(matched.actual_end_at, matched.due_at) >= ?
                ORDER BY root.start_at, root.id
                """,
                (end_value, start_value),
            ).fetchall()
            return [
                self._map_row(
                    row,
                    self._load_children(connection, int(row["id"])),
                    self._load_entries(connection, int(row["id"])),
                )
                for row in rows
            ]

    def list_all(self) -> list[Task]:
        with self._database.connect() as connection:
            rows: list[sqlite3.Row] = connection.execute(
                "SELECT * FROM tasks WHERE parent_id IS NULL ORDER BY start_at, id"
            ).fetchall()
            return [
                self._map_row(
                    row,
                    self._load_children(connection, int(row["id"])),
                    self._load_entries(connection, int(row["id"])),
                )
                for row in rows
            ]

    def upsert_work_entry(self, command: WorkEntryUpsert) -> WorkEntry:
        now: datetime = datetime.now(timezone.utc)
        with self._database.connect() as connection:
            self._assert_task_exists(connection, command.task_id)
            connection.execute(
                """
                INSERT INTO work_entries (
                    task_id, entry_date, performed_content, retrospective, created_at, updated_at
                )
                VALUES (?, ?, ?, ?, ?, ?)
                ON CONFLICT(task_id, entry_date) DO UPDATE SET
                    performed_content = excluded.performed_content,
                    retrospective = excluded.retrospective,
                    updated_at = excluded.updated_at
                """,
                (
                    command.task_id,
                    self._date_to_storage(command.entry_date),
                    command.performed_content,
                    command.retrospective,
                    self._to_storage(now),
                    self._to_storage(now),
                ),
            )
            connection.commit()
            return self.get_work_entry(command.task_id, command.entry_date)

    def get_work_entry(self, task_id: int, entry_date: date) -> WorkEntry:
        with self._database.connect() as connection:
            self._assert_task_exists(connection, task_id)
            row: sqlite3.Row | None = connection.execute(
                """
                SELECT * FROM work_entries
                WHERE task_id = ? AND entry_date = ?
                """,
                (task_id, self._date_to_storage(entry_date)),
            ).fetchone()
            if row is None:
                now: datetime = datetime.now(timezone.utc)
                return WorkEntry(
                    id=0,
                    task_id=task_id,
                    entry_date=entry_date,
                    performed_content="",
                    retrospective="",
                    created_at=now,
                    updated_at=now,
                )
            return self._map_work_entry(row)

    def delete_work_entry(self, task_id: int, entry_date: date) -> None:
        with self._database.connect() as connection:
            self._assert_task_exists(connection, task_id)
            connection.execute(
                "DELETE FROM work_entries WHERE task_id = ? AND entry_date = ?",
                (task_id, self._date_to_storage(entry_date)),
            )
            connection.commit()

    def _load_children(self, connection: sqlite3.Connection, parent_id: int) -> list[Task]:
        rows: Iterable[sqlite3.Row] = connection.execute(
            "SELECT * FROM tasks WHERE parent_id = ? ORDER BY start_at, id",
            (parent_id,),
        ).fetchall()
        return [
            self._map_row(
                row,
                self._load_children(connection, int(row["id"])),
                self._load_entries(connection, int(row["id"])),
            )
            for row in rows
        ]

    def _load_entries(self, connection: sqlite3.Connection, task_id: int) -> list[WorkEntry]:
        rows: Iterable[sqlite3.Row] = connection.execute(
            "SELECT * FROM work_entries WHERE task_id = ? ORDER BY entry_date",
            (task_id,),
        ).fetchall()
        return [self._map_work_entry(row) for row in rows]

    def _assert_parent_exists(self, connection: sqlite3.Connection, parent_id: int | None) -> None:
        if parent_id is None:
            return
        self._assert_task_exists(connection, parent_id)

    def _assert_task_exists(self, connection: sqlite3.Connection, task_id: int) -> None:
        row: sqlite3.Row | None = connection.execute("SELECT id FROM tasks WHERE id = ?", (task_id,)).fetchone()
        if row is None:
            raise TaskNotFoundError(f"Task {task_id} was not found.")

    def _assert_not_descendant_parent(
        self,
        connection: sqlite3.Connection,
        task_id: int,
        parent_id: int | None,
    ) -> None:
        if parent_id is None:
            return
        if parent_id == task_id:
            raise InvalidTaskHierarchyError("A task cannot be its own parent.")
        descendant_ids: set[int] = {
            int(row["id"])
            for row in connection.execute(
                """
                WITH RECURSIVE descendants(id) AS (
                    SELECT id FROM tasks WHERE parent_id = ?
                    UNION ALL
                    SELECT tasks.id FROM tasks
                    JOIN descendants ON tasks.parent_id = descendants.id
                )
                SELECT id FROM descendants
                """,
                (task_id,),
            ).fetchall()
        }
        if parent_id in descendant_ids:
            raise InvalidTaskHierarchyError("A task cannot be moved under one of its descendants.")

    def _map_row(
        self,
        row: sqlite3.Row,
        children: list[Task] | None = None,
        entries: list[WorkEntry] | None = None,
    ) -> Task:
        return Task(
            id=int(row["id"]),
            parent_id=int(row["parent_id"]) if row["parent_id"] is not None else None,
            title=str(row["title"]),
            content=str(row["content"]),
            start_at=self._from_storage(str(row["start_at"])),
            due_at=self._from_storage(str(row["due_at"])),
            actual_end_at=self._from_optional_storage(row["actual_end_at"]),
            created_at=self._from_storage(str(row["created_at"])),
            updated_at=self._from_storage(str(row["updated_at"])),
            entries=entries or [],
            children=children or [],
        )

    def _map_work_entry(self, row: sqlite3.Row) -> WorkEntry:
        return WorkEntry(
            id=int(row["id"]),
            task_id=int(row["task_id"]),
            entry_date=self._date_from_storage(str(row["entry_date"])),
            performed_content=str(row["performed_content"]),
            retrospective=str(row["retrospective"]),
            created_at=self._from_storage(str(row["created_at"])),
            updated_at=self._from_storage(str(row["updated_at"])),
        )

    def _to_optional_storage(self, value: datetime | None) -> str | None:
        return None if value is None else self._to_storage(value)

    def _to_storage(self, value: datetime) -> str:
        return DateBounds.normalize(value).isoformat(timespec="microseconds")

    def _date_to_storage(self, value: date) -> str:
        return value.isoformat()

    def _date_from_storage(self, value: str) -> date:
        return date.fromisoformat(value)

    def _from_optional_storage(self, value: object) -> datetime | None:
        if value is None:
            return None
        return self._from_storage(str(value))

    def _from_storage(self, value: str) -> datetime:
        return datetime.fromisoformat(value)
