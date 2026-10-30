// ============================================================================
// Billu's Bucket — Cloudflare Worker + D1 Weekly Leaderboard & Cloud Profile API
// ============================================================================
// Zero external npm runtime dependencies. Supports:
//   - Custom player names (2..12 chars, profanity-filtered for App Store / portal UGC safety)
//   - Persistent cloud profiles + cross-device 12-char Save/Connect Codes
//   - Ready for Web, Desktop, Google Play Store ('android'), and Apple App Store ('ios')
// ============================================================================

const MAX_SCORE = 50_000_000; // Hard ceiling (5 Crore Rs in a single human run)
const MAX_RUPEES_PER_SECOND = 15_000;
const MIN_DURATION_SECONDS = 0.5;
const MAX_WRITES_PER_MINUTE = 8;
const MAX_READS_PER_MINUTE = 30;
const ALLOWED_PLATFORMS = new Set([
  "local",
  "web",
  "crazygames",
  "poki",
  "desktop",
  "android",
  "ios",
]);

// Compact profanity / slur substring blocklist for App Store, Play Store, and portal UGC safety.
const BLOCKED_NAME_SUBSTRINGS = [
  "FUCK",
  "SHIT",
  "BITCH",
  "CUNT",
  "DICK",
  "COCK",
  "PUSSY",
  "NIGG",
  "FAGG",
  "SLUT",
  "WHORE",
  "PENIS",
  "VAGIN",
  "PORN",
  "RAPE",
  "NAZI",
  "HITLER",
  "CHUT",
  "BHENCH",
  "MADARCH",
  "GANDU",
  "RANDI",
  "LUND",
  "BHOSD",
];

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
    return null;
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

/**
 * Normalizes a player token or 12-char Save Code (`XXXX-XXXX-XXXX`) by removing
 * hyphens/spaces and uppercasing. Accepts 12..64 alphanumeric characters.
 */
export function normalizePlayerToken(rawToken) {
  if (typeof rawToken !== "string") {
    return "";
  }
  const compact = rawToken.trim().toUpperCase().replace(/[\s-]+/g, "");
  return /^[A-Z0-9]{12,64}$/.test(compact) ? compact : "";
}

function fallbackDisplayName(playerHash) {
  const fallbackNum = (parseInt(playerHash.slice(0, 8), 16) % 9000) + 1000;
  return `BILLU_${fallbackNum}`;
}

/**
 * Validates a user-chosen display name (2..12 chars, A-Z / 0-9 / _ / -,
 * starts and ends with letter or digit, no blocked profanity).
 */
export function validateCustomDisplayName(rawName) {
  if (typeof rawName !== "string") {
    return { valid: false, name: "", error: "ENTER 2-12 LETTERS OR NUMBERS" };
  }

  const candidate = rawName
    .trim()
    .toUpperCase()
    .replace(/\s+/g, "_");

  if (candidate.length < 2 || candidate.length > 12) {
    return { valid: false, name: "", error: "NAME MUST BE 2-12 CHARACTERS" };
  }

  if (!/^[A-Z0-9][A-Z0-9_-]{0,10}[A-Z0-9]$/.test(candidate)) {
    return {
      valid: false,
      name: "",
      error: "USE LETTERS, NUMBERS, _ OR -",
    };
  }

  const lettersAndDigitsOnly = candidate
    .replace(/[_-]+/g, "")
    .replace(/0/g, "O")
    .replace(/1/g, "I")
    .replace(/3/g, "E")
    .replace(/4/g, "A")
    .replace(/5/g, "S");

  for (const blocked of BLOCKED_NAME_SUBSTRINGS) {
    if (
      candidate.includes(blocked) ||
      lettersAndDigitsOnly.includes(blocked)
    ) {
      return { valid: false, name: "", error: "NAME NOT ALLOWED" };
    }
  }

  return { valid: true, name: candidate, error: "" };
}

