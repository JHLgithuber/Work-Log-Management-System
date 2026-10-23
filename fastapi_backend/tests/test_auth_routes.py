from __future__ import annotations

from pathlib import Path
from tempfile import TemporaryDirectory
from unittest import IsolatedAsyncioTestCase
from unittest.mock import AsyncMock

from fastapi import HTTPException, status

from app.auth import AuthenticatedUser
from app.auth_routes import AuthRouter
from app.auth_schemas import RegisterRequest
from app.database import Database


class AuthRouterTests(IsolatedAsyncioTestCase):
    async def asyncSetUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        database_path = Path(self.temporary_directory.name) / "auth-routes.sqlite3"
        self.database = Database(database_url=f"sqlite:///{database_path}")
        self.router = AuthRouter(self.database)

    async def asyncTearDown(self) -> None:
        await self.database.dispose()
        self.temporary_directory.cleanup()

    async def test_regular_user_cannot_register_another_user(self) -> None:
        self.router.current_user = AsyncMock(
            return_value=AuthenticatedUser(
                id=1,
                username="regular-user",
                display_name="Regular User",
                is_admin=False,
            )
        )
        self.router._service.register = AsyncMock()

        with self.assertRaises(HTTPException) as caught:
            await self.router.register(
                RegisterRequest(username="new-user", password="password123"),
                authorization="Bearer access-token",
            )

        self.assertEqual(caught.exception.status_code, status.HTTP_403_FORBIDDEN)
        self.router._service.register.assert_not_awaited()

    async def test_admin_can_register_user_and_response_marks_role(self) -> None:
        self.router.current_user = AsyncMock(
            return_value=AuthenticatedUser(
                id=None,
                username="admin",
                display_name="Administrator",
                is_admin=True,
            )
        )
        self.router._service.register = AsyncMock(
            return_value=AuthenticatedUser(
                id=2,
                username="new-user",
                display_name="New User",
                is_admin=False,
            )
        )

        response = await self.router.register(
            RegisterRequest(username="new-user", password="password123"),
            authorization="Bearer access-token",
        )

        self.assertEqual(response.username, "new-user")
        self.assertFalse(response.is_admin)
