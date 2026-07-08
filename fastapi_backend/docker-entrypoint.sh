#!/bin/sh
set -eu

if [ "${WORKLOG_JWT_SECRET_KEY:-}" = "change-me-secret" ] || [ "${WORKLOG_JWT_SECRET_KEY:-}" = "change-this-secret" ]; then
    echo "WORKLOG_JWT_SECRET_KEY must not use the example value." >&2
    exit 1
fi

if [ -n "${WORKLOG_ADMIN_USERNAME:-}" ] && [ -z "${WORKLOG_ADMIN_PASSWORD:-}" ]; then
    echo "WORKLOG_ADMIN_PASSWORD is required when WORKLOG_ADMIN_USERNAME is set." >&2
    exit 1
fi

if [ -z "${WORKLOG_ADMIN_USERNAME:-}" ] && [ -n "${WORKLOG_ADMIN_PASSWORD:-}" ]; then
    echo "WORKLOG_ADMIN_USERNAME is required when WORKLOG_ADMIN_PASSWORD is set." >&2
    exit 1
fi

exec "$@"
