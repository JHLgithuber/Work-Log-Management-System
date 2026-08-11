from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Header, HTTPException, Response, status

from app.auth import AuthenticatedUser, AuthService, UserAlreadyExistsError
from app.auth_schemas import (
    ChangePasswordRequest,
    LoginRequest,
    RefreshTokenRequest,
    RegisterRequest,
    TokenResponse,
    UserResponse,
)
from app.database import Database
from app.security import AuthenticationError, decode_access_token

AuthorizationHeader = Annotated[str | None, Header(alias="Authorization")]


class AuthRouter:
    def __init__(self, database: Database) -> None:
        self._service = AuthService(database)
        self.router = APIRouter(prefix="/auth", tags=["auth"])
        self._register_routes()

    async def current_user(self, authorization: AuthorizationHeader = None) -> AuthenticatedUser:
        if authorization is None or not authorization.startswith("Bearer "):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Bearer access token is required.",
                headers={"WWW-Authenticate": "Bearer"},
            )

        token = authorization.removeprefix("Bearer ").strip()
        try:
            payload = decode_access_token(token)
            return await self._service.get_user(str(payload["sub"]))
        except AuthenticationError as error:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail=str(error),
                headers={"WWW-Authenticate": "Bearer"},
            ) from error

    def _register_routes(self) -> None:
        self.router.add_api_route("/register", self.register, methods=["POST"], response_model=UserResponse, status_code=201)
        self.router.add_api_route("/login", self.login, methods=["POST"], response_model=TokenResponse)
        self.router.add_api_route("/refresh", self.refresh, methods=["POST"], response_model=TokenResponse)
        self.router.add_api_route("/me", self.me, methods=["GET"], response_model=UserResponse)
        self.router.add_api_route("/password", self.change_password, methods=["PUT"], status_code=204)

    async def register(self, request: RegisterRequest, authorization: AuthorizationHeader = None) -> UserResponse:
        await self.current_user(authorization)
        try:
            user = await self._service.register(request.username, request.password, request.display_name)
            return UserResponse.from_domain(user)
        except UserAlreadyExistsError as error:
            raise HTTPException(status_code=status.HTTP_409_CONFLICT, detail=str(error)) from error
        except AuthenticationError as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error

    async def login(self, request: LoginRequest) -> TokenResponse:
        try:
            return TokenResponse.from_domain(await self._service.login(request.username, request.password))
        except AuthenticationError as error:
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail=str(error)) from error

    async def refresh(self, request: RefreshTokenRequest) -> TokenResponse:
        try:
            return TokenResponse.from_domain(await self._service.refresh(request.refresh_token))
        except AuthenticationError as error:
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail=str(error)) from error

    async def me(self, authorization: AuthorizationHeader = None) -> UserResponse:
        return UserResponse.from_domain(await self.current_user(authorization))

    async def change_password(self, request: ChangePasswordRequest, authorization: AuthorizationHeader = None) -> Response:
        user = await self.current_user(authorization)
        try:
            await self._service.change_password(user.username, request.current_password, request.new_password)
            return Response(status_code=status.HTTP_204_NO_CONTENT)
        except AuthenticationError as error:
            raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail=str(error)) from error
