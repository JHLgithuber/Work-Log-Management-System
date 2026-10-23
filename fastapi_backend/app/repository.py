from __future__ import annotations

from datetime import date, datetime

from sqlalchemy import delete, select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import with_loader_criteria
from sqlalchemy.orm.attributes import set_committed_value

from app.database import Database
from app.domain import (
    DailyNote,
    DailyNoteUpsert,
    DateBounds,
    Task,
    TaskCreate,
    TaskUpdate,
    User,
    WorkEntry,
    WorkEntryUpsert,
)
from app.settings import settings


class TaskNotFoundError(Exception):
    pass


class InvalidTaskHierarchyError(Exception):
    pass


class TaskRepository:
    def __init__(self, database: Database) -> None:
        self._database: Database = database

    async def create(self, username: str, command: TaskCreate) -> Task:
        now: datetime = DateBounds.now()
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            await self._assert_parent_exists(session, user_id, command.parent_id)
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
            await session.commit()
            task_id: int = task.id
        return await self.get_required(username, task_id)

    async def update(self, username: str, task_id: int, command: TaskUpdate) -> Task:
        now: datetime = DateBounds.now()
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            task: Task = await self._get_required_model(session, user_id, task_id)
            await self._assert_parent_exists(session, user_id, command.parent_id)
            await self._assert_not_descendant_parent(session, user_id, task_id, command.parent_id)

            task.parent_id = command.parent_id
            task.title = command.title
            task.content = command.content
            task.priority = command.priority
            task.start_at = command.start_at
            task.due_at = command.due_at
            task.actual_end_at = command.actual_end_at
            task.updated_at = now
            await session.commit()
        return await self.get_required(username, task_id)

    async def delete(self, username: str, task_id: int) -> None:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            task: Task = await self._get_required_model(session, user_id, task_id)
            await session.delete(task)
            await session.commit()

    async def get_required(self, username: str, task_id: int) -> Task:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            tasks: list[Task] = await self._load_task_graph(session, user_id)
            task = next((item for item in tasks if item.id == task_id), None)
            if task is None:
                raise TaskNotFoundError(f"Task {task_id} was not found.")
            return task

    async def list_for_date(
        self,
        username: str,
        target_start: datetime,
        target_end: datetime,
        focused_task_id: int | None = None,
    ) -> list[Task]:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            tasks: list[Task] = await self._load_task_graph(session, user_id)
            tasks_by_id: dict[int, Task] = {task.id: task for task in tasks}
            root_ids: set[int] = {
                self._root_id_from_loaded_graph(tasks_by_id, task)
                for task in tasks
                if task.start_at <= target_end and (task.actual_end_at or task.due_at) >= target_start
            }
            focused_task = tasks_by_id.get(focused_task_id) if focused_task_id is not None else None
            if focused_task is not None:
                root_ids.add(self._root_id_from_loaded_graph(tasks_by_id, focused_task))
            return [
                task
                for task in tasks
                if task.id in root_ids
            ]

    async def list_all(self, username: str) -> list[Task]:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            tasks: list[Task] = await self._load_task_graph(session, user_id)
            task_ids = {task.id for task in tasks}
            return [task for task in tasks if task.parent_id is None or task.parent_id not in task_ids]

    async def upsert_work_entry(self, username: str, command: WorkEntryUpsert) -> WorkEntry:
        now: datetime = DateBounds.now()
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            await self._assert_task_exists(session, user_id, command.task_id)
            entry: WorkEntry | None = await session.scalar(
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

            await session.commit()
        return await self.get_work_entry(username, command.task_id, command.entry_date)

    async def get_work_entry(self, username: str, task_id: int, entry_date: date) -> WorkEntry:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            await self._assert_task_exists(session, user_id, task_id)
            entry: WorkEntry | None = await session.scalar(
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

    async def delete_work_entry(self, username: str, task_id: int, entry_date: date) -> None:
        async with self._database.session() as session:
            user_id: int | None = await self._resolve_user_id(session, username)
            await self._assert_task_exists(session, user_id, task_id)
            await session.execute(
                delete(WorkEntry).where(
                    WorkEntry.task_id == task_id,
                    WorkEntry.entry_date == entry_date,
                )
            )
            await session.commit()

    async def get_daily_note(self, username: str, note_date: date) -> DailyNote:
        normalized_username: str = username.strip()
        async with self._database.session() as session:
            note: DailyNote | None = await session.scalar(
                select(DailyNote).where(
                    DailyNote.username == normalized_username,
                    DailyNote.note_date == note_date,
                )
            )
            if note is not None:
                return note

        now: datetime = DateBounds.now()
        return DailyNote(
            id=0,
            username=normalized_username,
            note_date=note_date,
            content="",
            created_at=now,
            updated_at=now,
        )

    async def upsert_daily_note(self, username: str, command: DailyNoteUpsert) -> DailyNote:
        normalized_username: str = username.strip()
        now: datetime = DateBounds.now()
        async with self._database.session() as session:
            note: DailyNote | None = await session.scalar(
                select(DailyNote).where(
                    DailyNote.username == normalized_username,
                    DailyNote.note_date == command.note_date,
                )
            )
            if note is None:
                note = DailyNote(
                    username=normalized_username,
                    note_date=command.note_date,
                    content=command.content,
                    created_at=now,
                    updated_at=now,
                )
                session.add(note)
            else:
                note.content = command.content
                note.updated_at = now
            await session.commit()
        return await self.get_daily_note(normalized_username, command.note_date)

    async def list_daily_notes(self, username: str, start_date: date, end_date: date) -> list[DailyNote]:
        normalized_username: str = username.strip()
        async with self._database.session() as session:
            result = await session.scalars(
                select(DailyNote)
                .where(
                    DailyNote.username == normalized_username,
                    DailyNote.note_date >= start_date,
                    DailyNote.note_date <= end_date,
                )
                .order_by(DailyNote.note_date)
            )
            return list(result)

    async def _load_task_graph(self, session: AsyncSession, user_id: int | None) -> list[Task]:
        result = await session.scalars(
            select(Task)
            .where(self._task_user_filter(user_id))
            .options(self._task_scope(user_id))
            .order_by(Task.start_at, Task.id)
        )
        tasks: list[Task] = list(result)
        if not tasks:
            return []

        task_ids = [task.id for task in tasks]
        entry_result = await session.scalars(
            select(WorkEntry)
            .where(WorkEntry.task_id.in_(task_ids))
            .order_by(WorkEntry.entry_date, WorkEntry.id)
        )
        entries_by_task_id: dict[int, list[WorkEntry]] = {task_id: [] for task_id in task_ids}
        for entry in entry_result:
            entries_by_task_id.setdefault(entry.task_id, []).append(entry)

        children_by_parent_id: dict[int, list[Task]] = {task_id: [] for task_id in task_ids}
        for task in tasks:
            if task.parent_id in children_by_parent_id:
                children_by_parent_id[task.parent_id].append(task)

        for task in tasks:
            set_committed_value(task, "entries", entries_by_task_id.get(task.id, []))
            set_committed_value(task, "children", children_by_parent_id.get(task.id, []))
        return tasks

    async def _resolve_user_id(self, session: AsyncSession, username: str) -> int | None:
        normalized_username: str = username.strip()
        if (
            settings.admin_username is not None
            and settings.admin_password is not None
            and normalized_username == settings.admin_username
        ):
            return None

        user: User | None = await session.scalar(select(User).where(User.username == normalized_username))
        if user is None:
            raise TaskNotFoundError(f"User {normalized_username} was not found.")
        return user.id

    async def _get_required_model(self, session: AsyncSession, user_id: int | None, task_id: int) -> Task:
        task: Task | None = await session.scalar(
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

    async def _assert_parent_exists(
        self,
        session: AsyncSession,
        user_id: int | None,
        parent_id: int | None,
    ) -> None:
        if parent_id is None:
            return
        await self._assert_task_exists(session, user_id, parent_id)

    async def _assert_task_exists(self, session: AsyncSession, user_id: int | None, task_id: int) -> None:
        await self._get_required_model(session, user_id, task_id)

    async def _assert_not_descendant_parent(
        self,
        session: AsyncSession,
        user_id: int | None,
        task_id: int,
        parent_id: int | None,
    ) -> None:
        current_id: int | None = parent_id
        while current_id is not None:
            if current_id == task_id:
                raise InvalidTaskHierarchyError("A task cannot be moved under one of its descendants.")
            current: Task = await self._get_required_model(session, user_id, current_id)
            current_id = current.parent_id

    def _root_id_from_loaded_graph(self, tasks_by_id: dict[int, Task], task: Task) -> int:
        current: Task = task
        visited_ids: set[int] = set()
        while current.parent_id is not None and current.parent_id in tasks_by_id:
            if current.id in visited_ids:
                raise InvalidTaskHierarchyError("A task hierarchy cycle was detected.")
            visited_ids.add(current.id)
            current = tasks_by_id[current.parent_id]
        return current.id
