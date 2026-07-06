from __future__ import annotations

import base64
import hashlib
import hmac
import json
import secrets
from datetime import datetime, timedelta
from typing import Any

from app.domain import DateBounds
from app.settings import settings


class AuthenticationError(Exception):
    pass


_JWT_ALGORITHM = "HS256"
_PASSWORD_ALGORITHM = "pbkdf2_sha256"
_PASSWORD_ITERATIONS = 210_000


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, _PASSWORD_ITERATIONS)
    return "$".join(
        [
            _PASSWORD_ALGORITHM,
            str(_PASSWORD_ITERATIONS),
            _base64url_encode(salt),
            _base64url_encode(digest),
        ]
    )


def verify_password(password: str, password_hash: str) -> bool:
    try:
        algorithm, iterations_text, salt_text, digest_text = password_hash.split("$", 3)
        if algorithm != _PASSWORD_ALGORITHM:
            return False
        iterations = int(iterations_text)
        salt = _base64url_decode(salt_text)
        expected_digest = _base64url_decode(digest_text)
    except (ValueError, TypeError):
        return False

    actual_digest = hashlib.pbkdf2_hmac("sha256", password.encode(), salt, iterations)
    return hmac.compare_digest(actual_digest, expected_digest)


def create_access_token(username: str, user_id: int | None) -> str:
    now = DateBounds.now()
    expires_at = now + timedelta(minutes=settings.jwt_access_token_expire_minutes)
    payload: dict[str, Any] = {
        "sub": username,
        "user_id": user_id,
        "iat": int(now.timestamp()),
        "exp": int(expires_at.timestamp()),
    }
    return _encode_jwt(payload)


def decode_access_token(token: str) -> dict[str, Any]:
    parts = token.split(".")
    if len(parts) != 3:
        raise AuthenticationError("Invalid access token.")

    signing_input = f"{parts[0]}.{parts[1]}".encode()
    expected_signature = _sign(signing_input)
    actual_signature = _base64url_decode(parts[2])
    if not hmac.compare_digest(actual_signature, expected_signature):
        raise AuthenticationError("Invalid access token.")

    try:
        header = json.loads(_base64url_decode(parts[0]))
        payload = json.loads(_base64url_decode(parts[1]))
    except json.JSONDecodeError as error:
        raise AuthenticationError("Invalid access token.") from error

    if header.get("alg") != _JWT_ALGORITHM or header.get("typ") != "JWT":
        raise AuthenticationError("Invalid access token.")

    expires_at = payload.get("exp")
    if not isinstance(expires_at, int) or DateBounds.now().timestamp() > expires_at:
        raise AuthenticationError("Access token has expired.")

    subject = payload.get("sub")
    if not isinstance(subject, str) or subject.strip() == "":
        raise AuthenticationError("Invalid access token.")

    return payload


def _encode_jwt(payload: dict[str, Any]) -> str:
    header = {"alg": _JWT_ALGORITHM, "typ": "JWT"}
    header_text = _base64url_encode(json.dumps(header, separators=(",", ":")).encode())
    payload_text = _base64url_encode(json.dumps(payload, separators=(",", ":")).encode())
    signature = _base64url_encode(_sign(f"{header_text}.{payload_text}".encode()))
    return f"{header_text}.{payload_text}.{signature}"


def _sign(value: bytes) -> bytes:
    return hmac.new(settings.jwt_secret_key.encode(), value, hashlib.sha256).digest()


def _base64url_encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode()


def _base64url_decode(value: str) -> bytes:
    padding = "=" * (-len(value) % 4)
    return base64.urlsafe_b64decode(value + padding)
