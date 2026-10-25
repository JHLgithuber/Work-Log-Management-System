# 프런트엔드 빌드와 테스트

명령은 저장소 루트에서 실행합니다. `global.json`이 지정한 안정 .NET SDK
10.0.402(이후 10.0.4xx 패치 허용)를 설치합니다. 모든 프런트엔드 프로젝트는
.NET 10을 대상으로 합니다. 공통 프로젝트는 플랫폼에 종속되지 않으며,
플랫폼별 자격 증명 저장소와 진입점은 각 플랫폼 프로젝트에 유지합니다.

## 버전 정책

- .NET 10은 현재 LTS 버전입니다. .NET 9 지원은 2026년 11월 10일,
  .NET 10 지원은 2028년 11월 14일 종료됩니다.
- Avalonia는 기존 11.3 계열의 11.3.22로 갱신합니다. Avalonia 12 전환은 별도
  마이그레이션이며 이번 안정성 수정에 필요하지 않습니다.
- `Directory.Packages.props`에서 Avalonia 패키지 버전을 일치시킵니다. Android의
  기존 SkiaSharp 3.x 명시 참조는 네이티브 라이브러리 요구 사항에 맞게 유지하고
  3.119.4로 갱신합니다. 다른 플랫폼은 Avalonia가 지원하는 전이 그래픽 의존성을 유지합니다.
- CommunityToolkit.Mvvm, Harmony, Android SplashScreen은 패치 버전으로 갱신하며,
  미리 보기 패키지나 무관한 주요 버전 변경은 도입하지 않습니다.

공식 자료: [.NET 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[Avalonia 11.3.22](https://github.com/AvaloniaUI/Avalonia/releases/tag/11.3.22),
[SkiaSharp 3.119.4](https://github.com/mono/SkiaSharp/releases/tag/v3.119.4).

## 공통 회귀 검사

```sh
dotnet run --project avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Tests -c Release
```

검사기는 메모리 안의 HTTP 핸들러를 사용하며 실제 백엔드에 접속하지 않습니다.
데스크톱·WebAssembly·모바일 워크로드는 필요하지 않습니다. 검사 범위는 테스트
프로젝트의 README를 참고하세요.

## Windows와 Linux 데스크톱

```sh
dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Desktop -c Release -r win-x64 --self-contained true
dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Desktop -c Release -r linux-x64 --self-contained true
```

Linux에서도 다른 대상 플랫폼으로 게시할 수 있습니다. 게시 성공은 실제 UI 실행 검사가
아닙니다. Windows 및 그래픽 세션과 Avalonia 네이티브 의존성이 준비된 Linux에서 각각
실행해 확인하세요. 저장소의 Windows 매니페스트는 관리자 권한을 요청하지 않고 현재
사용자 권한으로 실행합니다.

## 브라우저

```sh
dotnet workload install wasm-tools
dotnet build avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser -c Release
```

프로젝트는 네이티브 WebAssembly 링크를 사용합니다. 실제 번들 검증에서도 이 설정을
유지하세요. .NET 10 WebAssembly 빌드는 별도 프로세스의 MSBuild 작업을 사용하므로
빌드 장비에서 로컬 프로세스 간 통신이 가능해야 합니다. Unix 도메인 소켓을 거부하는
제한 환경에서는 C# 프로젝트를 컴파일해도 최종 브라우저 번들을 검증할 수 없습니다.
`-t:Compile`이나 `WasmBuildNative=false`를 전체 빌드·브라우저 동작 검사의 대체로
취급하지 마세요.

## Android

```sh
dotnet workload install android
dotnet build avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Android -c Release
```

선택한 워크로드가 지원하는 완전한 JDK와 Android SDK를 설치합니다. 현재 프로젝트는
ARM64 APK를 만들며 최소 지원 플랫폼은 Android API 21입니다. Java 런타임만으로는
부족하며 JDK에 `jar`, `javac` 등의 도구가 있어야 합니다. 자동으로 감지되지 않으면
`AndroidSdkDirectory`, `JavaSdkDirectory`에 절대 경로를 MSBuild 속성으로 전달하세요.

Microsoft의 [의존성 설치 안내](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies)에
`InstallAndroidDependencies` 대상이 설명되어 있습니다. 해당 대상에 SDK 라이선스
수락을 요청하기 전에 [Android SDK 약관](https://developer.android.com/studio/terms)을
검토하고 동의해야 합니다. 다운로드 실패 시 TLS 인증서 검증을 우회하지 마세요.
APK 빌드는 네이티브 라이브러리의 페이지 크기 호환성을 포함한 실제 Android 기기나
에뮬레이터 설치·실행 검사를 대신하지 않습니다.

## iOS

기존 iOS 프로젝트는 공통 프로젝트와 같은 .NET/Avalonia 버전을 유지합니다.
빌드·서명·실행 검증에는 지원되는 Mac/Xcode 환경과 해당 iOS 워크로드가 필요합니다.
Linux 검사는 이 대상을 검증하지 않습니다.
