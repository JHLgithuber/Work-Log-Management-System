import { dotnet } from './_framework/dotnet.js'

const is_browser = typeof window != "undefined";
if (!is_browser) throw new Error(`Expected to be running in a browser`);

const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    // A bundled HTTPS page must not default to the visitor's localhost API.
    // This supplies a default only; explicit application settings still win.
    .withEnvironmentVariables({ WORKLOG_API_BASE_URL: globalThis.location.origin })
    .create();

const config = dotnetRuntime.getConfig();

await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.href]);
