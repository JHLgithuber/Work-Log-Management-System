from __future__ import annotations

import asyncio
from datetime import date, timedelta
from typing import Annotated

from fastapi import APIRouter, Header, HTTPException, Response, status

from app.auth import AuthService
from app.database import Database
from app.domain import DateBounds
from app.excel_exporter import NoExportableSheetsError, TaskExcelExporter
from app.repository import InvalidTaskHierarchyError, TaskNotFoundError, TaskRepository
from app.schemas import (
    DailyNoteResponse,
    DailyNoteWriteRequest,
    TaskResponse,
    TaskWriteRequest,
    WorkEntryResponse,
    WorkEntryWriteRequest,
)
from app.security import AuthenticationError, decode_access_token
from app.service import TaskService, TaskValidationError

AuthorizationHeader = Annotated[str | None, Header(alias="Authorization")]


class TaskRouter:
    def __init__(self, database: Database) -> None:
        repository: TaskRepository = TaskRepository(database)
        self._service: TaskService = TaskService(repository)
        self._auth_service: AuthService = AuthService(database)
        self._exporter: TaskExcelExporter = TaskExcelExporter()
        self.router: APIRouter = APIRouter(prefix="/tasks", tags=["tasks"])
        self._register_routes()

    def _register_routes(self) -> None:
        self.router.add_api_route("", self.list_tasks, methods=["GET"], response_model=list[TaskResponse])
        self.router.add_api_route("", self.create_task, methods=["POST"], response_model=TaskResponse, status_code=201)
        self.router.add_api_route("/export.xlsx", self.export_tasks, methods=["GET"])
        self.router.add_api_route(
            "/daily-notes/{note_date}",
            self.get_daily_note,
            methods=["GET"],
            response_model=DailyNoteResponse,
        )
        self.router.add_api_route(
            "/daily-notes/{note_date}",
            self.upsert_daily_note,
            methods=["PUT"],
            response_model=DailyNoteResponse,
        )
        self.router.add_api_route(
            "/{task_id}/entries/{entry_date}",
            self.get_work_entry,
            methods=["GET"],
            response_model=WorkEntryResponse,
        )
        self.router.add_api_route(
            "/{task_id}/entries/{entry_date}",
            self.upsert_work_entry,
            methods=["PUT"],
            response_model=WorkEntryResponse,
        )
        self.router.add_api_route(
            "/{task_id}/entries/{entry_date}",
            self.delete_work_entry,
            methods=["DELETE"],
            status_code=204,
        )
        self.router.add_api_route("/{task_id}", self.get_task, methods=["GET"], response_model=TaskResponse)
        self.router.add_api_route("/{task_id}", self.update_task, methods=["PUT"], response_model=TaskResponse)
        self.router.add_api_route("/{task_id}", self.delete_task, methods=["DELETE"], status_code=204)

    async def list_tasks(
        self,
        authorization: AuthorizationHeader = None,
        target_date: date | None = None,
    ) -> list[TaskResponse]:
        username: str = await self._username(authorization)
        if target_date is None:
            tasks = await self._service.list_all_tasks(username)
        else:
            tasks = await self._service.list_tasks_for_date(username, target_date)
        return [TaskResponse.from_domain(task) for task in tasks]

    async def get_task(self, task_id: int, authorization: AuthorizationHeader = None) -> TaskResponse:
        try:
            username = await self._username(authorization)
            return TaskResponse.from_domain(await self._service.get_task(username, task_id))
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    async def create_task(self, request: TaskWriteRequest, authorization: AuthorizationHeader = None) -> TaskResponse:
        try:
            username = await self._username(authorization)
            task = await self._service.create_task(username, request.to_create_command())
            return TaskResponse.from_domain(task)
        except (TaskValidationError, TaskNotFoundError, InvalidTaskHierarchyError) as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

    async def update_task(
        self,
        task_id: int,
        request: TaskWriteRequest,
        authorization: AuthorizationHeader = None,
    ) -> TaskResponse:
        try:
            username = await self._username(authorization)
            task = await self._service.update_task(username, task_id, request.to_update_command())
            return TaskResponse.from_domain(task)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error
        except (TaskValidationError, InvalidTaskHierarchyError) as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

    async def delete_task(self, task_id: int, authorization: AuthorizationHeader = None) -> Response:
        try:
            username = await self._username(authorization)
            await self._service.delete_task(username, task_id)
            return Response(status_code=status.HTTP_204_NO_CONTENT)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    async def get_work_entry(
        self,
        task_id: int,
        entry_date: date,
        authorization: AuthorizationHeader = None,
    ) -> WorkEntryResponse:
        try:
            username = await self._username(authorization)
            entry = await self._service.get_work_entry(username, task_id, entry_date)
            return WorkEntryResponse.from_domain(entry)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    async def upsert_work_entry(
        self,
        task_id: int,
        entry_date: date,
        request: WorkEntryWriteRequest,
        authorization: AuthorizationHeader = None,
    ) -> WorkEntryResponse:
        try:
            username = await self._username(authorization)
            entry = await self._service.upsert_work_entry(
                username,
                request.to_upsert_command(task_id, entry_date),
            )
            return WorkEntryResponse.from_domain(entry)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    async def delete_work_entry(
        self,
        task_id: int,
        entry_date: date,
        authorization: AuthorizationHeader = None,
    ) -> Response:
        try:
            username = await self._username(authorization)
            await self._service.delete_work_entry(username, task_id, entry_date)
            return Response(status_code=status.HTTP_204_NO_CONTENT)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    async def get_daily_note(
        self,
        note_date: date,
        authorization: AuthorizationHeader = None,
    ) -> DailyNoteResponse:
        username = await self._username(authorization)
        note = await self._service.get_daily_note(username, note_date)
        return DailyNoteResponse.from_domain(note)

    async def upsert_daily_note(
        self,
        note_date: date,
        request: DailyNoteWriteRequest,
        authorization: AuthorizationHeader = None,
    ) -> DailyNoteResponse:
        username = await self._username(authorization)
        note = await self._service.upsert_daily_note(
            username,
            request.to_upsert_command(note_date),
        )
        return DailyNoteResponse.from_domain(note)

    async def export_tasks(
        self,
        authorization: AuthorizationHeader = None,
        start_date: date | None = None,
        end_date: date | None = None,
    ) -> Response:
        if start_date is None and end_date is None:
            start_date, end_date = self._current_month_range()

        if start_date is not None and end_date is not None and start_date > end_date:
            raise HTTPException(
                status_code=status.HTTP_400_BAD_REQUEST,
                detail="The export start date must be earlier than or equal to the end date.",
            )

        generated_at: str = DateBounds.now().strftime("%Y%m%d_%H%M%S")
        username: str = await self._username(authorization)
        try:
            tasks = await self._service.list_all_tasks(username)
            daily_notes = await self._service.list_daily_notes(
                username,
                start_date or date.min,
                end_date or date.max,
            )
            content: bytes = await asyncio.to_thread(
                self._exporter.export,
                tasks,
                daily_notes=daily_notes,
                start_date=start_date,
                end_date=end_date,
            )
        except NoExportableSheetsError as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

        headers: dict[str, str] = {"Content-Disposition": f'attachment; filename="worklog_{generated_at}.xlsx"'}
        return Response(
            content=content,
            media_type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            headers=headers,
        )

    def _current_month_range(self) -> tuple[date, date]:
        today: date = DateBounds.now().date()
        first_day: date = today.replace(day=1)
        if today.month == 12:
            next_month_first_day: date = date(today.year + 1, 1, 1)
        else:
            next_month_first_day = date(today.year, today.month + 1, 1)
        return first_day, next_month_first_day - timedelta(days=1)

    async def _username(self, authorization: str | None) -> str:
        if authorization is None or not authorization.startswith("Bearer "):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Bearer access token is required.",
                headers={"WWW-Authenticate": "Bearer"},
            )

        try:
            payload = decode_access_token(authorization.removeprefix("Bearer ").strip())
            user = await self._auth_service.get_user(str(payload["sub"]))
            return user.username
        except AuthenticationError as error:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=str(error),
                headers={"WWW-Authenticate": "Bearer"},
            ) from error
