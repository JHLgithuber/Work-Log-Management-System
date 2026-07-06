from __future__ import annotations

from datetime import date, datetime

from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, WorkEntry, WorkEntryUpsert
from app.repository import TaskRepository


class TaskValidationError(Exception):
    pass


class TaskService:
    def __init__(self, repository: TaskRepository) -> None:
        self._repository: TaskRepository = repository

    def create_task(self, username: str, command: TaskCreate) -> Task:
        self._validate_dates(command.start_at, command.due_at, command.actual_end_at)
        self._validate_priority(command.priority)
        return self._repository.create(username, self._normalize_create(command))

    def update_task(self, username: str, task_id: int, command: TaskUpdate) -> Task:
        self._validate_dates(command.start_at, command.due_at, command.actual_end_at)
        self._validate_priority(command.priority)
        return self._repository.update(username, task_id, self._normalize_update(command))

    def delete_task(self, username: str, task_id: int) -> None:
        self._repository.delete(username, task_id)

    def get_task(self, username: str, task_id: int) -> Task:
        return self._repository.get_required(username, task_id)

    def get_work_entry(self, username: str, task_id: int, entry_date: date) -> WorkEntry:
        return self._repository.get_work_entry(username, task_id, entry_date)

    def upsert_work_entry(self, username: str, command: WorkEntryUpsert) -> WorkEntry:
        return self._repository.upsert_work_entry(username, command)

    def delete_work_entry(self, username: str, task_id: int, entry_date: date) -> None:
        self._repository.delete_work_entry(username, task_id, entry_date)

    def list_tasks_for_date(self, username: str, target_date: date) -> list[Task]:
        day_start: datetime = DateBounds.start_of_day(target_date)
        day_end: datetime = DateBounds.end_of_day(target_date)
        return self._repository.list_for_date(username, day_start, day_end)

    def list_all_tasks(self, username: str) -> list[Task]:
        return self._repository.list_all(username)

    def _validate_dates(self, start_at: datetime, due_at: datetime, actual_end_at: datetime | None) -> None:
        normalized_start: datetime = DateBounds.start_of_day(start_at)
        normalized_due: datetime = DateBounds.end_of_day(due_at)
        if normalized_start > normalized_due:
            raise TaskValidationError("The start date must be earlier than or equal to the due date.")
        if actual_end_at is not None and DateBounds.end_of_day(actual_end_at) < normalized_start:
            raise TaskValidationError("The actual end date must be later than or equal to the start date.")

    def _validate_priority(self, priority: int) -> None:
        if priority < 0 or priority > 255:
            raise TaskValidationError("The task priority must be between 0 and 255.")

    def _normalize_create(self, command: TaskCreate) -> TaskCreate:
        return TaskCreate(
            parent_id=command.parent_id,
            title=command.title,
            content=command.content,
            priority=command.priority,
            start_at=DateBounds.start_of_day(command.start_at),
            due_at=DateBounds.end_of_day(command.due_at),
            actual_end_at=None if command.actual_end_at is None else DateBounds.end_of_day(command.actual_end_at),
        )

    def _normalize_update(self, command: TaskUpdate) -> TaskUpdate:
        return TaskUpdate(
            parent_id=command.parent_id,
            title=command.title,
            content=command.content,
            priority=command.priority,
            start_at=DateBounds.start_of_day(command.start_at),
            due_at=DateBounds.end_of_day(command.due_at),
            actual_end_at=None if command.actual_end_at is None else DateBounds.end_of_day(command.actual_end_at),
        )
