// ============================================================================
// Billu Ki Balti — Supabase Edge Function: /functions/v1/leaderboard
// ============================================================================
// Required server-only environment secrets (set via `supabase secrets set`):
//   SUPABASE_URL               (auto-injected by Supabase)
//   SUPABASE_SERVICE_ROLE_KEY  (auto-injected by Supabase; NEVER expose to client)
//   PLAYER_HASH_SALT           (random 32+ char secret salt for anonymous ID hashing)
//   ALLOWED_ORIGINS            (comma-separated allowed origins, or "*" during local dev)
// ============================================================================

import { createClient } from "https://esm.sh/@supabase/supabase-js@2.49.1";

const MAX_SCORE = 50_000_000;            // Hard ceiling (5 Crore Rs in a single human run)
const MAX_RUPEES_PER_SECOND = 15_000;    // Plausible upper rate bound given DropDirector >= 0.24s spawn tempo
const MIN_DURATION_SECONDS = 0.5;
const MAX_WRITES_PER_MINUTE = 6;
const MAX_READS_PER_MINUTE = 30;
const ALLOWED_PLATFORMS = new Set(["local", "web", "crazygames", "poki", "desktop"]);

function buildCorsHeaders(origin: string | null): Record<string, string> {
  const configured = (Deno.env.get("ALLOWED_ORIGINS") ?? "*").trim();
  let allowOrigin = "*";

  if (configured !== "*") {
    const allowedList = configured.split(",").map((s) => s.trim()).filter(Boolean);
    allowOrigin = origin && allowedList.includes(origin) ? origin : allowedList[0] ?? "null";
  }

  return {
    "Access-Control-Allow-Origin": allowOrigin,
    "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Content-Type": "application/json; charset=utf-8",
  };
}

