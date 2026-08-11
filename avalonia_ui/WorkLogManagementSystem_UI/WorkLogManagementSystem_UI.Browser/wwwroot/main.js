const isBrowser = typeof window !== 'undefined';
if (!isBrowser) throw new Error('Expected to be running in a browser');

function installClipboardFallback() {
    if (globalThis.navigator.clipboard !== undefined) {
        return;
    }

    // Avalonia 11 assumes navigator.clipboard always exists. Expose a small
    // paste/copy event bridge for insecure or unsupported browsers.
    const fallbackClipboard = {
        readText() {
            return new Promise((resolve, reject) => {
                const timeoutId = globalThis.setTimeout(() => {
                    globalThis.removeEventListener('paste', onPaste);
                    reject(new Error('Browser paste event was not received'));
                }, 1500);

                const onPaste = event => {
                    globalThis.clearTimeout(timeoutId);
                    event.preventDefault();
                    resolve(event.clipboardData?.getData('text/plain') ?? '');
                };

                globalThis.addEventListener('paste', onPaste, { once: true });
            });
        },
        writeText(text) {
            return new Promise((resolve, reject) => {
                let copyEventHandled = false;
                const onCopy = event => {
                    if (event.clipboardData === null) {
                        return;
                    }

                    event.clipboardData.setData('text/plain', String(text));
                    event.preventDefault();
                    copyEventHandled = true;
                };

                globalThis.document.addEventListener('copy', onCopy);
                try {
                    const copyCommandSucceeded = globalThis.document.execCommand('copy');
                    if (!copyCommandSucceeded || !copyEventHandled) {
                        reject(new Error('Browser clipboard access is unavailable'));
                        return;
                    }

                    resolve();
                } finally {
                    globalThis.document.removeEventListener('copy', onCopy);
                }
            });
        }
    };

    try {
        Object.defineProperty(globalThis.navigator, 'clipboard', {
            configurable: true,
            value: fallbackClipboard
        });
    } catch {
        // The managed TextBox guard still prevents a clipboard error from
        // terminating WASM if a browser refuses to expose this fallback.
    }
}

installClipboardFallback();

const { dotnet } = await import('./_framework/dotnet.js');
const dotnetRuntime = await dotnet
    .withDiagnosticTracing(false)
    .withApplicationArgumentsFromQuery()
    .create();

const config = dotnetRuntime.getConfig();
await dotnetRuntime.runMain(config.mainAssemblyName, [globalThis.location.origin]);
