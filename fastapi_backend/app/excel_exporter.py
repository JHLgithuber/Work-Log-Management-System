from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime
from io import BytesIO
from typing import Iterable
from xml.sax.saxutils import escape
from zipfile import ZIP_DEFLATED, ZipFile

from app.domain import Task, WorkEntry


class NoExportableSheetsError(ValueError):
    pass


@dataclass(frozen=True)
class TaskCard:
    row: int
    column: int
    depth: int
    task_id: int
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None
    performed_content: str
    retrospective: str
    has_daily_entry: bool


class WorksheetBuilder:
    def __init__(self, sheet_date: date) -> None:
        self._sheet_date: date = sheet_date
        self._cells: dict[int, dict[int, str]] = {}
        self._merges: list[str] = []
        self._row_heights: dict[int, float] = {}
        self._max_row: int = 1
        self._max_column: int = 1

    def set_cell(self, row: int, column: int, value: object, style: int = 0) -> None:
        self._max_row = max(self._max_row, row)
        self._max_column = max(self._max_column, column)
        text: str = escape(str(value))
        self._cells.setdefault(row, {})[column] = (
            f'<c r="{self._cell_name(row, column)}" s="{style}" t="inlineStr"><is><t>{text}</t></is></c>'
        )

    def set_blank(self, row: int, column: int, style: int = 0) -> None:
        self._max_row = max(self._max_row, row)
        self._max_column = max(self._max_column, column)
        self._cells.setdefault(row, {})[column] = f'<c r="{self._cell_name(row, column)}" s="{style}"/>'

    def merge(self, first_row: int, first_column: int, last_row: int, last_column: int) -> None:
        self._merges.append(
            f"{self._cell_name(first_row, first_column)}:{self._cell_name(last_row, last_column)}"
        )

    def set_row_height(self, row: int, height: float) -> None:
        self._row_heights[row] = height

    def fill_range(self, first_row: int, first_column: int, last_row: int, last_column: int, style: int) -> None:
        for row in range(first_row, last_row + 1):
            for column in range(first_column, last_column + 1):
                if column not in self._cells.get(row, {}):
                    self.set_blank(row, column, style)

    def to_xml(self) -> str:
        rows: list[str] = []
        for row_index in range(1, self._max_row + 1):
            cells: dict[int, str] = self._cells.get(row_index, {})
            height_attribute: str = ""
            if row_index in self._row_heights:
                height_attribute = f' ht="{self._row_heights[row_index]}" customHeight="1"'
            rows.append(
                f'<row r="{row_index}"{height_attribute}>'
                f'{"".join(cells[column] for column in sorted(cells))}'
                "</row>"
            )

        merge_xml: str = ""
        if self._merges:
            merge_xml = (
                f'<mergeCells count="{len(self._merges)}">'
                f'{"".join(f"<mergeCell ref=\"{merge}\"/>" for merge in self._merges)}'
                "</mergeCells>"
            )

        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            "<sheetViews><sheetView showGridLines=\"0\" workbookViewId=\"0\"/></sheetViews>"
            f"<dimension ref=\"A1:{self._cell_name(self._max_row, self._max_column)}\"/>"
            "<cols>"
            f"{''.join(self._column_xml(column) for column in range(1, self._max_column + 1))}"
            "</cols>"
            "<sheetFormatPr defaultRowHeight=\"18\"/>"
            f"<sheetData>{''.join(rows)}</sheetData>"
            f"{merge_xml}"
            "<pageMargins left=\"0.25\" right=\"0.25\" top=\"0.5\" bottom=\"0.5\" header=\"0.3\" footer=\"0.3\"/>"
            "</worksheet>"
        )

    def _column_xml(self, column: int) -> str:
        width: int = 18 if (column - 1) % TaskExcelExporter.CARD_STRIDE < 6 else 3
        return f'<col min="{column}" max="{column}" width="{width}" customWidth="1"/>'

    def _cell_name(self, row: int, column: int) -> str:
        return f"{self._column_name(column)}{row}"

    def _column_name(self, column_index: int) -> str:
        name: str = ""
        while column_index:
            column_index, remainder = divmod(column_index - 1, 26)
            name = chr(65 + remainder) + name
        return name


