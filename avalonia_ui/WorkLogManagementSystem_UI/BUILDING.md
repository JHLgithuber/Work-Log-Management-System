# Building and testing the frontend

Run these commands from the repository root. Install the stable .NET SDK selected by
`global.json` (10.0.402, with later 10.0.4xx patches allowed). All frontend projects
target .NET 10. The shared project remains platform-neutral; platform-specific
credential storage and entry points remain in the individual platform projects.

## Version policy

- .NET 10 is the current LTS release. .NET 9 support ends on November 10, 2026;
  .NET 10 support ends on November 14, 2028.
- Avalonia is updated within the existing 11.3 line to 11.3.22. Moving to Avalonia 12
  is a separate migration, not required for these reliability fixes.
- `Directory.Packages.props` keeps the Avalonia packages synchronized. Android's
  existing explicit SkiaSharp 3.x references are retained and patched to 3.119.4
  for its native-library requirements. Other platforms retain Avalonia's supported
  transitive graphics dependencies.
- CommunityToolkit.Mvvm, Harmony, and Android SplashScreen receive patch updates,
  without switching to preview packages or introducing unrelated major upgrades.

Sources: [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core),
[Avalonia 11.3.22](https://github.com/AvaloniaUI/Avalonia/releases/tag/11.3.22), and
[SkiaSharp 3.119.4](https://github.com/mono/SkiaSharp/releases/tag/v3.119.4).

## Shared regression checks

```sh
dotnet run --project avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Tests -c Release
```

The runner uses an in-memory HTTP handler, not a live backend. It does not require
desktop, WebAssembly, or mobile workloads. See the test project's README for coverage.

## Windows and Linux desktop

```sh
dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Desktop -c Release -r win-x64 --self-contained true
dotnet publish avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Desktop -c Release -r linux-x64 --self-contained true
```

These commands can cross-publish from Linux. Publication is not a runtime UI test:
launch the corresponding output on Windows and on Linux with a graphical session
and Avalonia's native dependencies installed. The checked-in Windows manifest runs
as the current user, without requesting administrator privileges.

## Browser

```sh
dotnet workload install wasm-tools
dotnet build avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser -c Release
```

The project enables native WebAssembly linking. Keep that enabled for the real
bundle validation. The .NET 10 WebAssembly build uses out-of-process MSBuild tasks,
so the build host must permit local interprocess communication. A restricted host
that rejects Unix-domain sockets can compile the C# project but cannot validate
the final browser bundle. Do not treat `-t:Compile` or `WasmBuildNative=false` as a
substitute for the full build and browser interaction tests.

## Android

```sh
dotnet workload install android
dotnet build avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Android -c Release
```

Install a complete supported JDK and the Android SDK for the selected workload.
The current project produces an ARM64 APK and retains Android API 21 as its
minimum supported platform. A Java runtime alone is insufficient: the JDK must
include tools such as `jar` and `javac`. Supply `AndroidSdkDirectory` and
`JavaSdkDirectory` as absolute MSBuild property paths if they are not auto-detected.

Microsoft's [dependency setup instructions](https://learn.microsoft.com/en-us/dotnet/android/getting-started/installation/dependencies)
describe the `InstallAndroidDependencies` target. Review and accept the
[Android SDK terms](https://developer.android.com/studio/terms) before asking that
target to accept the SDK licenses. Do not bypass TLS certificate validation if
downloads fail. Building an APK does not replace installation and testing on an
Android device or emulator, including native-library page-size compatibility.

## iOS

The existing iOS project is kept on the same .NET/Avalonia versions as the shared
project. Building, signing, and runtime validation require a supported Mac/Xcode
environment and the matching iOS workload; Linux checks do not validate this target.
