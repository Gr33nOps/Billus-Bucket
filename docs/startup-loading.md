# Browser startup loading — 11 November 2026

The game runs in the browser from static files. The main host is now Cloudflare, with GitHub Pages retained as a mirror; see [hosting notes](game-hosting.md). The leaderboard is a separate service and was already initialized in the background. It does not need to finish connecting before play.

## Changes

- Publish with partial framework trimming. Game/engine code retains its existing callbacks and reflection support.
- Download engine scripts concurrently with `defer`, preserving their execution order.
- Wait for required graphics before starting the game. Fetch WAV files afterward, independently of the loading overlay.
- Let `GameAudio` notice each newly available sound once. Music and effects become usable without restarting; missing audio or hardware does not stop the game.
- Use one stable asset revision per build, rather than creating unique image URLs every visit.
- Reuse cached runtime files whose names contain content hashes. Revalidate the unversioned boot manifest so deployments still update correctly.

## Verification

The previous published bundle stayed on its loading overlay when all WAV downloads were held open. The fixed published bundle reached its ready screen while those same requests were held and leaderboard requests failed. After releasing the WAV requests, all eight sounds created browser audio buffers without a reload.

Published framework WASM file size fell from 25,097,491 bytes across 210 files to 7,741,268 bytes across 55 files, a reduction of about 69%. These are uncompressed file sizes, not a claim about every user's measured network transfer.

Also checked the Release build, gameplay/difficulty tests, browser cache regression tests, desktop and mobile controls, pause/resume, audio preferences, and leaderboard UI/name entry against the trimmed published bundle.

A first visit still needs the browser runtime and game graphics. Download time depends on the connection; the change removes optional audio and online-service dependencies from the startup path rather than promising instant first loads on every network.
