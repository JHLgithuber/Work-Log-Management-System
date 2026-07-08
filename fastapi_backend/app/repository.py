from __future__ import annotations

from datetime import date, datetime

from sqlalchemy import delete, func, select
from sqlalchemy.orm import Session, with_loader_criteria

from app.database import Database
from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, User, WorkEntry, WorkEntryUpsert
from app.settings import settings


class TaskNotFoundError(Exception):
    pass


class InvalidTaskHierarchyError(Exception):
    pass


class TaskRepository:
    def __init__(self, database: Database) -> None:
        self._database: Database = database

    def create(self, username: str, command: TaskCreate) -> Task:
        now: datetime = DateBounds.now()
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            self._assert_parent_exists(session, user_id, command.parent_id)
            task = Task(
                user_id=user_id,
                parent_id=command.parent_id,
                title=command.title,
                content=command.content,
                priority=command.priority,
                start_at=command.start_at,
                due_at=command.due_at,
                actual_end_at=command.actual_end_at,
                created_at=now,
                updated_at=now,
            )
            session.add(task)
            session.commit()
            task_id: int = task.id
        return self.get_required(username, task_id)

    def update(self, username: str, task_id: int, command: TaskUpdate) -> Task:
        now: datetime = DateBounds.now()
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            task: Task = self._get_required_model(session, user_id, task_id)
            self._assert_parent_exists(session, user_id, command.parent_id)
            self._assert_not_descendant_parent(session, user_id, task_id, command.parent_id)

            task.parent_id = command.parent_id
            task.title = command.title
            task.content = command.content
            task.priority = command.priority
            task.start_at = command.start_at
            task.due_at = command.due_at
            task.actual_end_at = command.actual_end_at
            task.updated_at = now
            session.commit()
        return self.get_required(username, task_id)

    def delete(self, username: str, task_id: int) -> None:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            task: Task = self._get_required_model(session, user_id, task_id)
            session.delete(task)
            session.commit()

    def get_required(self, username: str, task_id: int) -> Task:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            task: Task = self._get_required_model(session, user_id, task_id)
            self._load_graph(task)
            return task

    def list_for_date(self, username: str, target_start: datetime, target_end: datetime) -> list[Task]:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            matched_tasks: list[Task] = list(
                session.scalars(
                    select(Task).where(
                        self._task_user_filter(user_id),
                        Task.start_at <= target_end,
                        func.coalesce(Task.actual_end_at, Task.due_at) >= target_start,
                    ).options(self._task_scope(user_id))
                )
            )
            root_ids: set[int] = {self._root_id(session, user_id, task) for task in matched_tasks}
            if not root_ids:
                return []

            root_tasks: list[Task] = list(
                session.scalars(
                    select(Task)
                    .where(self._task_user_filter(user_id), Task.id.in_(root_ids))
                    .options(self._task_scope(user_id))
                    .order_by(Task.start_at, Task.id)
                )
            )
            for task in root_tasks:
                self._load_graph(task)
            return root_tasks

    def list_all(self, username: str) -> list[Task]:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            root_tasks: list[Task] = list(
                session.scalars(
                    select(Task)
                    .where(self._task_user_filter(user_id), Task.parent_id.is_(None))
                    .options(self._task_scope(user_id))
                    .order_by(Task.start_at, Task.id)
                )
            )
            for task in root_tasks:
                self._load_graph(task)
            return root_tasks

    def upsert_work_entry(self, username: str, command: WorkEntryUpsert) -> WorkEntry:
        now: datetime = DateBounds.now()
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            self._assert_task_exists(session, user_id, command.task_id)
            entry: WorkEntry | None = session.scalar(
                select(WorkEntry).where(
                    WorkEntry.task_id == command.task_id,
                    WorkEntry.entry_date == command.entry_date,
                )
            )

            if entry is None:
                entry = WorkEntry(
                    task_id=command.task_id,
                    entry_date=command.entry_date,
                    performed_content=command.performed_content,
                    retrospective=command.retrospective,
                    created_at=now,
                    updated_at=now,
                )
                session.add(entry)
            else:
                entry.performed_content = command.performed_content
                entry.retrospective = command.retrospective
                entry.updated_at = now

            session.commit()
        return self.get_work_entry(username, command.task_id, command.entry_date)

    def get_work_entry(self, username: str, task_id: int, entry_date: date) -> WorkEntry:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            self._assert_task_exists(session, user_id, task_id)
            entry: WorkEntry | None = session.scalar(
                select(WorkEntry).where(
                    WorkEntry.task_id == task_id,
                    WorkEntry.entry_date == entry_date,
                )
            )
            if entry is not None:
                return entry

        now: datetime = DateBounds.now()
        return WorkEntry(
            id=0,
            task_id=task_id,
            entry_date=entry_date,
            performed_content="",
            retrospective="",
            created_at=now,
            updated_at=now,
        )

    def delete_work_entry(self, username: str, task_id: int, entry_date: date) -> None:
        with self._database.session() as session:
            user_id: int | None = self._resolve_user_id(session, username)
            self._assert_task_exists(session, user_id, task_id)
            session.execute(
                delete(WorkEntry).where(
                    WorkEntry.task_id == task_id,
                    WorkEntry.entry_date == entry_date,
                )
            )
            session.commit()

    def _resolve_user_id(self, session: Session, username: str) -> int | None:
        normalized_username: str = username.strip()
        if (
            settings.admin_username is not None
            and settings.admin_password is not None
            and normalized_username == settings.admin_username
        ):
            return None

        user: User | None = session.scalar(select(User).where(User.username == normalized_username))
        if user is None:
            raise TaskNotFoundError(f"User {normalized_username} was not found.")
        return user.id

    def _get_required_model(self, session: Session, user_id: int | None, task_id: int) -> Task:
        task: Task | None = session.scalar(
            select(Task)
            .where(Task.id == task_id, self._task_user_filter(user_id))
            .options(self._task_scope(user_id))
        )
        if task is None:
            raise TaskNotFoundError(f"Task {task_id} was not found.")
        return task

    def _task_user_filter(self, user_id: int | None) -> object:
        return Task.user_id.is_(None) if user_id is None else Task.user_id == user_id

    def _task_scope(self, user_id: int | None) -> object:
        if user_id is None:
            return with_loader_criteria(Task, lambda task: task.user_id.is_(None), include_aliases=True)
        return with_loader_criteria(Task, lambda task: task.user_id == user_id, include_aliases=True)

    def _assert_parent_exists(self, session: Session, user_id: int | None, parent_id: int | None) -> None:
        if parent_id is None:
            return
        self._assert_task_exists(session, user_id, parent_id)

    def _assert_task_exists(self, session: Session, user_id: int | None, task_id: int) -> None:
        self._get_required_model(session, user_id, task_id)

    def _assert_not_descendant_parent(
        self,
        session: Session,
        user_id: int | None,
        task_id: int,
        parent_id: int | None,
    ) -> None:
        current_id: int | None = parent_id
        while current_id is not None:
            if current_id == task_id:
                raise InvalidTaskHierarchyError("A task cannot be moved under one of its descendants.")
            current: Task = self._get_required_model(session, user_id, current_id)
            current_id = current.parent_id

    def _root_id(self, session: Session, user_id: int | None, task: Task) -> int:
        current: Task = task
        while current.parent_id is not None:
            current = self._get_required_model(session, user_id, current.parent_id)
        return current.id

    def _load_graph(self, task: Task) -> None:
        list(task.entries)
        for child in list(task.children):
            self._load_graph(child)
