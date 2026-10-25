# Linux 단일 파일용 IBus 키코드 보정

공식 Avalonia 11.3.22 기준 627ae9ef921621e27e7aa58df2796fbadb50af88의 FreeDesktop 소스에 기존 앱의 IBus 보정과 같은 `keyCode >= 8`일 때 `keyCode -= 8`을 직접 적용했습니다. 공식 배포판이 아닌 로컬 패키지입니다. Browser qa-ime.6 패키지는 변경하지 않습니다. MIT 라이선스는 상위 LICENSE.md에 있습니다.

기존 Harmony 런타임 패치는 자체 포함 단일 파일 호스트에서 clrjit를 찾지 못해 앱 시작을 중단했습니다. 전체 내용 추출 옵션도 해결하지 못했습니다. 보정을 제거하거나 오류를 숨기는 대신, 동일 보정을 빌드 시 적용하여 Harmony 의존성과 런타임 코드 변경을 없앴습니다. Linux 대상에만 로컬 FreeDesktop 패키지를 명시합니다.

패키지: Avalonia.FreeDesktop 11.3.22.1-qa-ibus.1

SHA256: a2c006323c9c6816a24b860338a715b20fbfb74002e3e9d91b3808f3de318747

## 재현

.NET SDK 10.0.402에서 저장소 루트 기준:

```sh
dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Desktop/WorkLogManagementSystem_UI.Desktop.csproj -p:PublishProfile=LinuxSingle -p:DebugType=embedded -o out/linux-single
```

DebugType 전역 인자를 유지해야 참조 프로젝트의 PDB도 포함되어 출력이 실행파일 하나가 됩니다. 이 파일은 실행할 때 네이티브 라이브러리를 사용자별 .NET 추출 디렉터리에 풉니다. .NET 별도 설치는 필요 없지만 Linux x64 GUI 및 시스템 라이브러리는 필요합니다.

패키지 재생성은 `bash vendor/avalonia/linux-ibus/rebuild.sh <존재하지 않는 작업 경로>`로 수행합니다. 공식 기준 소스에 포함된 patch를 적용하고 공식 저장소의 빌드용 강한 이름 키를 사용합니다. 개인 인증서나 비밀 키는 필요하지 않습니다. 빌드 경로/도구에 따라 ZIP의 비트 동일성은 보장하지 않습니다.

## 검증과 한계

Ubuntu X11에서 단일 실행파일만 둔 디렉터리로 실행하고, 격리 SQLite·일반 QA 계정으로 실제 키 입력 로그인을 확인했습니다. IBus hangul 두벌식으로 `한글 공백` + Enter + `줄바꿈` 입력, 조합 중 포커스 이동, 저장 및 SQLite의 `한글 공백\n줄바꿈` 원문 일치를 확인했습니다. 날짜 이동 후 재조회, 정상 종료 뒤 단일 파일 재실행 및 실제 키 입력 재로그인 후 같은 비고의 표시까지 확인했습니다. 테스트 조작 중 저장된 아이디에 중복 입력/한글 모드로 영문 입력하여 로그인 실패가 있었고, 필드를 비우고 영어 모드로 재입력하여 정상 복구했습니다. UI 회귀 14개 통과. 다크 버튼 변경은 유지됩니다.

Windows·Android·NAS 배포는 이 변경의 대상이 아닙니다. Wayland, Fcitx 및 다른 배포판은 미검증입니다. 기존 앱과 사용자 설정을 보존하며 새 버전 경로로 설치합니다.
