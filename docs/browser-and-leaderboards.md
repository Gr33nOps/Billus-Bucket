# Browser Build, Platform SDKs, and Multi-Platform Leaderboards

## 1. MonoGame & Browser Architecture

- **Desktop target (`BillusBucket/BillusBucket.csproj`)**: Uses `MonoGame.Framework.DesktopGL` (`3.8.*`) on `net8.0`. DesktopGL depends on native SDL2 and OpenAL binaries, so it cannot compile directly to `browser-wasm`.
- **Browser WebAssembly target (`BillusBucket.Web/BillusBucket.Web.csproj`)**: Uses **KNI BlazorGL** (`nkast.Kni.Platform.Blazor.GL` `4.3.9001` on `Microsoft.NET.Sdk.BlazorWebAssembly`), which implements the exact `Microsoft.Xna.Framework.*` APIs on WebGL and WebAudio.
- **Single-source-of-truth gameplay & assets**:
  - `BillusBucket.Web` compiles the exact same C# gameplay, physics, animation, audio, UI, and leaderboard source files from `BillusBucket/` without duplicating code.
  - All 10 PNG sprites and 8 WAV sounds in `BillusBucket/Assets/` are synced automatically into `wwwroot/Assets/` at build time and preloaded asynchronously into `InMemoryAssetStreamProvider` before `BillusBucketGame` starts.
  - The native `320×180` pixel-art `RenderTarget2D` is scaled to the largest centered `16:9` frame inside any browser or mobile viewport using `SamplerState.PointClamp` (nearest-neighbor filtering), preserving crisp square pixels and mapping mouse/touch coordinates 1:1.

---

## 2. Leaderboard Architecture & Provider Selection

Core gameplay depends only on `ILeaderboardService` and `IGamePlatformBridge`. `LeaderboardServiceFactory.Create(...)` selects the active adapter from `PlatformRuntimeConfig` (populated on web from `wwwroot/platform-config.js`):

| `activePlatform` | Active `ILeaderboardService` | Behavior |
|---|---|---|
| `"local"` / `"desktop"` | `LocalLeaderboardService` | Saves Personal Best locally (`leaderboard.json` on desktop, safe `localStorage` in browser with session-memory fallback). Never displays fake global rankings. |
| `"web"` (with `sharedLeaderboardEnabled: true`) | `SharedOnlineLeaderboardService` | Saves Personal Best locally + submits completed runs once per `runId` to the Supabase Edge Function (`/functions/v1/leaderboard`) and displays Weekly Top 10 + player weekly rank. Degrades gracefully to local mode when offline. |
| `"crazygames"` | `CrazyGamesLeaderboardService` | Always tracks Personal Best locally. Submits AES-GCM encrypted scores via `window.CrazyGames.SDK.user.submitScore` **only** when `crazyGamesLeaderboardEnabled: true` and an encryption key is configured. Optionally pairs with the shared Top-10 backend if enabled. |
| `"poki"` | `PokiLeaderboardService` | **Zero external network requests by default** (`pokiApprovedExternalLeaderboardEnabled: false`) to comply with Poki's strict Content-Security-Policy (CSP). Enables the shared online backend **only** when `pokiApprovedExternalLeaderboardEnabled: true` after Poki approves your domain and Privacy Policy. |

### Bomb-Hit Scoring & Single-Submission Guarantee
1. When a bomb hits Billu's bucket, `BillusBucketRun.HitBomb()` resets active bucket cash (`run.Cash`) to `0`, updates `BestCash`, and returns the exact **pre-explosion cash** earned during that run (`lastRunCash`).
2. `StartRun()` mints a fresh `currentRunId` (`Guid`).
3. `HandleBombHit()` submits `lastRunCash` with `currentRunId` at most once (`hasSubmittedCurrentRun` guard in `BillusBucketGame`, backed by a `HashSet<string>` `runId` guard in every `ILeaderboardService` implementation and a `run_id_hash` primary key in Supabase).

---

## 3. Shared Online Leaderboard Evaluation (Supabase)

