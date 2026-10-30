-- ============================================================================
-- Billu's Bucket — Shared Weekly Leaderboard Schema (Cloudflare D1 / SQLite)
-- ============================================================================
-- Security model:
-- 1. Cloudflare D1 has NO public client endpoint; only the bound Worker
--    (`env.DB`) can query or mutate these tables.
-- 2. Client builds never contain any database secret, API key, or hash salt.
-- 3. Weekly boards reset automatically every Monday at 09:00 UTC
--    (aligned with CrazyGames' Monday 09:00 UTC weekly leaderboard reset).
-- ============================================================================

-- 1. Weekly best score per anonymous player
CREATE TABLE IF NOT EXISTS weekly_leaderboard_scores (
    week_start      TEXT    NOT NULL,
    player_hash     TEXT    NOT NULL CHECK (length(player_hash) = 64),
    display_name    TEXT    NOT NULL CHECK (length(display_name) BETWEEN 3 AND 12 AND display_name NOT GLOB '*[^A-Z0-9_]*'),
    best_score      INTEGER NOT NULL CHECK (best_score > 0 AND best_score <= 50000000 AND best_score % 10 = 0),
    submitted_at    TEXT    NOT NULL,
    source_platform TEXT    NOT NULL CHECK (source_platform IN ('local', 'web', 'crazygames', 'poki', 'desktop')),
    PRIMARY KEY (week_start, player_hash)
) STRICT;

CREATE INDEX IF NOT EXISTS idx_weekly_scores_ranking
    ON weekly_leaderboard_scores (week_start, best_score DESC, submitted_at ASC, player_hash ASC);

-- 2. Completed run IDs to prevent duplicate / replay submissions of the same run
CREATE TABLE IF NOT EXISTS leaderboard_submitted_runs (
    run_id_hash TEXT    PRIMARY KEY CHECK (length(run_id_hash) = 64),
    player_hash TEXT    NOT NULL CHECK (length(player_hash) = 64),
    score       INTEGER NOT NULL CHECK (score > 0 AND score <= 50000000 AND score % 10 = 0),
    created_at  TEXT    NOT NULL
) STRICT;

CREATE INDEX IF NOT EXISTS idx_submitted_runs_created_at
    ON leaderboard_submitted_runs (created_at);

-- 3. Per-minute rate-limit counters (keyed by salted IP hash and player hash)
CREATE TABLE IF NOT EXISTS leaderboard_rate_limits (
    bucket_key    TEXT    NOT NULL,
    window_minute TEXT    NOT NULL,
    hit_count     INTEGER NOT NULL DEFAULT 1 CHECK (hit_count >= 1),
    PRIMARY KEY (bucket_key, window_minute)
) STRICT;

CREATE INDEX IF NOT EXISTS idx_rate_limits_window
    ON leaderboard_rate_limits (window_minute);
