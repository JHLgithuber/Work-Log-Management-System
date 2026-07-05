from __future__ import annotations

from datetime import date, datetime

from pydantic import BaseModel, Field

from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, WorkEntry, WorkEntryUpsert


class TaskWriteRequest(BaseModel):
    parent_id: int | None = None
    title: str = Field(min_length=1, max_length=200)
    content: str = ""
    start_at: datetime = DateBounds.MINIMUM
    due_at: datetime = DateBounds.MAXIMUM
    actual_end_at: datetime | None = None

    def to_create_command(self) -> TaskCreate:
        return TaskCreate(
            parent_id=self.parent_id,
            title=self.title.strip(),
            content=self.content,
            start_at=self.start_at,
            due_at=self.due_at,
            actual_end_at=self.actual_end_at,
        )

    def to_update_command(self) -> TaskUpdate:
        return TaskUpdate(
            parent_id=self.parent_id,
            title=self.title.strip(),
            content=self.content,
            start_at=self.start_at,
            due_at=self.due_at,
            actual_end_at=self.actual_end_at,
        )


class WorkEntryWriteRequest(BaseModel):
    performed_content: str = ""
    retrospective: str = ""

    def to_upsert_command(self, task_id: int, entry_date: date) -> WorkEntryUpsert:
        return WorkEntryUpsert(
            task_id=task_id,
            entry_date=entry_date,
            performed_content=self.performed_content,
            retrospective=self.retrospective,
        )


class WorkEntryResponse(BaseModel):
    id: int
    task_id: int
    entry_date: date
    performed_content: str
    retrospective: str
    created_at: datetime
    updated_at: datetime

    @classmethod
    def from_domain(cls, entry: WorkEntry) -> "WorkEntryResponse":
        return cls(
            id=entry.id,
            task_id=entry.task_id,
            entry_date=entry.entry_date,
            performed_content=entry.performed_content,
            retrospective=entry.retrospective,
            created_at=entry.created_at,
            updated_at=entry.updated_at,
        )


class TaskResponse(BaseModel):
    id: int
    parent_id: int | None
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None
    created_at: datetime
    updated_at: datetime
    entries: list[WorkEntryResponse] = Field(default_factory=list)
    children: list["TaskResponse"] = Field(default_factory=list)

    @classmethod
    def from_domain(cls, task: Task) -> "TaskResponse":
        return cls(
            id=task.id,
            parent_id=task.parent_id,
            title=task.title,
            content=task.content,
            start_at=task.start_at,
            due_at=task.due_at,
            actual_end_at=task.actual_end_at,
            created_at=task.created_at,
            updated_at=task.updated_at,
            entries=[WorkEntryResponse.from_domain(entry) for entry in task.entries],
            children=[cls.from_domain(child) for child in task.children],
        )