function sanitizeSafeDisplayName(rawName, playerHash) {
  const checked = validateCustomDisplayName(rawName);
  return checked.valid ? checked.name : fallbackDisplayName(playerHash);
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

async function resolveCanonicalPlayerHash(db, hashSalt, normalizedToken) {
  if (!normalizedToken) {
    return "";
  }
  const rawHash = await sha256Hex(`${hashSalt}:player:${normalizedToken}`);
  const aliasRow = await db.prepare(
    `SELECT canonical_player_hash
     FROM player_profile_aliases
     WHERE alias_hash = ?`,
  ).bind(rawHash).first();

  return aliasRow?.canonical_player_hash
    ? String(aliasRow.canonical_player_hash)
    : rawHash;
}

async function bindConnectCodeAliasIfNeeded(
  db,
  hashSalt,
  canonicalPlayerHash,
  rawConnectCode,
  nowIso,
) {
  const normalizedCode = normalizePlayerToken(rawConnectCode);
  if (!normalizedCode || normalizedCode.length !== 12) {
    return;
  }

  const codeHash = await sha256Hex(`${hashSalt}:player:${normalizedCode}`);
  if (codeHash === canonicalPlayerHash) {
    return;
  }

  // Never hijack an existing standalone profile hash.
  const existingProfile = await db.prepare(
    `SELECT player_hash FROM player_profiles WHERE player_hash = ?`,
  ).bind(codeHash).first();
  if (existingProfile) {
    return;
  }

  await db.prepare(
    `INSERT INTO player_profile_aliases (alias_hash, canonical_player_hash, created_at)
     VALUES (?, ?, ?)
     ON CONFLICT (alias_hash) DO NOTHING`,
  ).bind(codeHash, canonicalPlayerHash, nowIso).all();
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

  let profileDisplayName = null;
  let profileAllTimeBest = null;
  if (playerHash) {
    const profileRow = await db.prepare(
      `SELECT display_name, all_time_best
       FROM player_profiles
       WHERE player_hash = ?`,
    ).bind(playerHash).first();

    if (profileRow) {
      profileDisplayName = String(profileRow.display_name);
      profileAllTimeBest = String(profileRow.all_time_best ?? 0);
    }
  }

  return {
    weekStart,
    playerRank,
    displayName: profileDisplayName,
    personalBest: profileAllTimeBest,
    entries,
  };
}

function parseOptionalBestScore(rawBest) {
  if (rawBest === undefined || rawBest === null || rawBest === "") {
    return 0;
  }
  const str = String(rawBest).trim();
  if (str === "0") {
    return 0;
  }
  if (!/^[1-9][0-9]{0,8}$/.test(str)) {
    return -1;
  }
  const num = Number(str);
  if (!Number.isSafeInteger(num) || num < 0 || num > MAX_SCORE || num % 10 !== 0) {
    return -1;
  }
  return num;
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
        const normalizedToken = normalizePlayerToken(
          url.searchParams.get("playerId") ?? "",
        );
        const limit = Math.min(
          10,
          Math.max(
            1,
            Math.floor(Number(url.searchParams.get("limit") ?? "10")) || 10,
          ),
        );

        try {
          const playerHash = normalizedToken
            ? await resolveCanonicalPlayerHash(db, hashSalt, normalizedToken)
            : "";
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
        const action = typeof body?.action === "string"
          ? body.action.trim().toLowerCase()
          : "submit_score";
        const platform = normalizePlatform(body?.platform);

        // -------------------------------------------------------------------
        // Action 1: Save / Rename Player Profile (and update live weekly row)
        // -------------------------------------------------------------------
        if (action === "save_profile") {
          const normalizedToken = normalizePlayerToken(body?.playerId);
          if (!normalizedToken) {
            return new Response(
              JSON.stringify({ error: "INVALID PLAYER ID" }),
              { status: 400, headers },
            );
          }

          const checkedName = validateCustomDisplayName(body?.displayName);
          if (!checkedName.valid) {
            return new Response(
              JSON.stringify({ error: checkedName.error }),
              { status: 400, headers },
            );
          }

          const optionalBest = parseOptionalBestScore(body?.personalBest);
          if (optionalBest < 0) {
            return new Response(
              JSON.stringify({ error: "INVALID PERSONAL BEST" }),
              { status: 400, headers },
            );
          }

          let ipAllowed = false;
          try {
            ipAllowed = await checkRateLimit(
              db,
              `${ipBucket}:write`,
              MAX_WRITES_PER_MINUTE,
              nowMs,
            );
          } catch (_rateErr) {
            return new Response(
              JSON.stringify({ error: "Rate limiter unavailable." }),
              { status: 503, headers },
            );
          }

          if (!ipAllowed) {
            return new Response(
              JSON.stringify({ error: "TOO MANY UPDATES - WAIT A MINUTE" }),
              { status: 429, headers },
            );
          }

          try {
            const playerHash = await resolveCanonicalPlayerHash(
              db,
              hashSalt,
              normalizedToken,
            );
            await bindConnectCodeAliasIfNeeded(
              db,
              hashSalt,
              playerHash,
              body?.connectCode,
              nowIso,
            );

            await db.batch([
              db.prepare(
                `INSERT INTO player_profiles (
                   player_hash, display_name, all_time_best, source_platform, created_at, updated_at
                 )
                 VALUES (?, ?, ?, ?, ?, ?)
                 ON CONFLICT (player_hash)
                 DO UPDATE SET
                   display_name = excluded.display_name,
                   all_time_best = MAX(player_profiles.all_time_best, excluded.all_time_best),
                   source_platform = excluded.source_platform,
                   updated_at = excluded.updated_at`,
              ).bind(
                playerHash,
                checkedName.name,
                optionalBest,
                platform,
                nowIso,
                nowIso,
              ),
              db.prepare(
                `UPDATE weekly_leaderboard_scores
                 SET display_name = ?
                 WHERE week_start = ? AND player_hash = ?`,
              ).bind(checkedName.name, weekStart, playerHash),
            ]);

            const snapshot = await getWeeklyLeaderboardSnapshot(
              db,
              weekStart,
              playerHash,
              10,
            );
            return new Response(
              JSON.stringify({ saved: true, ...snapshot }),
              { status: 200, headers },
            );
          } catch (_err) {
            return new Response(
              JSON.stringify({ error: "FAILED TO SAVE PROFILE" }),
              { status: 500, headers },
            );
          }
        }

        // -------------------------------------------------------------------
        // Action 2: Restore Cloud Profile on Another Device via Save Code
        // -------------------------------------------------------------------
        if (action === "restore_profile") {
          const normalizedCode = normalizePlayerToken(
            body?.connectCode ?? body?.playerId,
          );
          if (!normalizedCode) {
            return new Response(
              JSON.stringify({ found: false, error: "ENTER 12-CHAR SAVE CODE" }),
              { status: 400, headers },
            );
          }

          let ipAllowed = false;
          try {
            ipAllowed = await checkRateLimit(
              db,
              `${ipBucket}:write`,
              MAX_WRITES_PER_MINUTE,
              nowMs,
            );
          } catch (_rateErr) {
            return new Response(
              JSON.stringify({ error: "Rate limiter unavailable." }),
              { status: 503, headers },
            );
          }

          if (!ipAllowed) {
            return new Response(
              JSON.stringify({ found: false, error: "TOO MANY ATTEMPTS - WAIT" }),
              { status: 429, headers },
            );
          }

          try {
            const playerHash = await resolveCanonicalPlayerHash(
              db,
              hashSalt,
              normalizedCode,
            );
            let profileRow = await db.prepare(
              `SELECT display_name, all_time_best
               FROM player_profiles
               WHERE player_hash = ?`,
            ).bind(playerHash).first();

            if (!profileRow) {
              const weeklyFallback = await db.prepare(
                `SELECT display_name, MAX(best_score) AS max_best
                 FROM weekly_leaderboard_scores
                 WHERE player_hash = ?`,
              ).bind(playerHash).first();

              if (weeklyFallback?.display_name) {
                profileRow = {
                  display_name: weeklyFallback.display_name,
                  all_time_best: weeklyFallback.max_best ?? 0,
                };
              }
            }

            if (!profileRow) {
              return new Response(
                JSON.stringify({ found: false, error: "SAVE CODE NOT FOUND" }),
                { status: 404, headers },
              );
            }

            const snapshot = await getWeeklyLeaderboardSnapshot(
              db,
              weekStart,
              playerHash,
              10,
            );
            return new Response(
              JSON.stringify({
                found: true,
                ...snapshot,
                displayName: String(profileRow.display_name),
                personalBest: String(profileRow.all_time_best ?? 0),
              }),
              { status: 200, headers },
            );
          } catch (_err) {
            return new Response(
              JSON.stringify({ found: false, error: "RESTORE UNAVAILABLE" }),
              { status: 500, headers },
            );
          }
        }

        // -------------------------------------------------------------------
        // Action 3 (Default): Submit Completed Run Score
        // -------------------------------------------------------------------
        if (action !== "submit_score") {
          return new Response(
            JSON.stringify({ error: "Unknown action." }),
            { status: 400, headers },
          );
        }

        const rawRunId = typeof body?.runId === "string"
          ? body.runId.trim()
          : "";
        const normalizedToken = normalizePlayerToken(body?.playerId);
        const rawScore = typeof body?.score === "string"
          ? body.score.trim()
          : String(body?.score ?? "");
        const durationSeconds = Number(body?.durationSeconds ?? 0);

        if (
          rawRunId.length < 16 || rawRunId.length > 64 ||
          !normalizedToken
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

        let ipAllowed = false;
        let playerAllowed = false;
        let playerHash = "";
        try {
          playerHash = await resolveCanonicalPlayerHash(
            db,
            hashSalt,
            normalizedToken,
          );
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

        const runIdHash = await sha256Hex(`${hashSalt}:run:${rawRunId}`);
        let safeDisplayName = sanitizeSafeDisplayName(
          body?.displayName,
          playerHash,
        );
        const pruneRunsBefore = new Date(nowMs - 30 * 86_400_000).toISOString();

        try {
          if (/^BILLU_[1-9][0-9]{3}$/.test(safeDisplayName)) {
            const existingProfile = await db.prepare(
              `SELECT display_name FROM player_profiles WHERE player_hash = ?`,
            ).bind(playerHash).first();
            if (
              existingProfile?.display_name &&
              !/^BILLU_[1-9][0-9]{3}$/.test(String(existingProfile.display_name))
            ) {
              safeDisplayName = String(existingProfile.display_name);
            }
          }

          await bindConnectCodeAliasIfNeeded(
            db,
            hashSalt,
            playerHash,
            body?.connectCode,
            nowIso,
          );

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
              `INSERT INTO player_profiles (
                 player_hash, display_name, all_time_best, source_platform, created_at, updated_at
               )
               VALUES (?, ?, ?, ?, ?, ?)
               ON CONFLICT (player_hash)
               DO UPDATE SET
                 display_name = excluded.display_name,
                 all_time_best = MAX(player_profiles.all_time_best, excluded.all_time_best),
                 source_platform = excluded.source_platform,
                 updated_at = excluded.updated_at`,
            ).bind(
              playerHash,
              safeDisplayName,
              score,
              platform,
              nowIso,
              nowIso,
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
