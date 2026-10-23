from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import IsolatedAsyncioTestCase

from sqlalchemy import select, text

from app.auth import AuthService
from app.database import Database
from app.domain import DateBounds, Task, TaskCreate, User
from app.repository import TaskRepository
from app.service import TaskService


class RestartTests(IsolatedAsyncioTestCase):
    async def test_account_and_task_survive_repeated_startups(self):
        with TemporaryDirectory() as directory:
            database = Database(database_url=f"sqlite:///{Path(directory) / 'synthetic.sqlite3'}")
            try:
                await database.initialize()
                auth = AuthService(database)
                user = await auth.register('synthetic-user', 'synthetic-password')
                await database.initialize()
                self.assertEqual((await auth.authenticate(user.username, 'synthetic-password')).id, user.id)
                service = TaskService(TaskRepository(database))
                task = await service.create_task(user.username, TaskCreate(
                    parent_id=None, title='Synthetic task', content='', priority=0,
                    start_at=DateBounds.MINIMUM, due_at=DateBounds.MAXIMUM, actual_end_at=None))
                await database.initialize()
                async with database.session() as session:
                    self.assertEqual((await session.get(Task, task.id)).user_id, user.id)
                    self.assertIsNotNone(await session.get(User, user.id))
            finally:
                await database.dispose()

    async def test_legacy_conversion_is_one_time(self):
        with TemporaryDirectory() as directory:
            database = Database(database_url=f"sqlite:///{Path(directory) / 'legacy.sqlite3'}")
            try:
                await database.initialize()
                auth = AuthService(database)
                await auth.register('legacy-placeholder', 'synthetic-password')
                async with database._engine.begin() as connection:
                    await connection.execute(text('ALTER TABLE users DROP COLUMN password_hash'))
                await database.initialize()
                async with database.session() as session:
                    self.assertIsNone(await session.scalar(select(User)))
                user = await auth.register('real-after-migration', 'synthetic-password')
                await database.initialize()
                self.assertEqual((await auth.authenticate(user.username, 'synthetic-password')).id, user.id)
            finally:
                await database.dispose()
