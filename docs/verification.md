# Verification — 29 October 2026

## Passed

- Clean Release solution build: zero warnings and errors.
- Gameplay checks: rupee values and rarity ordering, scoring/reset, pause/resume/retry, focus-loss state transitions, motion, bounds and leaderboard persistence.
- Regression: malformed individual save entries no longer crash loading.
- Regression: score updates can be deferred; explicit flush persists them. A failed write preserves in-memory best.
- Currency boundaries: thousands, lakhs, crores, and very large legacy scores. Display truncates rather than overstating earned money.
- 100 seeded five-minute director simulations: bounded speed, opening grace and minimum bomb spacing. See difficulty-report.csv.
- Eight native renderer states exported and reviewed, including the new title and game-over text.
- Runtime asset dimensions and clean Release output verified. Ten runtime PNGs; no obsolete meter or old character PNGs in that output.
- Historical artwork backup integrity verified.

## Changes to challenge

The warm-up is now 4.5 seconds. The ramp approaches its ceiling over about two minutes. Late play averages roughly 14–15 bombs per 30 seconds (formerly 8–9), with 1.1 seconds minimum spawn separation. Bomb speed caps at 94 pixels/second; notes cap at 51. Three-second easing passages remain in each 20-second phrase. No new mechanics were added.

## Manual playtest limitation

Windows Computer Use reported `FrameArrived timed out` / `window capture timed out`. The optional live-review renderer exported real screenshots and telemetry, and an earlier user-controlled run was observed collecting notes, but automated keyboard actions did not reliably start the renamed build. A complete hands-on playtest is **not verified**. The simulation measures pacing constraints; it cannot establish that the game feels fun. Final feel testing should cover dodging at 30, 60 and 120 seconds, movement reversal, pause/resume, mute buttons, and retry.

## Cleanup

Source is grouped by responsibility; note values and rarity have one catalog. Historical experiments and generated outputs were moved outside the workspace to the sibling BillusBucket-Backups folder. Current builds use only the current project and character names. Personal bests were copied byte-for-byte to the BillusBucket application-data folder before updating the runtime path.
