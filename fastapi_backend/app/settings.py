from __future__ import annotations

import os
import secrets
from dataclasses import dataclass
from pathlib import Path

from dotenv import load_dotenv


_ENV_PATH = Path(__file__).resolve().parent.parent / ".env"
load_dotenv(_ENV_PATH)


@dataclass(frozen=True)
class AppSettings:
    app_title: str
    app_version: str
    cors_origins: list[str]
    cors_allow_credentials: bool
    database_url: str | None
    admin_username: str | None
    admin_password: str | None
    jwt_secret_key: str
    jwt_access_token_expire_minutes: int

    @classmethod
    def load(cls) -> "AppSettings":
        return cls(
            app_title=_string_env("WORKLOG_APP_TITLE", "Work Log Management System"),
            app_version=_string_env("WORKLOG_APP_VERSION", "0.1.0"),
            cors_origins=_list_env("WORKLOG_CORS_ORIGINS", ["*"]),
            cors_allow_credentials=_bool_env("WORKLOG_CORS_ALLOW_CREDENTIALS", True),
            database_url=_optional_string_env("WORKLOG_DATABASE_URL"),
            admin_username=_optional_string_env("WORKLOG_ADMIN_USERNAME"),
            admin_password=_optional_string_env("WORKLOG_ADMIN_PASSWORD"),
            jwt_secret_key=_string_env("WORKLOG_JWT_SECRET_KEY", secrets.token_urlsafe(32)),
            jwt_access_token_expire_minutes=_int_env("WORKLOG_JWT_ACCESS_TOKEN_EXPIRE_MINUTES", 1440),
        )


def _string_env(name: str, default: str) -> str:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return value.strip()


def _optional_string_env(name: str) -> str | None:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return None
    return value.strip()


def _list_env(name: str, default: list[str]) -> list[str]:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return [item.strip() for item in value.split(",") if item.strip()]


def _bool_env(name: str, default: bool) -> bool:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def _int_env(name: str, default: int) -> int:
    value = os.getenv(name)
    if value is None or value.strip() == "":
        return default
    return int(value.strip())


settings = AppSettings.load()