### Evaluation
A small **Supabase (Postgres + Edge Function)** backend is well suited for a lightweight arcade game across approved web portals:
- **Why an Edge Function instead of direct client table writes**: Direct PostgREST table inserts from a browser expose table write permissions to anyone holding the public `anon` key. Routing all requests through `/functions/v1/leaderboard` (`backend/supabase/functions/leaderboard/index.ts`) keeps `SUPABASE_SERVICE_ROLE_KEY` and `PLAYER_HASH_SALT` strictly server-side while Row-Level Security (RLS) blocks direct table access.
- **Zero accounts or PII**: The client generates a random UUID stored locally and derives a deterministic non-personal tag (`BILLU_1000`..`BILLU_9999`). The server hashes the UUID with a secret salt (`SHA-256`) so player identifiers cannot be enumerated or correlated across games, and rejects unmoderated free-text names.
- **Weekly reset alignment**: `current_leaderboard_week_start()` in `backend/supabase/schema.sql` resets every **Monday at 09:00 UTC**, matching CrazyGames' native weekly leaderboard schedule.

### Server-Side Protections & Remaining Client-Side Risks
`backend/supabase/functions/leaderboard/index.ts` enforces:
1. Positive integer check, upper score cap (`50,000,000`), and **multiple-of-10 check** (`score % 10 === 0`, matching `NoteCatalog` denominations `10, 20, 50, 100, 500, 1000, 5000`).
2. Plausible rupee-per-second ceiling against `durationSeconds` (`score <= max(15000, ceil(durationSeconds * 15000))`).
3. Duplicate-run replay protection via `leaderboard_submitted_runs (run_id_hash)`.
4. Sliding-minute rate limiting per IP hash and per player hash (`6` writes/min, `30` reads/min).

**Remaining risks inherent to client-authoritative HTML5 games**:
- Because drop physics run client-side in WebAssembly (with random note rolls and player input), a determined attacker inspecting browser memory or forging HTTP requests can still submit a fabricated score that satisfies `score % 10 === 0` and waits out a plausible `durationSeconds` within the rate limit.
- **Mitigation if high-stakes competition is ever added**: Record deterministic seed + timestamped input frames per run and replay `DropDirector` + `DropMotion` + `BucketControl` inside a server verifier before accepting Top-10 scores. For a casual arcade leaderboard with weekly resets and zero monetary prizes, rate-limited plausibility bounds keep maintenance simple while deterring casual script tampering.

---

## 4. Platform-Specific Setup & Remaining Submission Steps

### Building Portal Bundles
Run PowerShell from the repository root:

```powershell
# Standalone / Local test bundle (zero external SDK tags)
pwsh ./tools/publish_web.ps1 -Platform local -CreateZip

# CrazyGames bundle (injects crazygames-sdk-v3.js ONLY)
pwsh ./tools/publish_web.ps1 -Platform crazygames -CreateZip

# Poki bundle (injects poki-sdk.js ONLY; zero external leaderboard calls)
pwsh ./tools/publish_web.ps1 -Platform poki -CreateZip
```

Output folders and upload-ready `.zip` archives are written to `dist/`.

### CrazyGames Pre-Submission Checklist
1. Upload `dist/billus-bucket-crazygames.zip` to the CrazyGames Developer Portal and test in their QA tool (`SDK.init`, `game.loadingStart`/`loadingStop`, `game.gameplayStart`/`gameplayStop` are already wired).
2. **Native Weekly Leaderboard (invite-only)**:
   - CrazyGames grants native leaderboards by invitation (max 1 weekly leaderboard per game).
   - Until invited, keep `crazyGamesLeaderboardEnabled: false` in `wwwroot/platform-config.js`; the game automatically uses local Personal Best without claiming native leaderboard access.
   - Once invited, copy the base64 AES-GCM encryption key from the CrazyGames Developer Portal into `crazyGamesEncryptionKeyBase64`, set `crazyGamesLeaderboardEnabled: true`, and re-run `pwsh ./tools/publish_web.ps1 -Platform crazygames -CreateZip`. (Never place a server-side REST `X-API-Key` in client files.)

