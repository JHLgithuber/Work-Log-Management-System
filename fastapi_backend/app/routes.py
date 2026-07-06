from __future__ import annotations

from datetime import date, timedelta
from typing import Annotated

from fastapi import APIRouter, Header, HTTPException, Response, status

from app.auth import AuthService
from app.database import Database
from app.domain import DateBounds
from app.excel_exporter import NoExportableSheetsError, TaskExcelExporter
from app.repository import InvalidTaskHierarchyError, TaskNotFoundError, TaskRepository
from app.schemas import TaskResponse, TaskWriteRequest, WorkEntryResponse, WorkEntryWriteRequest
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

    def list_tasks(self, authorization: AuthorizationHeader = None, target_date: date | None = None) -> list[TaskResponse]:
        username: str = self._username(authorization)
        tasks = (
            self._service.list_all_tasks(username)
            if target_date is None
            else self._service.list_tasks_for_date(username, target_date)
        )
        return [TaskResponse.from_domain(task) for task in tasks]

    def get_task(self, task_id: int, authorization: AuthorizationHeader = None) -> TaskResponse:
        try:
            return TaskResponse.from_domain(self._service.get_task(self._username(authorization), task_id))
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    def create_task(self, request: TaskWriteRequest, authorization: AuthorizationHeader = None) -> TaskResponse:
        try:
            task = self._service.create_task(self._username(authorization), request.to_create_command())
            return TaskResponse.from_domain(task)
        except (TaskValidationError, TaskNotFoundError, InvalidTaskHierarchyError) as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

    def update_task(self, task_id: int, request: TaskWriteRequest, authorization: AuthorizationHeader = None) -> TaskResponse:
        try:
            task = self._service.update_task(self._username(authorization), task_id, request.to_update_command())
            return TaskResponse.from_domain(task)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error
        except (TaskValidationError, InvalidTaskHierarchyError) as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

    def delete_task(self, task_id: int, authorization: AuthorizationHeader = None) -> Response:
        try:
            self._service.delete_task(self._username(authorization), task_id)
            return Response(status_code=status.HTTP_204_NO_CONTENT)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    def get_work_entry(self, task_id: int, entry_date: date, authorization: AuthorizationHeader = None) -> WorkEntryResponse:
        try:
            entry = self._service.get_work_entry(self._username(authorization), task_id, entry_date)
            return WorkEntryResponse.from_domain(entry)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    def upsert_work_entry(
        self,
        task_id: int,
        entry_date: date,
        request: WorkEntryWriteRequest,
        authorization: AuthorizationHeader = None,
    ) -> WorkEntryResponse:
        try:
            entry = self._service.upsert_work_entry(
                self._username(authorization),
                request.to_upsert_command(task_id, entry_date),
            )
            return WorkEntryResponse.from_domain(entry)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    def delete_work_entry(self, task_id: int, entry_date: date, authorization: AuthorizationHeader = None) -> Response:
        try:
            self._service.delete_work_entry(self._username(authorization), task_id, entry_date)
            return Response(status_code=status.HTTP_204_NO_CONTENT)
        except TaskNotFoundError as error:
            raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail=str(error)) from error

    def export_tasks(
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
        try:
            content: bytes = self._exporter.export(
                self._service.list_all_tasks(self._username(authorization)),
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

    def _username(self, authorization: str | None) -> str:
        if authorization is None or not authorization.startswith("Bearer "):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Bearer access token is required.",
                headers={"WWW-Authenticate": "Bearer"},
            )

        try:
            payload = decode_access_token(authorization.removeprefix("Bearer ").strip())
            user = self._auth_service.get_user(str(payload["sub"]))
            return user.username
        except AuthenticationError as error:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=str(error),
                headers={"WWW-Authenticate": "Bearer"},
            ) from error
