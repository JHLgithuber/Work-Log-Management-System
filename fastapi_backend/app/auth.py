from __future__ import annotations

import asyncio
import hashlib
import secrets
from dataclasses import dataclass
from datetime import datetime, timedelta

from sqlalchemy import select

from app.database import Database
from app.domain import DateBounds, RefreshSession, User
from app.security import (
    AuthenticationError,
    create_access_token,
    hash_password,
    verify_password,
)
from app.settings import settings


class UserAlreadyExistsError(Exception):
    pass


@dataclass(frozen=True)
class AuthenticatedUser:
    id: int | None
    username: str
    display_name: str
    is_admin: bool = False


@dataclass(frozen=True)
class TokenPair:
    access_token: str
    refresh_token: str
    token_type: str


class AuthService:
    def __init__(self, database: Database) -> None:
        self._database = database

    async def register(self, username: str, password: str, display_name: str | None = None) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_username(normalized_username):
            raise UserAlreadyExistsError(f"User {normalized_username} already exists.")
        self._validate_password(password)
        now: datetime = DateBounds.now()
        password_hash = await asyncio.to_thread(hash_password, password)
        async with self._database.session() as session:
            existing_user = await session.scalar(select(User).where(User.username == normalized_username))
            if existing_user is not None:
                raise UserAlreadyExistsError(f"User {normalized_username} already exists.")

            user = User(
                username=normalized_username,
                password_hash=password_hash,
                display_name=(display_name or normalized_username).strip() or normalized_username,
                created_at=now,
                updated_at=now,
            )
            session.add(user)
            await session.commit()
            return AuthenticatedUser(id=user.id, username=user.username, display_name=user.display_name)

    async def login(self, username: str, password: str) -> TokenPair:
        user = await self.authenticate(username, password)
        return await self._create_token_pair(user)

    async def refresh(self, refresh_token: str) -> TokenPair:
        token_hash = self._refresh_token_hash(refresh_token)
        now = DateBounds.now()
        async with self._database.session() as session:
            refresh_session = await session.scalar(
                select(RefreshSession).where(RefreshSession.token_hash == token_hash)
            )
            if refresh_session is None or refresh_session.expires_at < now:
                if refresh_session is not None:
                    await session.delete(refresh_session)
                    await session.commit()
                raise AuthenticationError("Refresh token is invalid or has expired.")

            username = refresh_session.username
            await session.delete(refresh_session)
            await session.commit()

        user = await self.get_user(username)
        return await self._create_token_pair(user)

    async def change_password(self, username: str, current_password: str, new_password: str) -> None:
        normalized_username = self._normalize_username(username)
        self._validate_password(new_password)
        if self._is_admin_username(normalized_username):
            if settings.admin_password is None or current_password != settings.admin_password:
                raise AuthenticationError("Invalid current password.")
            raise AuthenticationError("The built-in admin password is configured by environment variables.")

        now: datetime = DateBounds.now()
        async with self._database.session() as session:
            user = await session.scalar(select(User).where(User.username == normalized_username))
            if user is None or not await asyncio.to_thread(verify_password, current_password, user.password_hash):
                raise AuthenticationError("Invalid current password.")

            user.password_hash = await asyncio.to_thread(hash_password, new_password)
            user.updated_at = now
            refresh_sessions = await session.scalars(
                select(RefreshSession).where(RefreshSession.username == normalized_username)
            )
            for refresh_session in refresh_sessions:
                await session.delete(refresh_session)
            await session.commit()

    async def _create_token_pair(self, user: AuthenticatedUser) -> TokenPair:
        refresh_token = secrets.token_urlsafe(48)
        now = DateBounds.now()
        refresh_session = RefreshSession(
            username=user.username,
            token_hash=self._refresh_token_hash(refresh_token),
            expires_at=now + timedelta(days=settings.jwt_refresh_token_expire_days),
            created_at=now,
        )
        async with self._database.session() as session:
            session.add(refresh_session)
            await session.commit()
        return TokenPair(
            access_token=create_access_token(user.username, user.id),
            refresh_token=refresh_token,
            token_type="bearer",
        )

    def _refresh_token_hash(self, refresh_token: str) -> str:
        return hashlib.sha256(refresh_token.encode()).hexdigest()

    async def authenticate(self, username: str, password: str) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_credentials(normalized_username, password):
            return self._admin_user()

        async with self._database.session() as session:
            user = await session.scalar(select(User).where(User.username == normalized_username))
            if user is None or not await asyncio.to_thread(verify_password, password, user.password_hash):
                raise AuthenticationError("Invalid username or password.")
            return AuthenticatedUser(id=user.id, username=user.username, display_name=user.display_name)

    async def get_user(self, username: str) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_username(normalized_username):
            return self._admin_user()

        async with self._database.session() as session:
            user = await session.scalar(select(User).where(User.username == normalized_username))
            if user is None:
                raise AuthenticationError("User was not found.")
            return AuthenticatedUser(id=user.id, username=user.username, display_name=user.display_name)

    def _normalize_username(self, username: str) -> str:
        normalized_username = username.strip()
        if normalized_username == "":
            raise AuthenticationError("Username is required.")
        return normalized_username

    def _validate_password(self, password: str) -> None:
        if len(password) < 8:
            raise AuthenticationError("Password must be at least 8 characters.")

    def _is_admin_credentials(self, username: str, password: str) -> bool:
        return self._is_admin_username(username) and password == settings.admin_password

    def _is_admin_username(self, username: str) -> bool:
        return self._is_admin_enabled() and username == settings.admin_username

    def _is_admin_enabled(self) -> bool:
        return settings.admin_username is not None and settings.admin_password is not None

    def _admin_user(self) -> AuthenticatedUser:
        if not self._is_admin_enabled():
            raise AuthenticationError("The built-in admin user is disabled.")
        admin_username = settings.admin_username
        if admin_username is None:
            raise AuthenticationError("The built-in admin user is disabled.")
        return AuthenticatedUser(
            id=None,
            username=admin_username,
            display_name=admin_username,
            is_admin=True,
        )
