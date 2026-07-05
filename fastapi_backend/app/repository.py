from __future__ import annotations

from datetime import date, datetime, timezone

from sqlalchemy import delete, func, select
from sqlalchemy.dialects.sqlite import insert as sqlite_insert
from sqlalchemy.orm import Session

from app.database import Database
from app.domain import Task, TaskCreate, TaskUpdate, WorkEntry, WorkEntryUpsert


class TaskNotFoundError(Exception):
    pass


class InvalidTaskHierarchyError(Exception):
    pass


class TaskRepository:
    def __init__(self, database: Database) -> None:
        self._database: Database = database

    def create(self, command: TaskCreate) -> Task:
        now: datetime = datetime.now(timezone.utc)
        with self._database.session() as session:
            self._assert_parent_exists(session, command.parent_id)
            task = Task(
                parent_id=command.parent_id,
                title=command.title,
                content=command.content,
                start_at=command.start_at,
                due_at=command.due_at,
                actual_end_at=command.actual_end_at,
                created_at=now,
                updated_at=now,
            )
            session.add(task)
            session.commit()
            task_id: int = task.id
        return self.get_required(task_id)

    def update(self, task_id: int, command: TaskUpdate) -> Task:
        now: datetime = datetime.now(timezone.utc)
        with self._database.session() as session:
            task: Task = self._get_required_model(session, task_id)
            self._assert_parent_exists(session, command.parent_id)
            self._assert_not_descendant_parent(session, task_id, command.parent_id)

            task.parent_id = command.parent_id
            task.title = command.title
            task.content = command.content
            task.start_at = command.start_at
            task.due_at = command.due_at
            task.actual_end_at = command.actual_end_at
            task.updated_at = now
            session.commit()
        return self.get_required(task_id)

    def delete(self, task_id: int) -> None:
        with self._database.session() as session:
            task: Task = self._get_required_model(session, task_id)
            session.delete(task)
            session.commit()

    def get_required(self, task_id: int) -> Task:
        with self._database.session() as session:
            task: Task = self._get_required_model(session, task_id)
            self._load_graph(task)
            return task

    def list_for_date(self, target_start: datetime, target_end: datetime) -> list[Task]:
        with self._database.session() as session:
            matched_tasks: list[Task] = list(
                session.scalars(
                    select(Task).where(
                        Task.start_at <= target_end,
                        func.coalesce(Task.actual_end_at, Task.due_at) >= target_start,
                    )
                )
            )
            root_ids: set[int] = {self._root_id(session, task) for task in matched_tasks}
            if not root_ids:
                return []

            root_tasks: list[Task] = list(
                session.scalars(
                    select(Task)
                    .where(Task.id.in_(root_ids))
                    .order_by(Task.start_at, Task.id)
                )
            )
            for task in root_tasks:
                self._load_graph(task)
            return root_tasks

    def list_all(self) -> list[Task]:
        with self._database.session() as session:
            root_tasks: list[Task] = list(
                session.scalars(
                    select(Task)
                    .where(Task.parent_id.is_(None))
                    .order_by(Task.start_at, Task.id)
                )
            )
            for task in root_tasks:
                self._load_graph(task)
            return root_tasks

    def upsert_work_entry(self, command: WorkEntryUpsert) -> WorkEntry:
        now: datetime = datetime.now(timezone.utc)
        with self._database.session() as session:
            self._assert_task_exists(session, command.task_id)
            statement = sqlite_insert(WorkEntry).values(
                task_id=command.task_id,
                entry_date=command.entry_date,
                performed_content=command.performed_content,
                retrospective=command.retrospective,
                created_at=now,
                updated_at=now,
            )
            session.execute(
                statement.on_conflict_do_update(
                    index_elements=[WorkEntry.task_id, WorkEntry.entry_date],
                    set_={
                        "performed_content": statement.excluded.performed_content,
                        "retrospective": statement.excluded.retrospective,
                        "updated_at": statement.excluded.updated_at,
                    },
                )
            )
            session.commit()
        return self.get_work_entry(command.task_id, command.entry_date)

    def get_work_entry(self, task_id: int, entry_date: date) -> WorkEntry:
        with self._database.session() as session:
            self._assert_task_exists(session, task_id)
            entry: WorkEntry | None = session.scalar(
                select(WorkEntry).where(
                    WorkEntry.task_id == task_id,
                    WorkEntry.entry_date == entry_date,
                )
            )
            if entry is not None:
                return entry

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

    def delete_work_entry(self, task_id: int, entry_date: date) -> None:
        with self._database.session() as session:
            self._assert_task_exists(session, task_id)
            session.execute(
                delete(WorkEntry).where(
                    WorkEntry.task_id == task_id,
                    WorkEntry.entry_date == entry_date,
                )
            )
            session.commit()

    def _get_required_model(self, session: Session, task_id: int) -> Task:
        task: Task | None = session.get(Task, task_id)
        if task is None:
            raise TaskNotFoundError(f"Task {task_id} was not found.")
        return task

    def _assert_parent_exists(self, session: Session, parent_id: int | None) -> None:
        if parent_id is None:
            return
        self._assert_task_exists(session, parent_id)

    def _assert_task_exists(self, session: Session, task_id: int) -> None:
        self._get_required_model(session, task_id)

    def _assert_not_descendant_parent(
        self,
        session: Session,
        task_id: int,
        parent_id: int | None,
    ) -> None:
        current_id: int | None = parent_id
        while current_id is not None:
            if current_id == task_id:
                raise InvalidTaskHierarchyError("A task cannot be moved under one of its descendants.")
            current: Task | None = session.get(Task, current_id)
            current_id = None if current is None else current.parent_id

    def _root_id(self, session: Session, task: Task) -> int:
        current: Task = task
        while current.parent_id is not None:
            current = self._get_required_model(session, current.parent_id)
        return current.id

    def _load_graph(self, task: Task) -> None:
        list(task.entries)
        for child in list(task.children):
            self._load_graph(child)
