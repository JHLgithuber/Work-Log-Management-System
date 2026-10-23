from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Final

from sqlalchemy import event, inspect, text
from sqlalchemy.engine import Connection, make_url
from sqlalchemy.ext.asyncio import (
    AsyncEngine,
    AsyncSession,
    async_sessionmaker,
    create_async_engine,
)

from app.domain import Base
from app.settings import settings


class Database:
    _DEFAULT_PATH: Final[Path] = Path(__file__).resolve().parent.parent / "worklog.sqlite3"
    _POOL_WAIT_TIMEOUT_SECONDS: Final[float] = 5.0
    _SQLITE_LOCK_TIMEOUT_SECONDS: Final[float] = 5.0

    def __init__(self, database_path: Path | None = None, database_url: str | None = None) -> None:
        self._database_path: Path = database_path or self._DEFAULT_PATH
        self._database_url: str = self._normalize_database_url(
            database_url or settings.database_url or f"sqlite:///{self._database_path}"
        )
        url = make_url(self._database_url)
        is_sqlite: bool = url.drivername.startswith("sqlite")
        connect_args: dict[str, object] = (
            {"timeout": self._SQLITE_LOCK_TIMEOUT_SECONDS}
            if is_sqlite
            else {"connect_timeout": int(self._POOL_WAIT_TIMEOUT_SECONDS)}
        )
        engine_options: dict[str, object] = {
            "connect_args": connect_args,
            "pool_pre_ping": True,
        }
        if not is_sqlite:
            engine_options["pool_timeout"] = self._POOL_WAIT_TIMEOUT_SECONDS
            engine_options["pool_recycle"] = 300

        self._engine: AsyncEngine = create_async_engine(self._database_url, **engine_options)
        self._session_factory: async_sessionmaker[AsyncSession] = async_sessionmaker(
            bind=self._engine,
            expire_on_commit=False,
        )
        if is_sqlite:
            event.listen(self._engine.sync_engine, "connect", self._enable_foreign_keys)

    @asynccontextmanager
    async def session(self) -> AsyncIterator[AsyncSession]:
        async with self._session_factory() as session:
            yield session

    async def initialize(self) -> None:
        async with self._engine.begin() as connection:
            await connection.run_sync(Base.metadata.create_all)
            await connection.run_sync(self._migrate_existing_schema)

    async def dispose(self) -> None:
        await self._engine.dispose()

    def _normalize_database_url(self, database_url: str) -> str:
        if database_url.startswith("sqlite+") or database_url.startswith("mysql+asyncmy://"):
            return database_url
        if database_url.startswith("sqlite://"):
            return database_url.replace("sqlite://", "sqlite+aiosqlite://", 1)
        if database_url.startswith("mariadb+mariadbconnector://"):
            return database_url.replace("mariadb+mariadbconnector://", "mysql+asyncmy://", 1)
        if database_url.startswith("mariadb://"):
            return database_url.replace("mariadb://", "mysql+asyncmy://", 1)
        if database_url.startswith("mysql://"):
            return database_url.replace("mysql://", "mysql+asyncmy://", 1)
        return database_url

    def _enable_foreign_keys(self, dbapi_connection: object, connection_record: object) -> None:
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA foreign_keys = ON")
        cursor.close()

    def _migrate_existing_schema(self, connection: Connection) -> None:
        table_names: set[str] = set(inspect(connection).get_table_names())
        if "tasks" not in table_names:
            return

        migrated_legacy_users = self._migrate_users_schema(connection)
        column_names: set[str] = self._task_column_names(connection)
        dialect_name: str = connection.dialect.name

        if "priority" not in column_names:
            priority_type = "TINYINT UNSIGNED" if dialect_name in {"mysql", "mariadb"} else "SMALLINT"
            connection.execute(text(f"ALTER TABLE tasks ADD COLUMN priority {priority_type} NOT NULL DEFAULT 0"))

        column_names = self._task_column_names(connection)
        if "user_id" not in column_names:
            connection.execute(text("ALTER TABLE tasks ADD COLUMN user_id INTEGER NULL"))

        if dialect_name in {"mysql", "mariadb"}:
            self._ensure_mysql_task_user_constraints(connection)

        # Only pre-authentication schemas contain a legacy placeholder user.
        # A current installation can also have exactly one real account;
        # running this conversion on every startup deletes that account.
        if migrated_legacy_users:
            self._move_single_legacy_user_tasks_to_admin(connection)

    def _task_column_names(self, connection: Connection) -> set[str]:
        return {column["name"] for column in inspect(connection).get_columns("tasks")}

    def _migrate_users_schema(self, connection: Connection) -> bool:
        column_names = {column["name"] for column in inspect(connection).get_columns("users")}
        dialect_name = connection.dialect.name

        if "password_hash" not in column_names:
            connection.execute(text("ALTER TABLE users ADD COLUMN password_hash VARCHAR(255) NULL"))
            connection.execute(
                text("UPDATE users SET password_hash = :password_hash WHERE password_hash IS NULL"),
                {"password_hash": "disabled"},
            )
            if dialect_name in {"mysql", "mariadb"}:
                connection.execute(text("ALTER TABLE users MODIFY COLUMN password_hash VARCHAR(255) NOT NULL"))
            return True

        connection.execute(
            text("UPDATE users SET password_hash = :password_hash WHERE password_hash IS NULL OR password_hash = ''"),
            {"password_hash": "disabled"},
        )
        return False

    def _move_single_legacy_user_tasks_to_admin(self, connection: Connection) -> None:
        admin_task_count = int(connection.scalar(text("SELECT COUNT(*) FROM tasks WHERE user_id IS NULL")) or 0)
        if admin_task_count > 0:
            return

        user_count = int(connection.scalar(text("SELECT COUNT(*) FROM users")) or 0)
        if user_count != 1:
            return

        legacy_user_id = connection.scalar(text("SELECT id FROM users LIMIT 1"))
        if legacy_user_id is None:
            return

        connection.execute(
            text("UPDATE tasks SET user_id = NULL WHERE user_id = :legacy_user_id"),
            {"legacy_user_id": int(legacy_user_id)},
        )
        connection.execute(
            text(
                "DELETE FROM users "
                "WHERE id = :legacy_user_id "
                "AND NOT EXISTS (SELECT 1 FROM tasks WHERE tasks.user_id = users.id)"
            ),
            {"legacy_user_id": int(legacy_user_id)},
        )

    def _ensure_mysql_task_user_constraints(self, connection: Connection) -> None:
        connection.execute(text("ALTER TABLE tasks MODIFY COLUMN user_id INTEGER NULL"))

        existing_indexes: set[str] = {
            row[2]
            for row in connection.execute(text("SHOW INDEX FROM tasks")).tuples()
            if row[2] is not None
        }
        if "idx_tasks_user_id" not in existing_indexes:
            connection.execute(text("CREATE INDEX idx_tasks_user_id ON tasks (user_id)"))

        existing_constraint = connection.scalar(
            text(
                "SELECT CONSTRAINT_NAME "
                "FROM information_schema.KEY_COLUMN_USAGE "
                "WHERE TABLE_SCHEMA = DATABASE() "
                "AND TABLE_NAME = 'tasks' "
                "AND COLUMN_NAME = 'user_id' "
                "AND REFERENCED_TABLE_NAME = 'users' "
                "LIMIT 1"
            )
        )
        if existing_constraint is None:
            connection.execute(
                text(
                    "ALTER TABLE tasks "
                    "ADD CONSTRAINT fk_tasks_user_id_users "
                    "FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE"
                )
            )
