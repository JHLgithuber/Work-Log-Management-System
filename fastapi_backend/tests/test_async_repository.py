from __future__ import annotations

import asyncio
import time
from datetime import date
from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import IsolatedAsyncioTestCase
from unittest.mock import patch

from app.auth import AuthService
from app.database import Database
from app.domain import DateBounds, TaskCreate, WorkEntryUpsert
from app.repository import TaskRepository
from app.schemas import TaskResponse
from app.service import TaskService


class AsyncRepositoryTests(IsolatedAsyncioTestCase):
    async def asyncSetUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        database_path = Path(self.temporary_directory.name) / "repository.sqlite3"
        self.database = Database(database_url=f"sqlite:///{database_path}")
        await self.database.initialize()
        self.auth_service = AuthService(self.database)
        await self.auth_service.register("repository-user", "password123")
        self.task_service = TaskService(TaskRepository(self.database))

    async def asyncTearDown(self) -> None:
        await self.database.dispose()
        self.temporary_directory.cleanup()

    async def test_task_graph_is_bulk_loaded_and_can_be_serialized_after_session_closes(self) -> None:
        target_date = date(2026, 8, 12)
        root = await self.task_service.create_task(
            "repository-user",
            TaskCreate(
                parent_id=None,
                title="root",
                content="root content",
                priority=1,
                start_at=DateBounds.start_of_day(target_date),
                due_at=DateBounds.end_of_day(target_date),
                actual_end_at=None,
            ),
        )
        child = await self.task_service.create_task(
            "repository-user",
            TaskCreate(
                parent_id=root.id,
                title="child",
                content="child content",
                priority=2,
                start_at=DateBounds.start_of_day(target_date),
                due_at=DateBounds.end_of_day(target_date),
                actual_end_at=None,
            ),
        )
        await self.task_service.upsert_work_entry(
            "repository-user",
            WorkEntryUpsert(
                task_id=child.id,
                entry_date=target_date,
                performed_content="done",
                retrospective="review",
            ),
        )

        roots = await self.task_service.list_tasks_for_date("repository-user", target_date)
        response = TaskResponse.from_domain(roots[0])

        self.assertEqual(len(roots), 1)
        self.assertEqual(response.title, "root")
        self.assertEqual([item.title for item in response.children], ["child"])
        self.assertEqual(response.children[0].entries[0].performed_content, "done")

        await self.task_service.delete_task("repository-user", root.id)
        self.assertEqual(await self.task_service.list_all_tasks("repository-user"), [])

    async def test_password_hashing_does_not_block_the_event_loop(self) -> None:
        def slow_hash(_: str) -> str:
            time.sleep(0.2)
            return "test-password-hash"

        with patch("app.auth.hash_password", side_effect=slow_hash):
            registration = asyncio.create_task(
                self.auth_service.register("another-user", "password123")
            )
            await asyncio.sleep(0.02)
            started = asyncio.get_running_loop().time()
            await asyncio.sleep(0.01)
            event_loop_delay = asyncio.get_running_loop().time() - started
            await registration

        self.assertLess(event_loop_delay, 0.1)