### Poki Pre-Submission Checklist
1. Keep `pokiApprovedExternalLeaderboardEnabled: false` and `sharedLeaderboardEnabled: false` for your initial Poki upload (`dist/billus-bucket-poki.zip`). In this default configuration, the game makes no external leaderboard requests. The official Poki SDK still loads externally. Portal Inspector testing and approval remain required.
2. Verify in Poki's Inspector that `gameLoadingFinished`, `gameplayStart`, and `gameplayStop` fire cleanly without consecutive duplicates (enforced by `PlatformLifecycleGuard`).
3. **Optional External Weekly Leaderboard on Poki (requires approval first)**:
   - Deploy `backend/supabase/schema.sql` and `backend/supabase/functions/leaderboard/index.ts` to your Supabase project.
   - In *Poki for Developers → Settings → CSP*, request approval for `https://<your-project-ref>.supabase.co` and submit your Privacy Policy explaining anonymous score storage.
   - **Only after Poki approves the CSP domain**, set `pokiApprovedExternalLeaderboardEnabled: true`, `sharedLeaderboardEnabled: true`, `sharedLeaderboardEndpointUrl`, and `sharedLeaderboardPublicKey` (the public `anon` key, never `service_role`) in `platform-config.js`.

---

## 5. Local Testing Guide

### A. Automated Unit, Policy & Difficulty Tests
```powershell
dotnet run --project GameTests/GameTests.csproj -c Release
```
Verifies gameplay scoring, pre-explosion bomb score capture, once-per-run submission deduplication, restricted storage fallback, CrazyGames uninvited vs. invited rules, Poki zero-network default, `PlatformLifecycleGuard` event ordering, and 100 seeded 5-minute difficulty runs.

### B. Desktop Build
```powershell
dotnet run --project BillusBucket/BillusBucket.csproj
```

### C. Browser Build (Desktop, Mobile Touch & Restricted Storage)
```powershell
dotnet run --project BillusBucket.Web/BillusBucket.Web.csproj
```
Open the local URL printed by `dotnet run` (or serve `dist/local` with any static HTTP server):
1. **Desktop controls**: Test `Left`/`Right` or `A`/`D`, mouse click-and-drag, `P`/`Esc` pause, `M` mute, `L` or `LEADERBOARD` button on Ready and Game-Over screens, and `PLAY AGAIN` immediate restart after bomb explosion.
2. **Mobile touch & responsive 16:9**: Open Chrome DevTools (`F12` → Device Toolbar `Ctrl+Shift+M`), switch between mobile landscape (`844×390`) and portrait (`390×844`), verify crisp nearest-neighbor pixel scaling and touch-drag bucket movement.
3. **Restricted / blocked browser storage**: Append `?restrictedStorage=1` on `localhost` (e.g. `http://localhost:5000/?restrictedStorage=1`). Verify the game boots, plays, tracks Personal Best for the active session, and displays `"SESSION ONLY - STORAGE RESTRICTED"` on the Leaderboard screen without console errors.

## Audit status (2026-10-30)
- GitHub Actions builds succeed, but Pages was never enabled. GitHub initially rejected enabling it on the private repository. With the owner's approval, Billus-Bucket is now public and Pages is enabled using Actions. A green build previously skipped deployment silently; deployment now fails explicitly when unavailable.
- Local static-host browser boot, gameplay, pause, mobile portrait, blocked storage and missing-asset recovery were checked. The live Pages URL is https://gr33nops.github.io/Billus-Bucket/.
- Shared rankings remain disabled until a Supabase project is configured and schema/functions are deployed. Set a unique PLAYER_HASH_SALT of at least 32 characters. Reapply schema.sql before deploying the updated atomic submission function.
- Edge Function tests mock the client; a separate PGlite/PostgreSQL test executes the actual schema and verifies rollback, replay rejection, ranking, and RPC permissions. Production Supabase and portal-hosted submissions still require deployment testing.
- CrazyGames and Poki bundles are preparation, not proof of submission or acceptance. No portal submission is verified. Commercial/rewarded ad flows are not implemented; evaluate them with the portal requirements before release.
- Official references: https://docs.crazygames.com/sdk/leaderboards-client/ and https://developers.poki.com/guide/sdk-html5.