class TaskExcelExporter:
    CARD_WIDTH: int = 6
    CARD_HEIGHT: int = 9
    CARD_COLUMN_GAP: int = 1
    CARD_ROW_GAP: int = 2
    ROOT_ROW_GAP: int = 3
    CARD_STRIDE: int = CARD_WIDTH + CARD_COLUMN_GAP

    DEFAULT_STYLE: int = 0
    SHEET_TITLE_STYLE: int = 1
    ROOT_TITLE_STYLE: int = 2
    CHILD_TITLE_STYLE: int = 3
    LABEL_STYLE: int = 4
    VALUE_STYLE: int = 5
    BODY_STYLE: int = 6
    CONTEXT_TITLE_STYLE: int = 7

    def export(
        self,
        tasks: Iterable[Task],
        start_date: date | None = None,
        end_date: date | None = None,
    ) -> bytes:
        root_tasks: list[Task] = list(tasks)
        sheet_dates: list[date] = self._collect_sheet_dates(root_tasks)
        if start_date is not None and end_date is not None:
            sheet_dates = [
                sheet_date
                for sheet_date in sheet_dates
                if start_date <= sheet_date <= end_date
            ]
        elif start_date is not None or end_date is not None:
            sheet_dates = [
                sheet_date
                for sheet_date in sheet_dates
                if (start_date is None or sheet_date >= start_date)
                and (end_date is None or sheet_date <= end_date)
            ]

        if not sheet_dates:
            raise NoExportableSheetsError("No exportable work log entries were found for the selected date range.")

        worksheet_xml_by_index: dict[int, str] = {}
        for index, sheet_date in enumerate(sheet_dates, start=1):
            worksheet_xml_by_index[index] = self._build_sheet_xml(root_tasks, sheet_date)

        buffer: BytesIO = BytesIO()
        with ZipFile(buffer, "w", ZIP_DEFLATED) as archive:
            archive.writestr("[Content_Types].xml", self._content_types_xml(len(sheet_dates)))
            archive.writestr("_rels/.rels", self._rels_xml())
            archive.writestr("xl/workbook.xml", self._workbook_xml(sheet_dates))
            archive.writestr("xl/_rels/workbook.xml.rels", self._workbook_rels_xml(len(sheet_dates)))
            archive.writestr("xl/styles.xml", self._styles_xml())
            for index, worksheet_xml in worksheet_xml_by_index.items():
                archive.writestr(f"xl/worksheets/sheet{index}.xml", worksheet_xml)
        return buffer.getvalue()

    def _build_sheet_xml(self, root_tasks: list[Task], sheet_date: date) -> str:
        cards: list[TaskCard] = []
        current_row: int = 3
        for task in root_tasks:
            if not self._has_exportable_entry_on_date(task, sheet_date):
                continue
            used_height: int = self._layout_task(cards, task, sheet_date, current_row, 1, 0)
            current_row += used_height + self.ROOT_ROW_GAP

        builder: WorksheetBuilder = WorksheetBuilder(sheet_date)
        max_column: int = max((card.column + self.CARD_WIDTH - 1 for card in cards), default=self.CARD_WIDTH)
        builder.set_cell(1, 1, f"{sheet_date.isoformat()} 업무일지", self.SHEET_TITLE_STYLE)
        builder.fill_range(1, 1, 1, max_column, self.SHEET_TITLE_STYLE)
        builder.merge(1, 1, 1, max_column)
        builder.set_row_height(1, 28)

        if not cards:
            builder.set_cell(4, 1, "해당 일자에 수행내용 또는 회고가 입력된 과업이 없습니다.", self.BODY_STYLE)
            builder.fill_range(4, 1, 5, max_column, self.BODY_STYLE)
            builder.merge(4, 1, 5, max_column)
            return builder.to_xml()

        for card in cards:
            self._write_card(builder, card)
        return builder.to_xml()

    def _layout_task(
        self,
        cards: list[TaskCard],
        task: Task,
        sheet_date: date,
        row: int,
        column: int,
        depth: int,
    ) -> int:
        entry: WorkEntry | None = self._entry_for_date(task, sheet_date)
        cards.append(
            TaskCard(
                row=row,
                column=column,
                depth=depth,
                task_id=task.id,
                title=task.title,
                content=task.content,
                start_at=task.start_at,
                due_at=task.due_at,
                actual_end_at=task.actual_end_at,
                performed_content="" if entry is None else entry.performed_content,
                retrospective="" if entry is None else entry.retrospective,
                has_daily_entry=entry is not None and self._has_exportable_entry(entry),
            )
        )

        child_row: int = row
        child_column: int = column + self.CARD_STRIDE
        used_child_height: int = 0
        for child in task.children:
            if not self._has_exportable_entry_on_date(child, sheet_date):
                continue
            child_height: int = self._layout_task(cards, child, sheet_date, child_row, child_column, depth + 1)
            child_row += child_height + self.CARD_ROW_GAP
            used_child_height += child_height + self.CARD_ROW_GAP

        if used_child_height > 0:
            used_child_height -= self.CARD_ROW_GAP
        return max(self.CARD_HEIGHT, used_child_height)

    def _write_card(self, builder: WorksheetBuilder, card: TaskCard) -> None:
        row: int = card.row
        column: int = card.column
        last_column: int = column + self.CARD_WIDTH - 1
        title_style: int = self.ROOT_TITLE_STYLE if card.depth == 0 else self.CHILD_TITLE_STYLE
        if not card.has_daily_entry:
            title_style = self.CONTEXT_TITLE_STYLE

        builder.set_cell(row, column, f"#{card.task_id} {card.title}", title_style)
        builder.fill_range(row, column, row, last_column, title_style)
        builder.merge(row, column, row, last_column)
        builder.set_row_height(row, 24)

        self._write_merged_pair(builder, row + 1, column, column + 1, "시작일", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 1, column + 2, column + 3, "마감일", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 1, column + 4, column + 5, "종료여부", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 2, column, column + 1, self._format_date(card.start_at), self.VALUE_STYLE)
        self._write_merged_pair(builder, row + 2, column + 2, column + 3, self._format_date(card.due_at), self.VALUE_STYLE)
        self._write_merged_pair(builder, row + 2, column + 4, column + 5, self._completion_status(card), self.VALUE_STYLE)

        self._write_merged_pair(builder, row + 3, column, last_column, "과업내용", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 4, column, last_column, card.content, self.BODY_STYLE)
        builder.set_row_height(row + 4, 48)

        self._write_merged_pair(builder, row + 5, column, last_column, "수행내용", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 6, column, last_column, card.performed_content, self.BODY_STYLE)
        builder.set_row_height(row + 6, 58)

        self._write_merged_pair(builder, row + 7, column, last_column, "회고", self.LABEL_STYLE)
        self._write_merged_pair(builder, row + 8, column, last_column, card.retrospective, self.BODY_STYLE)
        builder.set_row_height(row + 8, 58)

    def _write_merged_pair(
        self,
        builder: WorksheetBuilder,
        row: int,
        first_column: int,
        last_column: int,
        value: object,
        style: int,
    ) -> None:
        builder.set_cell(row, first_column, value, style)
        builder.fill_range(row, first_column, row, last_column, style)
        if first_column != last_column:
            builder.merge(row, first_column, row, last_column)

    def _collect_sheet_dates(self, tasks: list[Task]) -> list[date]:
        dates: set[date] = set()
        for task in tasks:
            self._collect_sheet_dates_from_task(task, dates)
        return sorted(dates)

    def _collect_sheet_dates_from_task(self, task: Task, dates: set[date]) -> None:
        for entry in task.entries:
            if self._has_exportable_entry(entry):
                dates.add(entry.entry_date)
        for child in task.children:
            self._collect_sheet_dates_from_task(child, dates)

    def _has_exportable_entry_on_date(self, task: Task, sheet_date: date) -> bool:
        if self._entry_for_date(task, sheet_date) is not None:
            return True
        return any(self._has_exportable_entry_on_date(child, sheet_date) for child in task.children)

    def _entry_for_date(self, task: Task, sheet_date: date) -> WorkEntry | None:
        for entry in task.entries:
            if entry.entry_date == sheet_date and self._has_exportable_entry(entry):
                return entry
        return None

    def _has_exportable_entry(self, entry: WorkEntry) -> bool:
        return bool(entry.performed_content.strip() or entry.retrospective.strip())

    def _completion_status(self, card: TaskCard) -> str:
        return "종료" if card.actual_end_at is not None else "진행 중"

    def _format_date(self, value: datetime) -> str:
        if value.year <= 1:
            return "시작 무제한"
        if value.year >= 9999:
            return "종료 무제한"
        return value.date().isoformat()

    def _content_types_xml(self, sheet_count: int) -> str:
        sheet_overrides: str = "".join(
            '<Override PartName="/xl/worksheets/sheet{index}.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'.format(
                index=index
            )
            for index in range(1, sheet_count + 1)
        )
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
            '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
            '<Default Extension="xml" ContentType="application/xml"/>'
            '<Override PartName="/xl/workbook.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
            '<Override PartName="/xl/styles.xml" '
            'ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>'
            f"{sheet_overrides}"
            "</Types>"
        )

    def _rels_xml(self) -> str:
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            '<Relationship Id="rId1" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
            'Target="xl/workbook.xml"/>'
            "</Relationships>"
        )

    def _workbook_xml(self, sheet_dates: list[date]) -> str:
        sheets_xml: str = "".join(
            f'<sheet name="{sheet_date.isoformat()}" sheetId="{index}" r:id="rId{index}"/>'
            for index, sheet_date in enumerate(sheet_dates, start=1)
        )
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" '
            'xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
            f"<sheets>{sheets_xml}</sheets>"
            "</workbook>"
        )

    def _workbook_rels_xml(self, sheet_count: int) -> str:
        sheet_relationships: str = "".join(
            '<Relationship Id="rId{index}" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" '
            'Target="worksheets/sheet{index}.xml"/>'.format(index=index)
            for index in range(1, sheet_count + 1)
        )
        styles_relationship_id: int = sheet_count + 1
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
            f"{sheet_relationships}"
            f'<Relationship Id="rId{styles_relationship_id}" '
            'Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" '
            'Target="styles.xml"/>'
            "</Relationships>"
        )

    def _styles_xml(self) -> str:
        return (
            '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
            '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
            '<fonts count="5">'
            '<font><sz val="10"/><name val="Inter"/></font>'
            '<font><b/><sz val="16"/><color rgb="FFFFFFFF"/><name val="Inter"/></font>'
            '<font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Inter"/></font>'
            '<font><b/><sz val="10"/><color rgb="FF263241"/><name val="Inter"/></font>'
            '<font><sz val="10"/><color rgb="FF1F2937"/><name val="Inter"/></font>'
            "</fonts>"
            '<fills count="8">'
            '<fill><patternFill patternType="none"/></fill>'
            '<fill><patternFill patternType="gray125"/></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FF17324D"/><bgColor indexed="64"/></patternFill></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FF226C5D"/><bgColor indexed="64"/></patternFill></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FF3B5B86"/><bgColor indexed="64"/></patternFill></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FFE9EEF5"/><bgColor indexed="64"/></patternFill></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FFFFFFFF"/><bgColor indexed="64"/></patternFill></fill>'
            '<fill><patternFill patternType="solid"><fgColor rgb="FF8A95A6"/><bgColor indexed="64"/></patternFill></fill>'
            "</fills>"
            '<borders count="2">'
            "<border><left/><right/><top/><bottom/><diagonal/></border>"
            '<border><left style="thin"><color rgb="FFB9C4D0"/></left>'
            '<right style="thin"><color rgb="FFB9C4D0"/></right>'
            '<top style="thin"><color rgb="FFB9C4D0"/></top>'
            '<bottom style="thin"><color rgb="FFB9C4D0"/></bottom><diagonal/></border>'
            "</borders>"
            '<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>'
            '<cellXfs count="8">'
            '<xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>'
            '<xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>'
            '<xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>'
            '<xf numFmtId="0" fontId="2" fillId="4" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>'
            '<xf numFmtId="0" fontId="3" fillId="5" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>'
            '<xf numFmtId="0" fontId="4" fillId="6" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>'
            '<xf numFmtId="0" fontId="4" fillId="6" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="top" wrapText="1"/></xf>'
            '<xf numFmtId="0" fontId="2" fillId="7" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>'
            "</cellXfs>"
            '<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>'
            "</styleSheet>"
        )
