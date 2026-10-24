# Android single-view lifecycle regression

Use a disposable API 35 emulator and isolated backend with a synthetic account.
Do not use a physical device or production account. This app has one Avalonia
MainView per process; MainActivity must use `singleTask` so a second launcher
intent does not attach that view to a second live host.

## Reproduction and checks

1. Install the Debug x86_64 APK, log in, add a task draft, and type a distinctive
   title without saving. Record the app PID and current Activity token.
2. Send MAIN/LAUNCHER intents to the resolved MainActivity three times with
   `adb -s <emulator> shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -f 0x18000000 -n <component>`.
   Each request must reuse the running Activity; the title and login state must
   remain visible. The old `standard` launch mode can crash with "already has a
   visual parent". Merely moving the view between roots is unsafe when layout
   work is queued, and is not the implemented fix.
3. Rotate portrait/landscape twice. Check the draft, scrolling and save control.
4. On this disposable emulator only, change font scale from 1.0 to 1.15 and
   back, restoring the original value afterward. Verify app-PID
   `wm_on_destroy_called` / `wm_on_create_called` pairs for MainActivity, the same
   process, and draft/login preservation. Changing a setting without observing
   OnCreate is not evidence of Activity recreation.
5. Save the synthetic draft once. Confirm one create request, with no duplicate
   event-driven save. Go Home and return three times; also test Android Back.
6. With no unsaved input, restart the app process twice. Remembered credentials
   may be restored but Login is still required. Unsaved process-death recovery
   is not implemented or claimed by this change.
7. Stop only the isolated backend; check bounded connection-error feedback.
   Restart it and retry login. Distinguish this expected outage from unexplained
   `Socket closed` errors; do not add unconditional retries to hide them.

Review app-scoped fatal/ANR logs and live Activity count after repetition. A
stable count and no new event subscriptions are useful checks, but are not a
managed/native heap leak proof. SingleTask changes repeated-launch/back-stack
behavior intentionally; multiple independent app windows are not supported.
Keep result counts, OS/API, source SHA and any untested paths with the QA report.
