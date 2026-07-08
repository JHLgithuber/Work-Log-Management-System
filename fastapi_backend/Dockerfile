FROM python:3.12-slim AS builder

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        gcc \
        libmariadb-dev \
        pkg-config \
    && pip install --no-cache-dir uv \
    && rm -rf /var/lib/apt/lists/*

COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project


FROM python:3.12-slim AS runtime

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/app/.venv/bin:$PATH" \
    WORKLOG_APP_TITLE="Work Log Management System" \
    WORKLOG_APP_VERSION="0.1.0" \
    WORKLOG_CORS_ORIGINS="*" \
    WORKLOG_CORS_ALLOW_CREDENTIALS="true" \
    WORKLOG_JWT_ACCESS_TOKEN_EXPIRE_MINUTES="1440" \
    WORKLOG_DATABASE_URL="sqlite:////app/data/worklog.sqlite3"

# Provide WORKLOG_ADMIN_USERNAME, WORKLOG_ADMIN_PASSWORD, and
# WORKLOG_JWT_SECRET_KEY at runtime with --env-file or -e.

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libmariadb3 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system app \
    && useradd --system --gid app --home-dir /app app \
    && mkdir -p /app/data \
    && chown -R app:app /app

COPY --from=builder --chown=app:app /app/.venv /app/.venv
COPY --chown=app:app docker-entrypoint.sh ./
COPY --chown=app:app main.py ./
COPY --chown=app:app app ./app
RUN chmod +x /app/docker-entrypoint.sh

USER app

VOLUME ["/app/data"]
EXPOSE 8000

ENTRYPOINT ["./docker-entrypoint.sh"]
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
