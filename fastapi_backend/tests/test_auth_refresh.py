from __future__ import annotations

from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import TestCase

from app.auth import AuthService
from app.database import Database
from app.security import AuthenticationError, decode_access_token


class RefreshTokenTests(TestCase):
    def setUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        database_path = Path(self.temporary_directory.name) / "auth.sqlite3"
        database = Database(database_url=f"sqlite:///{database_path}")
        database.initialize()
        self.service = AuthService(database)
        self.service.register("refresh-user", "password123")

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_refresh_rotates_token_and_issues_new_access_token(self) -> None:
        initial = self.service.login("refresh-user", "password123")

        refreshed = self.service.refresh(initial.refresh_token)

        self.assertNotEqual(refreshed.refresh_token, initial.refresh_token)
        self.assertEqual(decode_access_token(refreshed.access_token)["sub"], "refresh-user")
        with self.assertRaises(AuthenticationError):
            self.service.refresh(initial.refresh_token)
