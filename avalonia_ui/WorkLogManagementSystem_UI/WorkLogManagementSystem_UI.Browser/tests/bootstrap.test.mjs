import assert from 'node:assert/strict';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';

// Execute the unchanged bootstrap with an isolated fake runtime. No browser,
// network, application credentials, local storage, or real WASM are accessed.
for (const href of [
    'https://qa.invalid/',
    'https://qa.invalid:8443/nested/path?test=1#view',
    'http://localhost:8000/',
    'http://127.0.0.1:9090/screen?test=2',
]) {
    test(`bootstrap defaults API to ${new URL(href).origin}`, async () => {
        const directory = await mkdtemp(join(tmpdir(), 'worklog-bootstrap-'));
        const previous = {
            window: globalThis.window,
            location: globalThis.location,
            capture: globalThis.worklogBootstrapCapture,
        };
        try {
            await mkdir(join(directory, '_framework'));
            await writeFile(join(directory, 'package.json'), '{"type":"module"}');
            await writeFile(join(directory, 'main.js'), await readFile(new URL('../wwwroot/main.js', import.meta.url)));
            await writeFile(join(directory, '_framework', 'dotnet.js'), `
                const capture = globalThis.worklogBootstrapCapture;
                export const dotnet = {
                    withDiagnosticTracing(value) { capture.tracing = value; return this; },
                    withApplicationArgumentsFromQuery() { capture.queryArguments = true; return this; },
                    withEnvironmentVariables(value) { capture.environment = value; return this; },
                    async create() {
                        capture.created = true;
                        return {
                            getConfig: () => ({ mainAssemblyName: 'synthetic-ui' }),
                            runMain: async (assembly, args) => { capture.main = { assembly, args }; },
                        };
                    },
                };
            `);
            globalThis.window = {};
            globalThis.location = new URL(href);
            const capture = globalThis.worklogBootstrapCapture = {};
            await import(pathToFileURL(join(directory, 'main.js')).href);
            assert.deepEqual(capture.environment, { WORKLOG_API_BASE_URL: new URL(href).origin });
            assert.equal(capture.created, true);
            assert.equal(capture.tracing, false);
            assert.equal(capture.queryArguments, true);
            assert.deepEqual(capture.main, { assembly: 'synthetic-ui', args: [href] });
        } finally {
            for (const [key, value] of Object.entries({
                window: previous.window,
                location: previous.location,
                worklogBootstrapCapture: previous.capture,
            })) {
                if (value === undefined) delete globalThis[key];
                else globalThis[key] = value;
            }
            await rm(directory, { recursive: true, force: true });
        }
    });
}
