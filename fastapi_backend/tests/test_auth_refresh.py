from __future__ import annotations

from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import IsolatedAsyncioTestCase

from app.auth import AuthService
from app.database import Database
from app.security import AuthenticationError, decode_access_token


class RefreshTokenTests(IsolatedAsyncioTestCase):
    async def asyncSetUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        database_path = Path(self.temporary_directory.name) / "auth.sqlite3"
        self.database = Database(database_url=f"sqlite:///{database_path}")
        await self.database.initialize()
        self.service = AuthService(self.database)
        await self.service.register("refresh-user", "password123")

    async def asyncTearDown(self) -> None:
        await self.database.dispose()
        self.temporary_directory.cleanup()

    async def test_refresh_rotates_token_and_issues_new_access_token(self) -> None:
        initial = await self.service.login("refresh-user", "password123")

        refreshed = await self.service.refresh(initial.refresh_token)

        self.assertNotEqual(refreshed.refresh_token, initial.refresh_token)
        self.assertEqual(decode_access_token(refreshed.access_token)["sub"], "refresh-user")
        with self.assertRaises(AuthenticationError):
            await self.service.refresh(initial.refresh_token)
