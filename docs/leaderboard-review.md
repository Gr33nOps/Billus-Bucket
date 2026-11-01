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

## Follow-up: identities and connection states

- The v1 backend hashed raw player IDs; v2 normalized them. Requests carrying the original ID now reconcile those proven matching profiles atomically, keeping the higher score per week and the all-time best, preserving run deduplication and Save Code aliases. Anonymous reads never merge profiles by name.
- Separate profiles sharing a name receive distinct public `#` labels. Labels are display disambiguators, not account credentials. Their numbers may change as the weekly player list changes. Older anonymous `BILLU_` tags display as `GUEST_` tags.
- Cached scores now show connection status and a tap-to-refresh/retry affordance. Ten rows leave room for status messages. Offline, rate-limit, server failure, malformed response, and timeout messages are distinguished.
- Empty authoritative responses clear old rows and rank. Transport exceptions end loading. Old submissions cannot replace a restored profile; name writes are serialized and stale restore responses ignored.
- Offline name changes are marked pending in local storage and retried on refresh/reload. Cloud renames synchronize back to other devices. Server validation failures do not replace the local name.
- A Save Code already assigned to a different profile returns an explicit error. Restoring a different profile replaces its local best rather than importing the previous player's best.

The browser UI and Worker changes must both be deployed. Existing duplicate profiles caused by the old hash scheme can be reconciled when their owner next visits with the original stored ID; unrelated same-name profiles remain separate.
