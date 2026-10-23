from unittest import TestCase
from pydantic import ValidationError
from app.schemas import TaskWriteRequest

class TitleTests(TestCase):
    def test_blank_title_rejected_and_valid_title_trimmed(self):
        for title in (' ', '\t\n'):
            with self.assertRaises(ValidationError):
                TaskWriteRequest(title=title)
        self.assertEqual(TaskWriteRequest(title=' valid ').title, 'valid')
