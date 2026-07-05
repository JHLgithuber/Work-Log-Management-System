from __future__ import annotations

from datetime import date, datetime, time, timezone

from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, WorkEntry, WorkEntryUpsert
from app.repository import TaskRepository


class TaskValidationError(Exception):
    pass


class TaskService:
    def __init__(self, repository: TaskRepository) -> None:
        self._repository: TaskRepository = repository

    def create_task(self, command: TaskCreate) -> Task:
        self._validate_dates(command.start_at, command.due_at, command.actual_end_at)
        return self._repository.create(self._normalize_create(command))

    def update_task(self, task_id: int, command: TaskUpdate) -> Task:
        self._validate_dates(command.start_at, command.due_at, command.actual_end_at)
        return self._repository.update(task_id, self._normalize_update(command))

    def delete_task(self, task_id: int) -> None:
        self._repository.delete(task_id)

    def get_task(self, task_id: int) -> Task:
        return self._repository.get_required(task_id)

    def get_work_entry(self, task_id: int, entry_date: date) -> WorkEntry:
        return self._repository.get_work_entry(task_id, entry_date)

    def upsert_work_entry(self, command: WorkEntryUpsert) -> WorkEntry:
        return self._repository.upsert_work_entry(command)

    def delete_work_entry(self, task_id: int, entry_date: date) -> None:
        self._repository.delete_work_entry(task_id, entry_date)

    def list_tasks_for_date(self, target_date: date) -> list[Task]:
        day_start: datetime = datetime.combine(target_date, time.min, tzinfo=timezone.utc)
        day_end: datetime = datetime.combine(target_date, time.max, tzinfo=timezone.utc)
        return self._repository.list_for_date(day_start, day_end)

    def list_all_tasks(self) -> list[Task]:
        return self._repository.list_all()

    def _validate_dates(self, start_at: datetime, due_at: datetime, actual_end_at: datetime | None) -> None:
        normalized_start: datetime = DateBounds.normalize(start_at)
        normalized_due: datetime = DateBounds.normalize(due_at)
        if normalized_start > normalized_due:
            raise TaskValidationError("The start date must be earlier than or equal to the due date.")
        if actual_end_at is not None and DateBounds.normalize(actual_end_at) < normalized_start:
            raise TaskValidationError("The actual end date must be later than or equal to the start date.")

    def _normalize_create(self, command: TaskCreate) -> TaskCreate:
        return TaskCreate(
            parent_id=command.parent_id,
            title=command.title,
            content=command.content,
            start_at=DateBounds.normalize(command.start_at),
            due_at=DateBounds.normalize(command.due_at),
            actual_end_at=None if command.actual_end_at is None else DateBounds.normalize(command.actual_end_at),
        )

    def _normalize_update(self, command: TaskUpdate) -> TaskUpdate:
        return TaskUpdate(
            parent_id=command.parent_id,
            title=command.title,
            content=command.content,
            start_at=DateBounds.normalize(command.start_at),
            due_at=DateBounds.normalize(command.due_at),
            actual_end_at=None if command.actual_end_at is None else DateBounds.normalize(command.actual_end_at),
        )
