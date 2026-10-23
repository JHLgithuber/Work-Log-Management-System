# Browser bootstrap regression checks

Run with Node.js from the repository root:

```sh
node --test avalonia_ui/WorkLogManagementSystem_UI/WorkLogManagementSystem_UI.Browser/tests/bootstrap.test.mjs
```

The tests execute the real `wwwroot/main.js` in a temporary module directory with
a fake .NET loader. They verify that HTTPS and HTTP origins, including explicit
ports, become the API default without path, query, or fragment contamination.
The existing runtime startup arguments remain unchanged. No network, real WASM,
credentials, or application settings are accessed.

Read-only code review confirms that explicit server selection and persisted
settings precedence are unchanged; these isolated bootstrap tests do not read
the user's settings or execute that persistence path.
`MainViewModel.LoadPersistedSettings` first sets defaults, then replaces them
with the saved `ApiBaseUrl` when valid saved settings exist. `OpenSettings` uses
the current selected `ApiBaseUrl`, and login/connection handlers use the user's
`ConnectionGateApiBaseUrl`, not the runtime default.

The .NET 10.0.13 browser loader supports `withEnvironmentVariables` and merges
these values into its existing environment dictionary; unrelated variables are
not removed. This is the loader supplied by workload set `10.0.402.1` in the
bundled Docker build. These isolated checks do not replace an actual browser
smoke test of the fully published bundle.