async function sha256Hex(input: string): Promise<string> {
  const bytes = new TextEncoder().encode(input);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return Array.from(new Uint8Array(digest))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function sanitizeSafeDisplayName(rawName: unknown, playerHash: string): string {
  // Enforce generated BILLU_XXXX format so unmoderated free-text cannot appear on portals.
  if (typeof rawName === "string" && /^BILLU_[1-9][0-9]{3}$/.test(rawName.trim().toUpperCase())) {
    return rawName.trim().toUpperCase();
  }
  const fallbackNum = (parseInt(playerHash.slice(0, 8), 16) % 9000) + 1000;
  return `BILLU_${fallbackNum}`;
}

function normalizePlatform(rawPlatform: unknown): string {
  const cleaned = typeof rawPlatform === "string" ? rawPlatform.trim().toLowerCase() : "web";
  return ALLOWED_PLATFORMS.has(cleaned) ? cleaned : "web";
}

Deno.serve(async (req: Request) => {
  const origin = req.headers.get("origin");
  const headers = buildCorsHeaders(origin);

  if (req.method === "OPTIONS") {
    return new Response(null, { status: 204, headers });
  }

  const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? "";
  const serviceRoleKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  const hashSalt = Deno.env.get("PLAYER_HASH_SALT") ?? "billu-default-change-in-prod";

  if (!supabaseUrl || !serviceRoleKey) {
    return new Response(
      JSON.stringify({ error: "Server configuration incomplete." }),
      { status: 500, headers },
    );
  }

  const supabase = createClient(supabaseUrl, serviceRoleKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  const clientIp =
    req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ??
    req.headers.get("cf-connecting-ip") ??
    "unknown-ip";
  const ipBucket = "ip:" + (await sha256Hex(`${hashSalt}:ip:${clientIp}`));

  try {
    if (req.method === "GET") {
      const { data: allowed } = await supabase.rpc("check_leaderboard_rate_limit", {
        p_bucket_key: `${ipBucket}:read`,
        p_max_per_minute: MAX_READS_PER_MINUTE,
      });
      if (allowed === false) {
        return new Response(JSON.stringify({ error: "Rate limit exceeded." }), {
          status: 429,
          headers,
        });
      }

      const url = new URL(req.url);
      const rawPlayerId = (url.searchParams.get("playerId") ?? "").trim();
      const limit = Math.min(10, Math.max(1, Number(url.searchParams.get("limit") ?? "10") || 10));
      const playerHash = rawPlayerId.length >= 8
        ? await sha256Hex(`${hashSalt}:player:${rawPlayerId}`)
        : "";

      const { data: snapshot, error } = await supabase.rpc("get_weekly_leaderboard_snapshot", {
        p_player_hash: playerHash,
        p_limit: limit,
      });

      if (error) {
        return new Response(JSON.stringify({ error: "Failed to load rankings." }), {
          status: 500,
          headers,
        });
      }

      return new Response(JSON.stringify(snapshot), { status: 200, headers });
    }

    if (req.method === "POST") {
      const body = await req.json();
      const rawRunId = typeof body?.runId === "string" ? body.runId.trim() : "";
      const rawPlayerId = typeof body?.playerId === "string" ? body.playerId.trim() : "";
      const rawScore = typeof body?.score === "string" ? body.score.trim() : String(body?.score ?? "");
      const durationSeconds = Number(body?.durationSeconds ?? 0);
      const platform = normalizePlatform(body?.platform);

      if (rawRunId.length < 16 || rawRunId.length > 64 || rawPlayerId.length < 16 || rawPlayerId.length > 64) {
        return new Response(JSON.stringify({ error: "Invalid run or player identifier." }), {
          status: 400,
          headers,
        });
      }

      if (!/^[1-9][0-9]{0,8}$/.test(rawScore)) {
        return new Response(JSON.stringify({ error: "Invalid score format." }), {
          status: 400,
          headers,
        });
      }

      const score = Number(rawScore);
      // Every rupee denomination in NoteCatalog (10, 20, 50, 100, 500, 1000, 5000) is divisible by 10.
      if (!Number.isSafeInteger(score) || score <= 0 || score > MAX_SCORE || score % 10 !== 0) {
        return new Response(JSON.stringify({ error: "Score outside valid gameplay bounds." }), {
          status: 400,
          headers,
        });
      }

      // Plausibility check against elapsed run duration
      if (!Number.isFinite(durationSeconds) || durationSeconds < MIN_DURATION_SECONDS) {
        return new Response(JSON.stringify({ error: "Invalid run duration." }), {
          status: 400,
          headers,
        });
      }

      const maxPlausibleForDuration = Math.max(15_000, Math.ceil(durationSeconds * MAX_RUPEES_PER_SECOND));
      if (score > maxPlausibleForDuration) {
        return new Response(JSON.stringify({ error: "Score exceeds plausible catch rate." }), {
          status: 400,
          headers,
        });
      }

      const playerHash = await sha256Hex(`${hashSalt}:player:${rawPlayerId}`);
      const runIdHash = await sha256Hex(`${hashSalt}:run:${rawRunId}`);
      const safeDisplayName = sanitizeSafeDisplayName(body?.displayName, playerHash);

      // Enforce per-IP and per-player write rate limits
      const { data: ipAllowed } = await supabase.rpc("check_leaderboard_rate_limit", {
        p_bucket_key: `${ipBucket}:write`,
        p_max_per_minute: MAX_WRITES_PER_MINUTE,
      });
      const { data: playerAllowed } = await supabase.rpc("check_leaderboard_rate_limit", {
        p_bucket_key: `player:${playerHash}:write`,
        p_max_per_minute: MAX_WRITES_PER_MINUTE,
      });

      if (ipAllowed === false || playerAllowed === false) {
        return new Response(JSON.stringify({ error: "Too many score submissions." }), {
          status: 429,
          headers,
        });
      }

      // Duplicate-run protection: reject replayed runIds
      const { error: runInsertError } = await supabase
        .from("leaderboard_submitted_runs")
        .insert({
          run_id_hash: runIdHash,
          player_hash: playerHash,
          score: score,
        });

      if (runInsertError) {
        // Postgres 23505 = unique_violation (duplicate runId)
        const status = runInsertError.code === "23505" ? 409 : 500;
        return new Response(JSON.stringify({ error: "Duplicate or rejected run submission." }), {
          status,
          headers,
        });
      }

      const { error: upsertError } = await supabase.rpc("upsert_weekly_best_score", {
        p_player_hash: playerHash,
        p_display_name: safeDisplayName,
        p_score: score,
        p_source_platform: platform,
      });

      if (upsertError) {
        return new Response(JSON.stringify({ error: "Could not record score." }), {
          status: 500,
          headers,
        });
      }

      const { data: snapshot, error: snapError } = await supabase.rpc(
        "get_weekly_leaderboard_snapshot",
        {
          p_player_hash: playerHash,
          p_limit: 10,
        },
      );

      if (snapError) {
        return new Response(JSON.stringify({ error: "Saved, but failed to fetch rank." }), {
          status: 500,
          headers,
        });
      }

      return new Response(JSON.stringify(snapshot), { status: 200, headers });
    }

    return new Response(JSON.stringify({ error: "Method not allowed." }), {
      status: 405,
      headers,
    });
  } catch (_err) {
    return new Response(JSON.stringify({ error: "Malformed request." }), {
      status: 400,
      headers,
    });
  }
});
