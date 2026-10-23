# Frontend regression checks

Requires the .NET 10 SDK selected by the repository's `global.json`. From the repository root:

```sh
dotnet run --project avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Tests -c Release
```

This dependency-free executable returns a nonzero exit code when a check fails. It builds the real shared Avalonia project, then exercises its generated commands against an in-memory HTTP handler. It does not start the application, load saved credentials/settings, or contact a backend. All synthetic dates are in 2099.

Coverage:

- Saving while navigating preserves the original task-entry date and text, without replacing the newer editor.
- A retry after task creation succeeds but entry saving fails updates the original task instead of creating a duplicate.
- Failed daily-note reads block writes until a successful refresh has loaded the existing note.
- Logout invalidates in-flight task and daily-note reads.
- Refresh, date navigation, and task reselection cannot race pending task/note saves; failed writes retain drafts.
- Clearing and reselecting a date recovers both loaders.
- Displaying and resaving task dates preserves Korean calendar days regardless of the device timezone, including midnight and unbounded-date sentinels.

On Linux, rerun with `TZ=UTC`, `TZ=Asia/Seoul`, `TZ=America/Los_Angeles`, and `TZ=Australia/Sydney` to cover device timezone differences.

The runner does not replace browser/device interaction tests. A browser build can be checked separately:

```sh
dotnet build avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser -c Release
```

Native WebAssembly compilation may require the matching `wasm-tools` workload. Android and iOS require their platform workloads and are not covered by this runner.
