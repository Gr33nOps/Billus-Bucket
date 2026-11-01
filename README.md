# Billu's Bucket

[Play in your browser](https://gr33nops.github.io/Billus-Bucket/)

A crisp `320×180` pixel-art rooftop arcade game built with C# and MonoGame (DesktopGL + Browser WebAssembly via KNI BlazorGL).

Guide Billu across the rooftop to catch floating rupee notes (`Rs 10` to `Rs 5,000`) in his bucket while dodging falling bombs.

## Controls

- **Move**: `Left` / `Right`, `A` / `D`, mouse drag, or mobile touch drag
- **Start / Resume / Retry**: `Enter`, `Space`, or click/tap the button
- **Leaderboard**: Click/tap the top-right trophy icon button (below the speaker button) or press `L` on the Ready or Game-Over screen
- **Pause / Resume**: `P`, `Esc`, or the top-right pause/play icon above the music button
- **Mobile**: Turn the phone sideways to play. Portrait orientation shows a rotate prompt and pauses an active run.
- **Mute All Audio**: `M` (or click/tap the top-right music and sound buttons)

## Project Structure

- `BillusBucket/` — Core C# gameplay, presentation, audio, platform abstractions, modular leaderboards, and DesktopGL target (`net8.0`)
- `BillusBucket.Web/` — HTML5 / WebAssembly browser target using KNI BlazorGL (`net9.0`) + JS bridge for CrazyGames SDK v3, Poki SDK v2, and safe `localStorage`
- `GameTests/` — Automated unit tests for gameplay physics, scoring, leaderboard adapters, platform policies, and 100-seed 5-minute difficulty checks
- `backend/cloudflare/` � Active Workers + D1 weekly leaderboard, custom names, Save Code profiles, schema and migrations
- `tools/deploy_cloudflare_leaderboard.ps1` � Cloudflare provisioning and migration/deployment helper
- `backend/supabase/` — Optional shared weekly Top-10 leaderboard Postgres schema (`schema.sql`) and Edge Function (`functions/leaderboard/index.ts`)
- `tools/publish_web.ps1` — Builds portal-specific static WebAssembly bundles (`local`, `web`, `crazygames`, `poki`)
- `docs/browser-and-leaderboards.md` — Full guide for platform configuration, optional Supabase setup, security risks, and CrazyGames/Poki submission checklists

## Quick Commands

```powershell
# Build entire solution (Desktop + Browser WebAssembly + Tests)
dotnet build BillusBucket.sln -c Release -warnaserror

# Run all gameplay, leaderboard, platform SDK, and difficulty checks
dotnet run --project GameTests/GameTests.csproj -c Release

# Play Desktop build
dotnet run --project BillusBucket/BillusBucket.csproj

# Run Browser WebAssembly dev server
dotnet run --project BillusBucket.Web/BillusBucket.Web.csproj

# Publish portal-specific static browser bundles into dist/
pwsh ./tools/publish_web.ps1 -Platform local -CreateZip
pwsh ./tools/publish_web.ps1 -Platform crazygames -CreateZip
pwsh ./tools/publish_web.ps1 -Platform poki -CreateZip
```

## Online profiles

The web build uses Cloudflare Workers + D1. Players can set a name in the leaderboard and use their Save Code on another device to restore their name and personal best. Keep this code private: it grants access to that profile. Offline scores remain on the current device; they are not a durable submission queue.

See [Cloudflare setup](backend/cloudflare/README.md) for the active backend. Supabase is an alternative backend, not a required second service. Google Play Games and Apple Game Center sign-in are not implemented.
