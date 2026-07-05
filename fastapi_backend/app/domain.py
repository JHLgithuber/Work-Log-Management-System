from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime, timezone
from typing import Any, Final

from sqlalchemy import ForeignKey, Index, String, UniqueConstraint
from sqlalchemy.engine import Dialect
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column, relationship
from sqlalchemy.types import TypeDecorator


class UTCDateTime(TypeDecorator[datetime]):
    impl = String
    cache_ok = True

    def process_bind_param(self, value: datetime | None, dialect: Dialect) -> str | None:
        if value is None:
            return None
        return DateBounds.normalize(value).isoformat(timespec="microseconds")

    def process_result_value(self, value: Any | None, dialect: Dialect) -> datetime | None:
        if value is None:
            return None
        if isinstance(value, datetime):
            return DateBounds.normalize(value)
        return DateBounds.normalize(datetime.fromisoformat(str(value)))


class ISODate(TypeDecorator[date]):
    impl = String
    cache_ok = True

    def process_bind_param(self, value: date | None, dialect: Dialect) -> str | None:
        if value is None:
            return None
        return value.isoformat()

    def process_result_value(self, value: Any | None, dialect: Dialect) -> date | None:
        if value is None:
            return None
        if isinstance(value, date) and not isinstance(value, datetime):
            return value
        return date.fromisoformat(str(value))


class Base(DeclarativeBase):
    pass


class DateBounds:
    MINIMUM: Final[datetime] = datetime.min.replace(tzinfo=timezone.utc)
    MAXIMUM: Final[datetime] = datetime.max.replace(tzinfo=timezone.utc)

    @classmethod
    def normalize(cls, value: datetime) -> datetime:
        if value.tzinfo is None:
            return value.replace(tzinfo=timezone.utc)
        return value.astimezone(timezone.utc)


class Task(Base):
    __tablename__ = "tasks"
    __table_args__ = (
        Index("idx_tasks_parent_id", "parent_id"),
        Index("idx_tasks_date_range", "start_at", "due_at", "actual_end_at"),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    parent_id: Mapped[int | None] = mapped_column(
        ForeignKey("tasks.id", ondelete="CASCADE"),
        nullable=True,
    )
    title: Mapped[str] = mapped_column(String, nullable=False)
    content: Mapped[str] = mapped_column(String, nullable=False, default="")
    start_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)
    due_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)
    actual_end_at: Mapped[datetime | None] = mapped_column(UTCDateTime(), nullable=True)
    created_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)

    parent: Mapped[Task | None] = relationship(
        back_populates="children",
        remote_side=lambda: [Task.id],
    )
    entries: Mapped[list[WorkEntry]] = relationship(
        back_populates="task",
        cascade="all, delete-orphan",
        order_by=lambda: WorkEntry.entry_date,
    )
    children: Mapped[list[Task]] = relationship(
        back_populates="parent",
        cascade="all, delete-orphan",
        order_by=lambda: (Task.start_at, Task.id),
        single_parent=True,
    )

    @property
    def effective_end_at(self) -> datetime:
        return self.actual_end_at or self.due_at


class WorkEntry(Base):
    __tablename__ = "work_entries"
    __table_args__ = (
        UniqueConstraint("task_id", "entry_date", name="uq_work_entries_task_entry_date"),
        Index("idx_work_entries_task_id", "task_id"),
        Index("idx_work_entries_entry_date", "entry_date"),
    )

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    task_id: Mapped[int] = mapped_column(
        ForeignKey("tasks.id", ondelete="CASCADE"),
        nullable=False,
    )
    entry_date: Mapped[date] = mapped_column(ISODate(), nullable=False)
    performed_content: Mapped[str] = mapped_column(String, nullable=False, default="")
    retrospective: Mapped[str] = mapped_column(String, nullable=False, default="")
    created_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)
    updated_at: Mapped[datetime] = mapped_column(UTCDateTime(), nullable=False)

    task: Mapped[Task] = relationship(back_populates="entries")


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
