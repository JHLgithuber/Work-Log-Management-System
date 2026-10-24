# Bundled browser UI + API. Build with the repository root as the context.
FROM mcr.microsoft.com/dotnet/sdk:10.0.402 AS web-builder

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

WORKDIR /src
COPY global.json NuGet.Config ./
COPY vendor/avalonia/packages ./vendor/avalonia/packages

# Emscripten in the native WebAssembly workload requires Python on Linux.
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/* \
    && dotnet workload install wasm-tools --version 10.0.402.1

COPY avalonia_ui/WorkLogManagementSystem_UI/Directory.Packages.props ./avalonia_ui/WorkLogManagementSystem_UI/
COPY avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI ./avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI
COPY avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser ./avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser

RUN dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser/WorkLogManagementSystem_UI.Browser.csproj \
        -c Release -o /out/browser \
    && test -s /out/browser/wwwroot/index.html \
    && test -s /out/browser/wwwroot/_framework/dotnet.js \
    && find /out/browser/wwwroot/_framework -type f -name '*.wasm' -print -quit | grep -q .


FROM python:3.12-slim AS backend-builder

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    UV_COMPILE_BYTECODE=1 \
    UV_LINK_MODE=copy

WORKDIR /app

RUN pip install --no-cache-dir uv==0.13.4

COPY fastapi_backend/pyproject.toml fastapi_backend/uv.lock ./
RUN uv sync --frozen --no-dev --no-install-project


FROM python:3.12-slim AS runtime

ARG VCS_REF=unknown
LABEL org.opencontainers.image.revision=$VCS_REF

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PATH="/app/.venv/bin:$PATH" \
    WORKLOG_APP_TITLE="Work Log Management System" \
    WORKLOG_APP_VERSION="0.1.0" \
    WORKLOG_CORS_ORIGINS="*" \
    WORKLOG_CORS_ALLOW_CREDENTIALS="true" \
    WORKLOG_JWT_ACCESS_TOKEN_EXPIRE_MINUTES="1440" \
    WORKLOG_JWT_REFRESH_TOKEN_EXPIRE_DAYS="90" \
    WORKLOG_DATABASE_URL="sqlite:////app/data/worklog.sqlite3"

# Supply the existing database URL, JWT key, and optional bootstrap credentials
# only at runtime. Do not put secrets in build arguments or the build context.
WORKDIR /app

RUN groupadd --system app \
    && useradd --system --gid app --home-dir /app app \
    && mkdir -p /app/data \
    && chown -R app:app /app

COPY --from=backend-builder --chown=app:app /app/.venv /app/.venv
COPY --chown=app:app fastapi_backend/docker-entrypoint.sh fastapi_backend/main.py ./
COPY --chown=app:app fastapi_backend/app ./app
COPY --from=web-builder --chown=app:app /out/browser/wwwroot /app/web
RUN chmod +x /app/docker-entrypoint.sh

USER app

VOLUME ["/app/data"]
EXPOSE 8000

ENTRYPOINT ["./docker-entrypoint.sh"]
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]
