# Leaderboard and web review — 2026-11-02

## Fixes

- Ignore leaderboard refreshes superseded by a name change, profile restore, or newer refresh. A regression test reproduced an old request replacing the restored profile's rank and best.
- Wait for clipboard completion before reporting a copied Save Code; clipboard permission denial returns the manual-code fallback.
- Apply tracked D1 migrations before Worker deployment. Creating tables with `IF NOT EXISTS` alone did not upgrade existing name/platform constraints.
- Remove the unconditional cloud-save claim from the name editor and document the active backend, profile credentials, offline behavior, and optional Supabase limitations.

- Use a project-relative publish directory so the renamed local folder (`Billu's bucket`) does not break MSBuild publish transforms. Reproduced the failure with the original absolute path and verified the relative path succeeds.

## Verification

- Release solution build with warnings as errors: zero warnings/errors.
- Gameplay, platform, profile save/restore, stale-request regression, and 100 seeded five-minute difficulty checks pass.
- Node checks cover actual SQLite D1 requests, custom-name validation, Save Code aliases, migration preservation, duplicate submissions, rank ordering, atomic rollback, rate limiting, PostgreSQL permissions, mobile viewport sizing, and clipboard failure.
- Optional Supabase handler tests pass.
- Live Pages booted in Chromium without page exceptions or failed HTTP requests. Reviewed the name keypad and ranking layout; no clipped text or mismatched assets were seen in those screens.
- Live Worker GET returned ranked entries with the expected CORS origin. Write behavior was exercised against the isolated test database, not real player rankings.

## Limits

Save Codes are bearer credentials, not account authentication. Weekly scores are client-submitted with validation and rate limits, not server-replayed gameplay. Offline submissions are not queued for later delivery. Portal approval/submission status cannot be established from the repository alone. Physical phone/browser combinations still need device testing.
