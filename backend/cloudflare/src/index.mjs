// ============================================================================
// Billu's Bucket — Cloudflare Worker + D1 Weekly Leaderboard API
// ============================================================================
// Zero external npm runtime dependencies. Compatible with both:
//   1. `npx wrangler deploy` (from `backend/cloudflare/`)
//   2. Direct paste into Cloudflare Dashboard -> Workers -> Quick Edit
//
// Required Worker bindings / secrets:
//   env.DB               (D1Database binding pointing to your D1 SQLite DB)
//   env.PLAYER_HASH_SALT (Secret: random 32+ char salt for anonymous ID hashing)
//   env.ALLOWED_ORIGINS  (Var: comma-separated origins, e.g. "https://gr33nops.github.io")
// ============================================================================

const MAX_SCORE = 50_000_000; // Hard ceiling (5 Crore Rs in a single human run)
const MAX_RUPEES_PER_SECOND = 15_000; // Plausible upper rate bound given DropDirector >= 0.24s spawn tempo
const MIN_DURATION_SECONDS = 0.5;
const MAX_WRITES_PER_MINUTE = 6;
const MAX_READS_PER_MINUTE = 30;
const ALLOWED_PLATFORMS = new Set([
  "local",
  "web",
  "crazygames",
  "poki",
  "desktop",
]);

/**
 * Computes the current weekly reset boundary: Monday 09:00:00.000 UTC
 * (aligned with CrazyGames' Monday 09:00 UTC weekly leaderboard reset).
 * @param {number} [nowMs]
 * @returns {string}
 */
export function computeLeaderboardWeekStartIso(nowMs = Date.now()) {
  const shifted = new Date(nowMs - 9 * 3_600_000);
  const daysSinceMonday = (shifted.getUTCDay() + 6) % 7;
  const weekStartMs = Date.UTC(
    shifted.getUTCFullYear(),
    shifted.getUTCMonth(),
    shifted.getUTCDate() - daysSinceMonday,
    9,
    0,
    0,
    0,
  );
  return new Date(weekStartMs).toISOString();
}

function parseAllowedOrigins(rawConfigured) {
  const configured = String(rawConfigured ?? "*").trim();
  if (!configured || configured === "*") {
    return null; // wildcard allowed
  }
  const list = configured
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean);
  return list.length > 0 ? list : null;
}

function buildCorsHeaders(origin, allowedList) {
  let allowOrigin = "*";
  if (allowedList !== null) {
    allowOrigin = origin && allowedList.includes(origin) ? origin : "null";
  }

  return {
    "Access-Control-Allow-Origin": allowOrigin,
    "Access-Control-Allow-Headers":
      "authorization, x-client-info, apikey, content-type",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Vary": "Origin",
    "Content-Type": "application/json; charset=utf-8",
  };
}

