from __future__ import annotations

import os
import subprocess
from pathlib import Path
from unittest import TestCase


class DockerEntrypointTests(TestCase):
    entrypoint = Path(__file__).resolve().parent.parent / "docker-entrypoint.sh"

    def run_entrypoint(self, **environment: str) -> subprocess.CompletedProcess[str]:
        clean_environment = {
            key: value
            for key, value in os.environ.items()
            if key not in {"WORKLOG_ADMIN_USERNAME", "WORKLOG_ADMIN_PASSWORD"}
        }
        clean_environment.update(environment)
        clean_environment["WORKLOG_JWT_SECRET_KEY"] = "secure-test-secret"
        return subprocess.run(
            [
                str(self.entrypoint),
                "sh",
                "-c",
                'printf "%s|%s" "${WORKLOG_ADMIN_USERNAME:-}" "${WORKLOG_ADMIN_PASSWORD:-}"',
            ],
            env=clean_environment,
            check=False,
            capture_output=True,
            text=True,
        )

    def test_default_admin_placeholders_are_not_applied(self) -> None:
        result = self.run_entrypoint(
            WORKLOG_ADMIN_USERNAME="ENTER_ADMIN_USERNAME",
            WORKLOG_ADMIN_PASSWORD="ENTER_ADMIN_PASSWORD",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "|")

    def test_custom_admin_credentials_are_preserved(self) -> None:
        result = self.run_entrypoint(
            WORKLOG_ADMIN_USERNAME="custom-admin",
            WORKLOG_ADMIN_PASSWORD="custom-password",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "custom-admin|custom-password")

    def test_partial_custom_admin_configuration_is_rejected(self) -> None:
        result = self.run_entrypoint(WORKLOG_ADMIN_USERNAME="custom-admin")

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("WORKLOG_ADMIN_PASSWORD is required", result.stderr)
