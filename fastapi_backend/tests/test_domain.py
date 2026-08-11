from __future__ import annotations

from datetime import datetime, timezone
from unittest import TestCase

from app.domain import DateBounds, KoreaDateTime


class DateBoundsTests(TestCase):
    def test_minimum_is_representable_as_utc(self) -> None:
        self.assertEqual(
            DateBounds.MINIMUM.astimezone(timezone.utc),
            datetime.min.replace(tzinfo=timezone.utc),
        )

    def test_normalize_clamps_utc_minimum_to_safe_korea_time(self) -> None:
        normalized = DateBounds.normalize(datetime.min.replace(tzinfo=timezone.utc))

        self.assertEqual(normalized, DateBounds.MINIMUM)
        self.assertEqual(normalized.isoformat(), "0001-01-01T09:00:00+09:00")

    def test_existing_unsafe_database_minimum_is_repaired_when_read(self) -> None:
        database_type = KoreaDateTime()

        normalized = database_type.process_result_value(
            "0001-01-01T00:00:00.000000+09:00",
            dialect=None,
        )

        self.assertEqual(normalized, DateBounds.MINIMUM)