async function sha256Hex(input) {
  const bytes = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function sanitizeSafeDisplayName(rawName, playerHash) {
  if (
    typeof rawName === "string" &&
    /^BILLU_[1-9][0-9]{3}$/.test(rawName.trim().toUpperCase())
  ) {
    return rawName.trim().toUpperCase();
  }
  const fallbackNum = (parseInt(playerHash.slice(0, 8), 16) % 9000) + 1000;
  return `BILLU_${fallbackNum}`;
}

function normalizePlatform(rawPlatform) {
  const cleaned = typeof rawPlatform === "string"
    ? rawPlatform.trim().toLowerCase()
    : "web";
  return ALLOWED_PLATFORMS.has(cleaned) ? cleaned : "web";
}

function isUniqueConstraintError(err) {
  const message = String(err?.message ?? err ?? "");
  return /UNIQUE constraint failed|SQLITE_CONSTRAINT_PRIMARYKEY|SQLITE_CONSTRAINT_UNIQUE/i
    .test(message);
}

async function checkRateLimit(db, bucketKey, maxPerMinute, nowMs) {
  const windowMinute = new Date(Math.floor(nowMs / 60_000) * 60_000)
    .toISOString();
  const pruneBefore = new Date(nowMs - 2 * 3_600_000).toISOString();

  const batchResults = await db.batch([
    db.prepare(
      `INSERT INTO leaderboard_rate_limits (bucket_key, window_minute, hit_count)
       VALUES (?, ?, 1)
       ON CONFLICT (bucket_key, window_minute)
       DO UPDATE SET hit_count = leaderboard_rate_limits.hit_count + 1
       RETURNING hit_count`,
    ).bind(bucketKey, windowMinute),
    db.prepare(
      `DELETE FROM leaderboard_rate_limits WHERE window_minute < ?`,
    ).bind(pruneBefore),
  ]);

  const hitCount = Number(batchResults?.[0]?.results?.[0]?.hit_count ?? NaN);
  if (!Number.isFinite(hitCount) || hitCount < 1) {
    throw new Error("Invalid rate-limit counter result.");
  }

  return hitCount <= maxPerMinute;
}

async function getWeeklyLeaderboardSnapshot(db, weekStart, playerHash, limit) {
  const safeLimit = Math.min(10, Math.max(1, Math.floor(Number(limit) || 10)));
  const topResult = await db.prepare(
    `SELECT player_hash, display_name, best_score
     FROM weekly_leaderboard_scores
     WHERE week_start = ?
     ORDER BY best_score DESC, submitted_at ASC, player_hash ASC
     LIMIT ?`,
  ).bind(weekStart, safeLimit).all();

  const rows = Array.isArray(topResult?.results) ? topResult.results : [];
  let playerRank = null;

  const entries = rows.map((row, index) => {
    const rank = index + 1;
    const isCurrentPlayer = Boolean(
      playerHash && row.player_hash === playerHash,
    );
    if (isCurrentPlayer) {
      playerRank = rank;
    }

    return {
      rank,
      displayName: String(row.display_name),
      score: String(row.best_score),
      isCurrentPlayer,
    };
  });

  if (playerHash && playerRank === null) {
    const playerRow = await db.prepare(
      `SELECT best_score, submitted_at
       FROM weekly_leaderboard_scores
       WHERE week_start = ? AND player_hash = ?`,
    ).bind(weekStart, playerHash).first();

    if (playerRow) {
      const rankRow = await db.prepare(
        `SELECT 1 + COUNT(*) AS rank_pos
         FROM weekly_leaderboard_scores
         WHERE week_start = ?
           AND (
             best_score > ?
             OR (best_score = ? AND submitted_at < ?)
             OR (best_score = ? AND submitted_at = ? AND player_hash < ?)
           )`,
      ).bind(
        weekStart,
        playerRow.best_score,
        playerRow.best_score,
        playerRow.submitted_at,
        playerRow.best_score,
        playerRow.submitted_at,
        playerHash,
      ).first();

      const computedRank = Number(rankRow?.rank_pos ?? 0);
      if (Number.isFinite(computedRank) && computedRank > 0) {
        playerRank = computedRank;
      }
    }
  }

  return {
    weekStart,
    playerRank,
    entries,
  };
}

/**
 * Creates a request handler (allows deterministic unit testing with a clock override).
 * @param {() => number} [clock]
 */
export function createCloudflareLeaderboardHandler(clock = () => Date.now()) {
  return async function handleRequest(req, env) {
    const origin = req.headers.get("origin");
    const allowedOrigins = parseAllowedOrigins(env?.ALLOWED_ORIGINS);
    const headers = buildCorsHeaders(origin, allowedOrigins);

    if (req.method === "OPTIONS") {
      if (allowedOrigins !== null && origin && !allowedOrigins.includes(origin)) {
        return new Response(null, { status: 403, headers });
      }
      return new Response(null, { status: 204, headers });
    }

    if (allowedOrigins !== null && origin && !allowedOrigins.includes(origin)) {
      return new Response(
        JSON.stringify({ error: "Origin not permitted." }),
        { status: 403, headers },
      );
    }

    const db = env?.DB;
    const hashSalt = typeof env?.PLAYER_HASH_SALT === "string"
      ? env.PLAYER_HASH_SALT
      : "";

    if (!db || hashSalt.length < 32) {
      return new Response(
        JSON.stringify({ error: "Server configuration incomplete." }),
        { status: 500, headers },
      );
    }

    const nowMs = clock();
    const nowIso = new Date(nowMs).toISOString();
    const weekStart = computeLeaderboardWeekStartIso(nowMs);

    const clientIp =
      req.headers.get("cf-connecting-ip")?.trim() ||
      req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ||
      "unknown-ip";
    const ipBucket = "ip:" + (await sha256Hex(`${hashSalt}:ip:${clientIp}`));

    try {
      if (req.method === "GET") {
        let allowed = false;
        try {
          allowed = await checkRateLimit(
            db,
            `${ipBucket}:read`,
            MAX_READS_PER_MINUTE,
            nowMs,
          );
        } catch (_rateErr) {
          return new Response(
            JSON.stringify({ error: "Rate limiter unavailable." }),
            { status: 503, headers },
          );
        }

        if (!allowed) {
          return new Response(
            JSON.stringify({ error: "Rate limit exceeded." }),
            { status: 429, headers },
          );
        }

        const url = new URL(req.url);
        const rawPlayerId = (url.searchParams.get("playerId") ?? "").trim();
        const limit = Math.min(
          10,
          Math.max(
            1,
            Math.floor(Number(url.searchParams.get("limit") ?? "10")) || 10,
          ),
        );
        const playerHash = rawPlayerId.length >= 8
          ? await sha256Hex(`${hashSalt}:player:${rawPlayerId}`)
          : "";

        try {
          const snapshot = await getWeeklyLeaderboardSnapshot(
            db,
            weekStart,
            playerHash,
            limit,
          );
          return new Response(JSON.stringify(snapshot), { status: 200, headers });
        } catch (_snapErr) {
          return new Response(
            JSON.stringify({ error: "Failed to load rankings." }),
            { status: 500, headers },
          );
        }
      }

      if (req.method === "POST") {
        const body = await req.json();
        const rawRunId = typeof body?.runId === "string"
          ? body.runId.trim()
          : "";
        const rawPlayerId = typeof body?.playerId === "string"
          ? body.playerId.trim()
          : "";
        const rawScore = typeof body?.score === "string"
          ? body.score.trim()
          : String(body?.score ?? "");
        const durationSeconds = Number(body?.durationSeconds ?? 0);
        const platform = normalizePlatform(body?.platform);

        if (
          rawRunId.length < 16 || rawRunId.length > 64 ||
          rawPlayerId.length < 16 || rawPlayerId.length > 64
        ) {
          return new Response(
            JSON.stringify({ error: "Invalid run or player identifier." }),
            { status: 400, headers },
          );
        }

        if (!/^[1-9][0-9]{0,8}$/.test(rawScore)) {
          return new Response(
            JSON.stringify({ error: "Invalid score format." }),
            { status: 400, headers },
          );
        }

        const score = Number(rawScore);
        // Every rupee denomination in NoteCatalog (10, 20, 50, 100, 500, 1000, 5000) is divisible by 10.
        if (
          !Number.isSafeInteger(score) || score <= 0 || score > MAX_SCORE ||
          score % 10 !== 0
        ) {
          return new Response(
            JSON.stringify({ error: "Score outside valid gameplay bounds." }),
            { status: 400, headers },
          );
        }

        if (
          !Number.isFinite(durationSeconds) ||
          durationSeconds < MIN_DURATION_SECONDS
        ) {
          return new Response(
            JSON.stringify({ error: "Invalid run duration." }),
            { status: 400, headers },
          );
        }

        const maxPlausibleForDuration = Math.max(
          15_000,
          Math.ceil(durationSeconds * MAX_RUPEES_PER_SECOND),
        );
        if (score > maxPlausibleForDuration) {
          return new Response(
            JSON.stringify({ error: "Score exceeds plausible catch rate." }),
            { status: 400, headers },
          );
        }

        const playerHash = await sha256Hex(`${hashSalt}:player:${rawPlayerId}`);
        const runIdHash = await sha256Hex(`${hashSalt}:run:${rawRunId}`);
        const safeDisplayName = sanitizeSafeDisplayName(
          body?.displayName,
          playerHash,
        );

        let ipAllowed = false;
        let playerAllowed = false;
        try {
          ipAllowed = await checkRateLimit(
            db,
            `${ipBucket}:write`,
            MAX_WRITES_PER_MINUTE,
            nowMs,
          );
          playerAllowed = await checkRateLimit(
            db,
            `player:${playerHash}:write`,
            MAX_WRITES_PER_MINUTE,
            nowMs,
          );
        } catch (_rateErr) {
          return new Response(
            JSON.stringify({ error: "Rate limiter unavailable." }),
            { status: 503, headers },
          );
        }

        if (!ipAllowed || !playerAllowed) {
          return new Response(
            JSON.stringify({ error: "Too many score submissions." }),
            { status: 429, headers },
          );
        }

        const pruneRunsBefore = new Date(nowMs - 30 * 86_400_000).toISOString();

        // Atomic D1 transaction batch: duplicate runId rolls back the score upsert,
        // and any score upsert failure rolls back the runId record.
        try {
          await db.batch([
            db.prepare(
              `INSERT INTO leaderboard_submitted_runs (run_id_hash, player_hash, score, created_at)
               VALUES (?, ?, ?, ?)`,
            ).bind(runIdHash, playerHash, score, nowIso),
            db.prepare(
              `INSERT INTO weekly_leaderboard_scores (
                 week_start, player_hash, display_name, best_score, submitted_at, source_platform
               )
               VALUES (?, ?, ?, ?, ?, ?)
               ON CONFLICT (week_start, player_hash)
               DO UPDATE SET
                 submitted_at = CASE
                   WHEN excluded.best_score > weekly_leaderboard_scores.best_score THEN excluded.submitted_at
                   ELSE weekly_leaderboard_scores.submitted_at
                 END,
                 best_score = MAX(weekly_leaderboard_scores.best_score, excluded.best_score),
                 display_name = excluded.display_name,
                 source_platform = excluded.source_platform`,
            ).bind(
              weekStart,
              playerHash,
              safeDisplayName,
              score,
              nowIso,
              platform,
            ),
            db.prepare(
              `DELETE FROM leaderboard_submitted_runs WHERE created_at < ?`,
            ).bind(pruneRunsBefore),
          ]);
        } catch (submitErr) {
          return new Response(
            JSON.stringify({ error: "Duplicate or rejected run submission." }),
            {
              status: isUniqueConstraintError(submitErr) ? 409 : 500,
              headers,
            },
          );
        }

        try {
          const snapshot = await getWeeklyLeaderboardSnapshot(
            db,
            weekStart,
            playerHash,
            10,
          );
          return new Response(JSON.stringify(snapshot), { status: 200, headers });
        } catch (_snapErr) {
          return new Response(
            JSON.stringify({ error: "Saved, but failed to fetch rank." }),
            { status: 500, headers },
          );
        }
      }

      return new Response(
        JSON.stringify({ error: "Method not allowed." }),
        { status: 405, headers },
      );
    } catch (_err) {
      return new Response(
        JSON.stringify({ error: "Malformed request." }),
        { status: 400, headers },
      );
    }
  };
}

const defaultHandler = createCloudflareLeaderboardHandler();

export default {
  fetch(request, env) {
    return defaultHandler(request, env);
  },
};
