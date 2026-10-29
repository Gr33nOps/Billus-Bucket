-- ============================================================================
-- Billu Ki Balti — Optional Shared Weekly Leaderboard Schema (Supabase/Postgres)
-- ============================================================================
-- Security model:
-- 1. Row-Level Security (RLS) is ENABLED on every table with NO public/anon write policies.
-- 2. Client builds never contain SUPABASE_SERVICE_ROLE_KEY or database passwords.
-- 3. All reads and writes go through the `leaderboard` Edge Function, which enforces
--    rate limiting, duplicate-run protection, safe display names, and score bounds.
-- ============================================================================

-- Computes the current weekly reset boundary: Monday 09:00 UTC
-- (aligned with CrazyGames' Monday 09:00 UTC weekly leaderboard reset).
create or replace function public.current_leaderboard_week_start(ts timestamptz default now())
returns timestamptz
language sql
immutable
as $$
    select date_trunc('week', ts at time zone 'UTC' - interval '9 hours')
           + interval '9 hours'
           at time zone 'UTC';
$$;

-- 1. Weekly best score per anonymous player
create table if not exists public.weekly_leaderboard_scores (
    week_start      timestamptz not null default public.current_leaderboard_week_start(),
    player_hash     text        not null check (char_length(player_hash) = 64),
    display_name    text        not null check (display_name ~ '^[A-Z0-9_]{3,12}$'),
    best_score      bigint      not null check (best_score > 0 and best_score % 10 = 0),
    submitted_at    timestamptz not null default now(),
    source_platform text        not null check (source_platform in ('local', 'web', 'crazygames', 'poki', 'desktop')),
    primary key (week_start, player_hash)
);

create index if not exists idx_weekly_scores_ranking
    on public.weekly_leaderboard_scores (week_start, best_score desc, submitted_at asc);

-- 2. Completed run IDs to prevent duplicate / replay submissions of the same run
create table if not exists public.leaderboard_submitted_runs (
    run_id_hash text        primary key check (char_length(run_id_hash) = 64),
    player_hash text        not null,
    score       bigint      not null,
    created_at  timestamptz not null default now()
);

create index if not exists idx_submitted_runs_created_at
    on public.leaderboard_submitted_runs (created_at);

-- 3. Per-minute rate-limit counters (keyed by salted IP hash and player hash)
create table if not exists public.leaderboard_rate_limits (
    bucket_key    text        not null,
    window_minute timestamptz not null,
    hit_count     integer     not null default 1,
    primary key (bucket_key, window_minute)
);

-- Enable strict Row-Level Security (zero direct client table access)
alter table public.weekly_leaderboard_scores enable row level security;
alter table public.leaderboard_submitted_runs enable row level security;
alter table public.leaderboard_rate_limits enable row level security;

-- Atomic rate-limit increment RPC (called only by the Edge Function using service_role)
create or replace function public.check_leaderboard_rate_limit(
    p_bucket_key text,
    p_max_per_minute integer
)
returns boolean
language plpgsql
security definer
set search_path = public
as $$
declare
    v_window timestamptz := date_trunc('minute', now());
    v_count  integer;
begin
    insert into public.leaderboard_rate_limits (bucket_key, window_minute, hit_count)
    values (p_bucket_key, v_window, 1)
    on conflict (bucket_key, window_minute)
    do update set hit_count = public.leaderboard_rate_limits.hit_count + 1
    returning hit_count into v_count;

    -- Opportunistically prune rate-limit rows older than 2 hours
    delete from public.leaderboard_rate_limits
    where window_minute < now() - interval '2 hours';

    return v_count <= p_max_per_minute;
end;
$$;

-- Atomic score upsert (only updates best_score if the new run beats this week's personal best)
create or replace function public.upsert_weekly_best_score(
    p_player_hash text,
    p_display_name text,
    p_score bigint,
    p_source_platform text
)
returns void
language plpgsql
security definer
set search_path = public
as $$
declare
    v_week timestamptz := public.current_leaderboard_week_start();
begin
    insert into public.weekly_leaderboard_scores (
        week_start,
        player_hash,
        display_name,
        best_score,
        submitted_at,
        source_platform
    )
    values (
        v_week,
        p_player_hash,
        p_display_name,
        p_score,
        now(),
        p_source_platform
    )
    on conflict (week_start, player_hash)
    do update set
        best_score      = greatest(public.weekly_leaderboard_scores.best_score, excluded.best_score),
        display_name    = excluded.display_name,
        source_platform = excluded.source_platform,
        submitted_at    = case
            when excluded.best_score > public.weekly_leaderboard_scores.best_score then now()
            else public.weekly_leaderboard_scores.submitted_at
        end;
end;
$$;

-- Returns weekly Top-N rows plus the requesting player's weekly rank
create or replace function public.get_weekly_leaderboard_snapshot(
    p_player_hash text default '',
    p_limit integer default 10
)
returns jsonb
language plpgsql
security definer
set search_path = public
as $$
declare
    v_week        timestamptz := public.current_leaderboard_week_start();
    v_safe_limit  integer := least(greatest(coalesce(p_limit, 10), 1), 10);
    v_player_rank integer;
    v_entries     jsonb;
begin
    with ranked as (
        select
            row_number() over (order by best_score desc, submitted_at asc) as rank_pos,
            player_hash,
            display_name,
            best_score
        from public.weekly_leaderboard_scores
        where week_start = v_week
    )
    select
        (select rank_pos::integer from ranked where player_hash = p_player_hash limit 1),
        coalesce(
            (
                select jsonb_agg(
                    jsonb_build_object(
                        'rank', rank_pos,
                        'displayName', display_name,
                        'score', best_score::text,
                        'isCurrentPlayer', (player_hash = p_player_hash)
                    )
                    order by rank_pos asc
                )
                from ranked
                where rank_pos <= v_safe_limit
            ),
            '[]'::jsonb
        )
    into v_player_rank, v_entries;

    return jsonb_build_object(
        'weekStart', v_week,
        'playerRank', v_player_rank,
        'entries', v_entries
    );
end;
$$;

revoke all on function public.check_leaderboard_rate_limit(text, integer) from public, anon, authenticated;
revoke all on function public.upsert_weekly_best_score(text, text, bigint, text) from public, anon, authenticated;
revoke all on function public.get_weekly_leaderboard_snapshot(text, integer) from public, anon, authenticated;
