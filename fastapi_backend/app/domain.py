from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime, timezone
from typing import Final


class DateBounds:
    MINIMUM: Final[datetime] = datetime.min.replace(tzinfo=timezone.utc)
    MAXIMUM: Final[datetime] = datetime.max.replace(tzinfo=timezone.utc)

    @classmethod
    def normalize(cls, value: datetime) -> datetime:
        if value.tzinfo is None:
            return value.replace(tzinfo=timezone.utc)
        return value.astimezone(timezone.utc)


@dataclass(frozen=True)
class WorkEntry:
    id: int
    task_id: int
    entry_date: date
    performed_content: str
    retrospective: str
    created_at: datetime
    updated_at: datetime


@dataclass(frozen=True)
class Task:
    id: int
    parent_id: int | None
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None
    created_at: datetime
    updated_at: datetime
    entries: list[WorkEntry] = field(default_factory=list)
    children: list[Task] = field(default_factory=list)

    @property
    def effective_end_at(self) -> datetime:
        return self.actual_end_at or self.due_at


@dataclass(frozen=True)
class TaskCreate:
    parent_id: int | None
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None


@dataclass(frozen=True)
class TaskUpdate:
    parent_id: int | None
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None


@dataclass(frozen=True)
class WorkEntryUpsert:
    task_id: int
    entry_date: date
    performed_content: str
    retrospective: str
