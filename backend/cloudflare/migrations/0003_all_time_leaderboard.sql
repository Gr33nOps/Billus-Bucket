-- Retain every archived score and profile. Recover any historical best that
-- predates permanent profiles; existing custom names and identities stay intact.
INSERT INTO player_profiles (
    player_hash, display_name, all_time_best, source_platform, created_at, updated_at
)
SELECT player_hash, display_name, MAX(best_score), source_platform,
       MIN(submitted_at), MAX(submitted_at)
FROM weekly_leaderboard_scores
GROUP BY player_hash
ON CONFLICT (player_hash) DO UPDATE SET
    all_time_best = MAX(player_profiles.all_time_best, excluded.all_time_best),
    created_at = MIN(player_profiles.created_at, excluded.created_at);

CREATE INDEX IF NOT EXISTS idx_profiles_all_time_ranking
    ON player_profiles (all_time_best DESC, created_at ASC, player_hash ASC)
    WHERE all_time_best > 0;
