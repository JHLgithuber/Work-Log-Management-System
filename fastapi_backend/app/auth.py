from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

from sqlalchemy import select

from app.database import Database
from app.domain import DateBounds, User
from app.security import AuthenticationError, create_access_token, hash_password, verify_password
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
    token_type: str


class AuthService:
    def __init__(self, database: Database) -> None:
        self._database = database

    def register(self, username: str, password: str, display_name: str | None = None) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_username(normalized_username):
            raise UserAlreadyExistsError(f"User {normalized_username} already exists.")
        self._validate_password(password)
        now: datetime = DateBounds.now()
        with self._database.session() as session:
            existing_user = session.scalar(select(User).where(User.username == normalized_username))
            if existing_user is not None:
                raise UserAlreadyExistsError(f"User {normalized_username} already exists.")

            user = User(
                username=normalized_username,
                password_hash=hash_password(password),
                display_name=(display_name or normalized_username).strip() or normalized_username,
                created_at=now,
                updated_at=now,
            )
            session.add(user)
            session.commit()
            return AuthenticatedUser(id=user.id, username=user.username, display_name=user.display_name)

    def login(self, username: str, password: str) -> TokenPair:
        user = self.authenticate(username, password)
        return TokenPair(access_token=create_access_token(user.username, user.id), token_type="bearer")

    def authenticate(self, username: str, password: str) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_credentials(normalized_username, password):
            return self._admin_user()

        with self._database.session() as session:
            user = session.scalar(select(User).where(User.username == normalized_username))
            if user is None or not verify_password(password, user.password_hash):
                raise AuthenticationError("Invalid username or password.")
            return AuthenticatedUser(id=user.id, username=user.username, display_name=user.display_name)

    def get_user(self, username: str) -> AuthenticatedUser:
        normalized_username = self._normalize_username(username)
        if self._is_admin_username(normalized_username):
            return self._admin_user()

        with self._database.session() as session:
            user = session.scalar(select(User).where(User.username == normalized_username))
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
        return username == settings.admin_username

    def _admin_user(self) -> AuthenticatedUser:
        return AuthenticatedUser(
            id=None,
            username=settings.admin_username,
            display_name=settings.admin_username,
            is_admin=True,
        )
