-- ============================================================================
-- Billu's Bucket — Shared Weekly Leaderboard & Cloud Profile Schema (D1/SQLite)
-- ============================================================================
-- Security model:
-- 1. Cloudflare D1 has NO public client endpoint; only the bound Worker
--    (`env.DB`) can query or mutate these tables.
-- 2. Raw player tokens and Save/Connect Codes are NEVER stored in D1 — only
--    salted SHA-256 hashes (`player_hash`, `alias_hash`).
-- 3. Ready for Web, Desktop, Google Play Store ('android' + google_play_hash),
--    and Apple App Store ('ios' + apple_gamecenter_hash).
-- ============================================================================

-- 1. Persistent cloud player profile (custom name, all-time best, mobile store links)
CREATE TABLE IF NOT EXISTS player_profiles (
    player_hash           TEXT    PRIMARY KEY CHECK (length(player_hash) = 64),
    display_name          TEXT    NOT NULL CHECK (length(display_name) BETWEEN 2 AND 12 AND display_name NOT GLOB '*[^A-Z0-9_-]*'),
    all_time_best         INTEGER NOT NULL DEFAULT 0 CHECK (all_time_best >= 0 AND all_time_best <= 50000000 AND all_time_best % 10 = 0),
    google_play_hash      TEXT    UNIQUE,
    apple_gamecenter_hash TEXT    UNIQUE,
    source_platform       TEXT    NOT NULL CHECK (source_platform IN ('local', 'web', 'crazygames', 'poki', 'desktop', 'android', 'ios')),
    created_at            TEXT    NOT NULL,
    updated_at            TEXT    NOT NULL
) STRICT;

-- 2. Maps a short 12-char Save/Connect Code hash to an existing player_hash
CREATE TABLE IF NOT EXISTS player_profile_aliases (
    alias_hash            TEXT    PRIMARY KEY CHECK (length(alias_hash) = 64),
    canonical_player_hash TEXT    NOT NULL CHECK (length(canonical_player_hash) = 64),
    created_at            TEXT    NOT NULL
) STRICT;

CREATE INDEX IF NOT EXISTS idx_profile_aliases_canonical
    ON player_profile_aliases (canonical_player_hash);

-- 3. Weekly best score per player (resets every Monday at 09:00 UTC)
CREATE TABLE IF NOT EXISTS weekly_leaderboard_scores (
    week_start      TEXT    NOT NULL,
    player_hash     TEXT    NOT NULL CHECK (length(player_hash) = 64),
    display_name    TEXT    NOT NULL CHECK (length(display_name) BETWEEN 2 AND 12 AND display_name NOT GLOB '*[^A-Z0-9_-]*'),
    best_score      INTEGER NOT NULL CHECK (best_score > 0 AND best_score <= 50000000 AND best_score % 10 = 0),
    submitted_at    TEXT    NOT NULL,
    source_platform TEXT    NOT NULL CHECK (source_platform IN ('local', 'web', 'crazygames', 'poki', 'desktop', 'android', 'ios')),
    PRIMARY KEY (week_start, player_hash)
) STRICT;

CREATE INDEX IF NOT EXISTS idx_weekly_scores_ranking
    ON weekly_leaderboard_scores (week_start, best_score DESC, submitted_at ASC, player_hash ASC);

-- 4. Completed run IDs to prevent duplicate / replay submissions of the same run
CREATE TABLE IF NOT EXISTS leaderboard_submitted_runs (
    run_id_hash TEXT    PRIMARY KEY CHECK (length(run_id_hash) = 64),
    player_hash TEXT    NOT NULL CHECK (length(player_hash) = 64),
    score       INTEGER NOT NULL CHECK (score > 0 AND score <= 50000000 AND score % 10 = 0),
    created_at  TEXT    NOT NULL
) STRICT;

CREATE INDEX IF NOT EXISTS idx_submitted_runs_created_at
    ON leaderboard_submitted_runs (created_at);

-- 5. Per-minute rate-limit counters (keyed by salted IP hash and player hash)
CREATE TABLE IF NOT EXISTS leaderboard_rate_limits (
    bucket_key    TEXT    NOT NULL,
    window_minute TEXT    NOT NULL,
    hit_count     INTEGER NOT NULL DEFAULT 1 CHECK (hit_count >= 1),
    PRIMARY KEY (bucket_key, window_minute)
) STRICT;

CREATE INDEX IF NOT EXISTS idx_rate_limits_window
    ON leaderboard_rate_limits (window_minute);
