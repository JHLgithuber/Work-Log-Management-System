# 검증된 Avalonia Browser 한글 IME 패치

공식 Avalonia 11.3.22에 Browser 입력 수정만 최소 이식했다. 네이티브 패키지는 공식 11.3.22를 유지한다. 앱 자체 HTML textarea 우회 코드는 없다. 프레임워크 내부 IME DOM은 단일행 INPUT, 다중행 TEXTAREA를 사용한다.

- 공식 기준: AvaloniaUI/Avalonia 627ae9ef921621e27e7aa58df2796fbadb50af88
- backport 최종: 4bc9eec6f931e73a470409547afca62ad8f9d9b3
- upstream main 기준: c1e4fab24c38be5d74aba5a8ec1ac9f04bfd5baf
- upstream 최종: 9ac33b2018491707607136717a1cfd836adf155d
- 공개 upstream PR은 아직 없으며 사람 검토 대기다. 공식 수정판으로 표시하지 않는다.
- 로컬 패키지 SHA256: 54531ebae54613e07032eca9541b63e52960c5bf5db3ceeb70e1d56a0d61b4da

NuGet.Config의 상대 경로 로컬 피드와 중앙 패키지 버전으로 고정했다. Dockerfile도 이 피드를 복사한다. 이 패키지를 공식 NuGet에 게시하지 않는다. MIT 라이선스는 LICENSE.md에 보존했다.

## 재현

앱은 저장소 루트에서 `dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser/WorkLogManagementSystem_UI.Browser.csproj -c Release -o out/browser`로 빌드한다. .NET SDK 10.0.402와 wasm-tools 10.0.402.1, Python3가 필요하다. Dockerfile은 기존 배포 절차대로 이 도구를 설치한다.

패키지를 소스에서 다시 만드는 별도 절차는 `bash vendor/avalonia/rebuild/rebuild.sh`다. 공식 Git 기준을 체크아웃하고 포함된 patch를 적용한다. 기존 검증 패키지를 덮어쓰지 않는다. npm 개발 의존성과 빌드 도구 버전/환경 차이로 재생성 ZIP의 비트 단위 해시는 달라질 수 있다. 배포 검증은 포함된 검증 패키지의 위 SHA256을 기준으로 한다. 공식 강한 이름 키는 공식 저장소에 있는 빌드 입력을 사용하며 개인 배포 서명키를 요구하지 않는다.

## 기존 qa-ime.3 검증 기록 및 한계

실제 Chrome/Gboard 한국어 자동 줄바꿈·공백·연속 Enter·중간 편집 및 업무일지 저장/DB/재조회 검증. 독립 PC에서는 입력선택29/29, 포커스·클립보드15/15, readonly·재부착14/14 통과. PC 검사는 CDP이며 실제 OS IME/물리 키보드 검증과 다르다. JS 단위11/11, 앱 UI7/7 및 가짜 HTTP17/17 통과.

긴 문자열에서 추가된 음절은 앱3회/기본 HTML2회 모두 신뢰된 Gboard compositionupdate 원문에 이미 있었다. 프레임워크에서 키보드 보정 문자를 임의 삭제하지 않는다.

Android 공통 편집기의 Multiline/ReturnKey 설정은 별도 앱 커밋이다. 실제 에뮬레이터 검사18/18 및 두 줄 저장이 통과했지만 ARM64 실기기 실행은 아직 검증하지 않았다. 운영 backend/스키마 변경은 없다.


추가 조합 포커스 수정은 docs/browser-focus-regression.ko.md를 참조한다. 최소 재현은 vendor/avalonia/tests/focus-repro에 있으며 합성 검사와 실제 키보드 검사를 구분한다.

현재 패키지는 11.3.22-qa-ime.6이다. 포커스 추가 수정의 검증 수치와 upstream SDK 빌드 장애는 위 추가 보고서에 구분해 기록했다.
