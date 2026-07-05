from __future__ import annotations

from dataclasses import dataclass, field
from datetime import date, datetime, timezone

from app.database import Database
from app.domain import DateBounds, Task, TaskCreate, TaskUpdate, WorkEntryUpsert
from app.repository import TaskRepository
from app.service import TaskService


@dataclass(frozen=True)
class DummyEntrySpec:
    entry_date: date
    performed_content: str
    retrospective: str


@dataclass(frozen=True)
class DummyTaskSpec:
    title: str
    content: str
    start_at: datetime
    due_at: datetime
    actual_end_at: datetime | None = None
    entries: list[DummyEntrySpec] = field(default_factory=list)
    children: list["DummyTaskSpec"] = field(default_factory=list)


@dataclass(frozen=True)
class SeedResult:
    task_count: int
    entry_count: int


class DummyDataSeeder:
    def __init__(self, service: TaskService) -> None:
        self._service: TaskService = service

    def seed(self) -> SeedResult:
        task_count: int = 0
        entry_count: int = 0
        for specification in self._build_specs():
            seeded_task, child_task_count, child_entry_count = self._seed_task(None, specification)
            task_count += 1 + child_task_count
            entry_count += len(seeded_task.entries) + child_entry_count
        return SeedResult(task_count=task_count, entry_count=entry_count)

    def _seed_task(self, parent_id: int | None, specification: DummyTaskSpec) -> tuple[Task, int, int]:
        task: Task = self._upsert_task(parent_id, specification)
        for entry_specification in specification.entries:
            self._service.upsert_work_entry(
                WorkEntryUpsert(
                    task_id=task.id,
                    entry_date=entry_specification.entry_date,
                    performed_content=entry_specification.performed_content,
                    retrospective=entry_specification.retrospective,
                )
            )

        child_task_count: int = 0
        child_entry_count: int = 0
        for child_specification in specification.children:
            child_task, nested_task_count, nested_entry_count = self._seed_task(task.id, child_specification)
            child_task_count += 1 + nested_task_count
            child_entry_count += len(child_task.entries) + nested_entry_count

        return self._service.get_task(task.id), child_task_count, child_entry_count

    def _upsert_task(self, parent_id: int | None, specification: DummyTaskSpec) -> Task:
        existing_task: Task | None = self._find_existing_task(parent_id, specification.title)
        if existing_task is None:
            return self._service.create_task(
                TaskCreate(
                    parent_id=parent_id,
                    title=specification.title,
                    content=specification.content,
                    start_at=specification.start_at,
                    due_at=specification.due_at,
                    actual_end_at=specification.actual_end_at,
                )
            )

        return self._service.update_task(
            existing_task.id,
            TaskUpdate(
                parent_id=parent_id,
                title=specification.title,
                content=specification.content,
                start_at=specification.start_at,
                due_at=specification.due_at,
                actual_end_at=specification.actual_end_at,
            ),
        )

    def _find_existing_task(self, parent_id: int | None, title: str) -> Task | None:
        for root_task in self._service.list_all_tasks():
            found_task: Task | None = self._find_in_tree(root_task, parent_id, title)
            if found_task is not None:
                return found_task
        return None

    def _find_in_tree(self, task: Task, parent_id: int | None, title: str) -> Task | None:
        if task.parent_id == parent_id and task.title == title:
            return task
        for child in task.children:
            found_child: Task | None = self._find_in_tree(child, parent_id, title)
            if found_child is not None:
                return found_child
        return None

    def _build_specs(self) -> list[DummyTaskSpec]:
        return [
            DummyTaskSpec(
                title="업무일지 작성체계 완성",
                content="FastAPI 백엔드와 Avalonia 클라이언트를 연결해 과업, 하위 과업, 일자별 수행내용/회고, 엑셀 내보내기를 완성한다.",
                start_at=self._utc(2026, 7, 1),
                due_at=self._utc(2026, 7, 31, 23, 59, 59),
                entries=[
                    DummyEntrySpec(
                        entry_date=date(2026, 7, 5),
                        performed_content="요구사항을 과업/일자별 기록 모델로 재정리하고 FastAPI 라우트 구조를 점검했다.",
                        retrospective="과업내용과 수행내용을 분리하니 엑셀 내보내기 기준이 명확해졌다.",
                    ),
                    DummyEntrySpec(
                        entry_date=date(2026, 7, 6),
                        performed_content="날짜별 조회 화면에서 선택 과업의 일자별 수행내용과 회고를 저장하도록 연결할 예정이다.",
                        retrospective="UI 저장 흐름에서 과업 저장과 일자별 기록 저장의 실패 처리를 더 세분화할 필요가 있다.",
                    ),
                ],
                children=[
                    DummyTaskSpec(
                        title="FastAPI 백엔드 데이터 모델 정리",
                        content="tasks 테이블은 과업 기본정보만 보관하고 work_entries 테이블은 일자별 수행내용과 회고를 보관한다.",
                        start_at=self._utc(2026, 7, 1),
                        due_at=self._utc(2026, 7, 12, 23, 59, 59),
                        entries=[
                            DummyEntrySpec(
                                entry_date=date(2026, 7, 5),
                                performed_content="work_entries 테이블과 /tasks/{id}/entries/{date} API를 추가했다.",
                                retrospective="upsert 방식이라 클라이언트 저장 버튼을 단순하게 유지할 수 있었다.",
                            )
                        ],
                    ),
                    DummyTaskSpec(
                        title="Avalonia 반응형 UI 개선",
                        content="데스크톱과 좁은 화면에서 모두 사용할 수 있도록 과업 목록과 편집기를 폭에 따라 재배치한다.",
                        start_at=self._utc(2026, 7, 3),
                        due_at=self._utc(2026, 7, 20, 23, 59, 59),
                        entries=[
                            DummyEntrySpec(
                                entry_date=date(2026, 7, 5),
                                performed_content="2열/1열 전환 기준을 정하고 입력 영역을 과업내용, 수행내용, 회고로 분리했다.",
                                retrospective="XAML만으로 버티기보다 코드비하인드에서 명시적으로 전환하는 편이 안정적이었다.",
                            )
                        ],
                        children=[
                            DummyTaskSpec(
                                title="엑셀 내보내기 검증",
                                content="일자 섹터별로 과업 제목, 과업내용, 수행내용, 회고가 들어가는지 확인한다.",
                                start_at=self._utc(2026, 7, 5),
                                due_at=self._utc(2026, 7, 9, 23, 59, 59),
                                entries=[
                                    DummyEntrySpec(
                                        entry_date=date(2026, 7, 5),
                                        performed_content="XLSX 파일 시그니처와 일자 섹터 행 생성을 확인했다.",
                                        retrospective="다음 단계에서는 스타일과 컬럼 너비를 추가하면 더 읽기 좋다.",
                                    )
                                ],
                            )
                        ],
                    ),
                ],
            ),
            DummyTaskSpec(
                title="운영 점검 루틴",
                content="매일 서버 상태, 백업 여부, 최근 작업 로그를 확인하는 상시 과업이다.",
                start_at=DateBounds.MINIMUM,
                due_at=DateBounds.MAXIMUM,
                entries=[
                    DummyEntrySpec(
                        entry_date=date(2026, 7, 5),
                        performed_content="헬스체크 엔드포인트와 SQLite 파일 생성 여부를 확인했다.",
                        retrospective="운영 점검 과업은 기간을 무제한으로 두면 홈 화면에서 항상 확인하기 쉽다.",
                    )
                ],
            ),
            DummyTaskSpec(
                title="7월 첫째 주 회의 준비",
                content="주간 회의에서 공유할 진행 현황, 리스크, 다음 액션을 정리한다.",
                start_at=self._utc(2026, 7, 4),
                due_at=self._utc(2026, 7, 7, 18, 0, 0),
                entries=[
                    DummyEntrySpec(
                        entry_date=date(2026, 7, 4),
                        performed_content="회의 안건 초안을 작성하고 백엔드/API 변경사항을 요약했다.",
                        retrospective="데모 데이터가 있어야 화면 흐름을 설명하기 쉽다.",
                    ),
                    DummyEntrySpec(
                        entry_date=date(2026, 7, 5),
                        performed_content="Avalonia 화면에서 날짜 선택 후 과업별 수행내용이 보이는지 점검했다.",
                        retrospective="오늘 날짜에 걸치는 과업을 충분히 넣어두면 QA가 편하다.",
                    ),
                ],
            ),
        ]

    def _utc(
        self,
        year: int,
        month: int,
        day: int,
        hour: int = 0,
        minute: int = 0,
        second: int = 0,
    ) -> datetime:
        return datetime(year, month, day, hour, minute, second, tzinfo=timezone.utc)


def main() -> None:
    database: Database = Database()
    database.initialize()
    service: TaskService = TaskService(TaskRepository(database))
    result: SeedResult = DummyDataSeeder(service).seed()
    print(f"Seeded dummy worklog data: tasks={result.task_count}, entries={result.entry_count}")


if __name__ == "__main__":
    main()
