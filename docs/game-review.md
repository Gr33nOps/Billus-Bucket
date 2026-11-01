# Game reliability review — 2026-11-02

## Fixes

- Runtime resources retry transient HTTP/network failures with bounded attempts and preserve integrity checks. Startup failures show a connection/reload message instead of leaving the loader indefinitely visible. Reload controls stay within the visible viewport.
- Required art downloads fail visibly; optional audio downloads can fail without preventing play. Individual missing/corrupt sounds no longer disable all other sounds. SFX remain available if the music file is absent.
- Catch detection sweeps both item and bucket movement through each frame. Contacts are resolved chronologically; exact simultaneous contacts favor the bomb. This prevents skipped catches and insertion-order-dependent scoring without changing hitbox sizes or difficulty tuning.
- Browser interruptions pause even if animation frames were suspended for the entire hidden period. Page hide, visibility loss, and window blur save pending personal bests synchronously. Returning does not automatically resume a run.
- Active sound effects are stopped on focus loss/external mute and when SFX are muted; completed voices are disposed. A browser audio-node probe reproduced the old sounds continuing after blur and verifies stop calls now occur.
- Music and SFX mute preferences persist independently in browser and desktop storage. Restricted storage falls back to session preferences.
- If localStorage accepts reads but rejects writes, newer session values remain authoritative rather than being overwritten with older persisted values.
- Pathologically large finite movement time steps cannot trap the physics subdivision loop.

## Verification

- Release solution build with warnings as errors, gameplay/profile checks, and 100 seeded five-minute difficulty checks pass.
- Regression tests cover resource retry/failure behavior, contact sweeps, audio preference persistence, page-hide suspension, hidden-frame interruption, storage failure, and bounded movement.
- Published browser bundle tested with normal startup, temporary runtime 503, permanently failed runtime, missing required art, and missing music. Expected error states show reload UI; healthy/recovered cases render successfully.
- Keyboard movement, pause/resume, blur/focus pause, persisted mute icons, three landscape viewport sizes (844×390, 667×320, 568×240), and portrait rotation prompt checked in Chromium.
- All 13 PNGs decode correctly. All eight WAVs decode and contain no saturated 16-bit samples. No visual asset changes were required in the inspected ready/playing/paused layouts.

These checks establish the tested behaviors; they cannot guarantee absence of every bug or replace testing on physical phones and other browser engines.
