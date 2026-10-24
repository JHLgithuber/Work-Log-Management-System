from datetime import date
from unittest import TestCase
from xml.etree import ElementTree
from app.excel_exporter import WorksheetBuilder

class ExportTests(TestCase):
    def test_control_characters_do_not_break_xml_or_whitespace(self):
        sheet = WorksheetBuilder(date(2099, 1, 1))
        sheet.set_cell(1, 1, ' 한글 😀\x00\x0b\x1f & <tag>\t\n')
        root = ElementTree.fromstring(sheet.to_xml())
        value = root.find('.//{*}t')
        self.assertEqual(value.text, ' 한글 😀��� & <tag>\t\n')
        self.assertEqual(value.attrib['{http://www.w3.org/XML/1998/namespace}space'], 'preserve')
