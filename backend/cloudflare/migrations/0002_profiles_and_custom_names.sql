-- ============================================================================
-- Non-destructive migration for existing D1 database:
-- 1. Adds player_profiles and player_profile_aliases tables.
-- 2. Upgrades weekly_leaderboard_scores CHECK constraints in-place to allow
--    2..12 character custom names ([A-Z0-9_-]) and 'android'/'ios' platforms
--    while preserving 100% of existing weekly scores.
-- ============================================================================

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

CREATE TABLE IF NOT EXISTS player_profile_aliases (
    alias_hash            TEXT    PRIMARY KEY CHECK (length(alias_hash) = 64),
    canonical_player_hash TEXT    NOT NULL CHECK (length(canonical_player_hash) = 64),
    created_at            TEXT    NOT NULL
) STRICT;

CREATE INDEX IF NOT EXISTS idx_profile_aliases_canonical
    ON player_profile_aliases (canonical_player_hash);

CREATE TABLE weekly_leaderboard_scores_v2 (
    week_start      TEXT    NOT NULL,
    player_hash     TEXT    NOT NULL CHECK (length(player_hash) = 64),
    display_name    TEXT    NOT NULL CHECK (length(display_name) BETWEEN 2 AND 12 AND display_name NOT GLOB '*[^A-Z0-9_-]*'),
    best_score      INTEGER NOT NULL CHECK (best_score > 0 AND best_score <= 50000000 AND best_score % 10 = 0),
    submitted_at    TEXT    NOT NULL,
    source_platform TEXT    NOT NULL CHECK (source_platform IN ('local', 'web', 'crazygames', 'poki', 'desktop', 'android', 'ios')),
    PRIMARY KEY (week_start, player_hash)
) STRICT;

INSERT INTO weekly_leaderboard_scores_v2 (
    week_start, player_hash, display_name, best_score, submitted_at, source_platform
)
SELECT
    week_start, player_hash, display_name, best_score, submitted_at, source_platform
FROM weekly_leaderboard_scores;

DROP TABLE weekly_leaderboard_scores;

ALTER TABLE weekly_leaderboard_scores_v2 RENAME TO weekly_leaderboard_scores;

CREATE INDEX IF NOT EXISTS idx_weekly_scores_ranking
    ON weekly_leaderboard_scores (week_start, best_score DESC, submitted_at ASC, player_hash ASC);

-- Backfill player_profiles from any existing weekly scores
INSERT INTO player_profiles (
    player_hash, display_name, all_time_best, source_platform, created_at, updated_at
)
SELECT
    player_hash,
    display_name,
    MAX(best_score),
    source_platform,
    MIN(submitted_at),
    MAX(submitted_at)
FROM weekly_leaderboard_scores
GROUP BY player_hash
ON CONFLICT (player_hash) DO NOTHING;
