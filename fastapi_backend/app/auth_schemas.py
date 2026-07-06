from __future__ import annotations

from pydantic import BaseModel, Field

from app.auth import AuthenticatedUser, TokenPair


class RegisterRequest(BaseModel):
    username: str = Field(min_length=1, max_length=100)
    password: str = Field(min_length=8, max_length=256)
    display_name: str | None = Field(default=None, max_length=100)


class LoginRequest(BaseModel):
    username: str = Field(min_length=1, max_length=100)
    password: str = Field(min_length=1, max_length=256)


class TokenResponse(BaseModel):
    access_token: str
    token_type: str

    @classmethod
    def from_domain(cls, token: TokenPair) -> "TokenResponse":
        return cls(access_token=token.access_token, token_type=token.token_type)


class UserResponse(BaseModel):
    id: int | None
    username: str
    display_name: str

    @classmethod
    def from_domain(cls, user: AuthenticatedUser) -> "UserResponse":
        return cls(id=user.id, username=user.username, display_name=user.display_name)
