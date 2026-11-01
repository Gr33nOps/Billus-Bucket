import assert from "node:assert/strict";
import fs from "node:fs";
import { DatabaseSync } from "node:sqlite";
import { test } from "node:test";
import {
  computeLeaderboardWeekStartIso,
  createCloudflareLeaderboardHandler,
} from "../backend/cloudflare/src/index.mjs";

const SCHEMA_SQL = fs.readFileSync("backend/cloudflare/schema.sql", "utf8");
const TEST_SALT = "test-only-cloudflare-salt-at-least-32-chars";

/**
 * Wraps Node 22's built-in SQLite DatabaseSync in Cloudflare D1's async
 * `prepare().bind().first()/all()` + atomic `batch()` interface.
 */
function createD1SqliteMock() {
  const sqlite = new DatabaseSync(":memory:");
  sqlite.exec(SCHEMA_SQL);

  function makeBoundStatement(sql, params) {
    function runRaw() {
      const stmt = sqlite.prepare(sql);
      stmt.setAllowBareNamedParameters(false);
      // Statements with RETURNING or SELECT return rows via .all()
      if (/^\s*(SELECT|WITH)\b|\bRETURNING\b/i.test(sql)) {
        const rows = stmt.all(...params).map((row) => ({ ...row }));
        return { results: rows, success: true };
      }
      stmt.run(...params);
      return { results: [], success: true };
    }

    return {
      _runSync: runRaw,
      async all() {
        return runRaw();
      },
      async first() {
        const res = runRaw();
        return res.results[0] ?? null;
      },
    };
  }

  return {
    sqlite,
    prepare(sql) {
      return {
        bind(...params) {
          return makeBoundStatement(sql, params);
        },
      };
    },
    async batch(statements) {
      sqlite.exec("BEGIN IMMEDIATE");
      try {
        const results = statements.map((stmt) => stmt._runSync());
        sqlite.exec("COMMIT");
        return results;
      } catch (err) {
        sqlite.exec("ROLLBACK");
        throw err;
      }
    },
    close() {
      sqlite.close();
    },
  };
}

test("Monday 09:00 UTC weekly reset boundary matches CrazyGames schedule", () => {
  assert.equal(
    computeLeaderboardWeekStartIso(Date.parse("2026-10-26T08:59:59.999Z")),
    "2026-10-19T09:00:00.000Z",
  );
  assert.equal(
    computeLeaderboardWeekStartIso(Date.parse("2026-10-26T09:00:00.000Z")),
    "2026-10-26T09:00:00.000Z",
  );
  assert.equal(
    computeLeaderboardWeekStartIso(Date.parse("2026-11-01T23:30:00.000Z")),
    "2026-10-26T09:00:00.000Z",
  );
});

test("missing DB binding or short salt refuses service with 500", async () => {
  const handler = createCloudflareLeaderboardHandler();
  const resNoSalt = await handler(new Request("https://lb.example.workers.dev"), {
    DB: {},
    PLAYER_HASH_SALT: "too-short",
  });
  assert.equal(resNoSalt.status, 500);

  const resNoDb = await handler(new Request("https://lb.example.workers.dev"), {
    PLAYER_HASH_SALT: TEST_SALT,
  });
  assert.equal(resNoDb.status, 500);
});

test("rate-limit database errors fail closed with 503", async () => {
  const brokenDb = {
    prepare() {
      return {
        bind() {
          return {};
        },
      };
    },
    async batch() {
      throw new Error("D1 offline");
    },
  };

  const handler = createCloudflareLeaderboardHandler();
  const env = { DB: brokenDb, PLAYER_HASH_SALT: TEST_SALT };

  const getRes = await handler(new Request("https://lb.example.workers.dev"), env);
  assert.equal(getRes.status, 503);

  const postRes = await handler(
    new Request("https://lb.example.workers.dev", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        runId: "run-1234567890123456",
        playerId: "player-123456789012",
        displayName: "BILLU_1234",
        score: "100",
        durationSeconds: 5,
        platform: "web",
      }),
    }),
    env,
  );
  assert.equal(postRes.status, 503);
});

test("CORS origin allowlist blocks unauthorized browser origins", async () => {
  const db = createD1SqliteMock();
  try {
    const handler = createCloudflareLeaderboardHandler();
    const env = {
      DB: db,
      PLAYER_HASH_SALT: TEST_SALT,
      ALLOWED_ORIGINS: "https://gr33nops.github.io,http://127.0.0.1:5099",
    };

    const blockedRes = await handler(
      new Request("https://lb.example.workers.dev", {
        headers: { Origin: "https://evil.example.com" },
      }),
      env,
    );
    assert.equal(blockedRes.status, 403);

    const allowedRes = await handler(
      new Request("https://lb.example.workers.dev", {
        headers: { Origin: "https://gr33nops.github.io" },
      }),
      env,
    );
    assert.equal(allowedRes.status, 200);
    assert.equal(
      allowedRes.headers.get("Access-Control-Allow-Origin"),
      "https://gr33nops.github.io",
    );
  } finally {
    db.close();
  }
});

test("end-to-end D1 SQLite submission, deduplication, best-score retention, and >Top-10 ranking", async () => {
  const db = createD1SqliteMock();
  try {
    let nowMs = Date.parse("2026-10-30T10:00:00.000Z");
    const handler = createCloudflareLeaderboardHandler(() => nowMs);
    const env = {
      DB: db,
      PLAYER_HASH_SALT: TEST_SALT,
      ALLOWED_ORIGINS: "*",
    };

    const submit = (payload, ip = "203.0.113.10") =>
      handler(
        new Request("https://lb.example.workers.dev", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "CF-Connecting-IP": ip,
          },
          body: JSON.stringify(payload),
        }),
        env,
      );

    // 1. Reject non-multiples of 10 and implausible catch rates
    const badMultiple = await submit({
      runId: "run-bad-multiple-0001",
      playerId: "player-alpha-00000001",
      displayName: "BILLU_2222",
      score: "105",
      durationSeconds: 10,
      platform: "web",
    });
    assert.equal(badMultiple.status, 400);

    const implausible = await submit({
      runId: "run-implausible-00001",
      playerId: "player-alpha-00000001",
      displayName: "BILLU_2222",
      score: "900000",
      durationSeconds: 1.0,
      platform: "web",
    });
    assert.equal(implausible.status, 400);

    // 2. Valid submission stores best score and returns rank #1
    const first = await submit({
      runId: "run-valid-alpha-00001",
      playerId: "player-alpha-00000001",
      displayName: "unsafe_free_text!",
      score: "1500",
      durationSeconds: 12,
      platform: "web",
    });
    assert.equal(first.status, 200);
    const firstJson = await first.json();
    assert.equal(firstJson.playerRank, 1);
    assert.equal(firstJson.entries.length, 1);
    assert.equal(firstJson.entries[0].score, "1500");
    assert.match(firstJson.entries[0].displayName, /^GUEST_[1-9][0-9]{3}$/);
    assert.equal(firstJson.entries[0].isCurrentPlayer, true);

    // 3. Replaying the exact same runId returns 409 Conflict
    const replay = await submit({
      runId: "run-valid-alpha-00001",
      playerId: "player-alpha-00000001",
      displayName: "BILLU_2222",
      score: "9000",
      durationSeconds: 30,
      platform: "web",
    });
    assert.equal(replay.status, 409);

    // 4. Lower score on a new run keeps the higher weekly personal best (1500)
    nowMs += 5_000;
    const lower = await submit({
      runId: "run-valid-alpha-00002",
      playerId: "player-alpha-00000001",
      displayName: "BILLU_2222",
      score: "400",
      durationSeconds: 8,
      platform: "web",
    });
    assert.equal(lower.status, 200);
    const lowerJson = await lower.json();
    assert.equal(lowerJson.entries[0].score, "1500");
    assert.equal(lowerJson.entries[0].displayName, firstJson.entries[0].displayName);

    // 5. Populate 11 higher-scoring players from distinct IPs and verify alpha moves to rank #12
    for (let i = 1; i <= 11; i++) {
      nowMs += 1_000;
      const pId = `player-competitor-${String(i).padStart(4, "0")}`;
      const rId = `run-competitor-00-${String(i).padStart(4, "0")}`;
      const res = await submit(
        {
          runId: rId,
          playerId: pId,
          displayName: `BILLU_${3000 + i}`,
          score: String(2000 + i * 100),
          durationSeconds: 20,
          platform: "web",
        },
        `198.51.100.${i}`,
      );
      assert.equal(res.status, 200);
    }

    const alphaCheck = await handler(
      new Request(
        "https://lb.example.workers.dev?limit=10&playerId=player-alpha-00000001&platform=web",
        { headers: { "CF-Connecting-IP": "203.0.113.99" } },
      ),
      env,
    );
    assert.equal(alphaCheck.status, 200);
    const alphaSnapshot = await alphaCheck.json();
    assert.equal(alphaSnapshot.entries.length, 10);
    assert.equal(alphaSnapshot.playerRank, 12);
    assert.equal(alphaSnapshot.entries[0].score, "3100");

    // 6. Advancing past Monday 09:00 UTC opens a fresh weekly board
    nowMs = Date.parse("2026-11-02T09:00:01.000Z");
    const nextWeekRes = await handler(
      new Request(
        "https://lb.example.workers.dev?limit=10&playerId=player-alpha-00000001&platform=web",
        { headers: { "CF-Connecting-IP": "203.0.113.99" } },
      ),
      env,
    );
    const nextWeekJson = await nextWeekRes.json();
    assert.equal(nextWeekJson.weekStart, "2026-11-02T09:00:00.000Z");
    assert.equal(nextWeekJson.entries.length, 0);
    assert.equal(nextWeekJson.playerRank, null);
  } finally {
    db.close();
  }
});

test("rate limiting enforces 8 writes/min and atomic batch rolls back failed SQL constraints", async () => {
  const db = createD1SqliteMock();
  try {
    // Verify SQLite atomic rollback on CHECK violation
    await assert.rejects(() =>
      db.batch([
        db.prepare(
          "INSERT INTO leaderboard_submitted_runs (run_id_hash, player_hash, score, created_at) VALUES (?, ?, ?, ?)",
        ).bind("r".repeat(64), "p".repeat(64), 100, "2026-10-30T10:00:00.000Z"),
        db.prepare(
          "INSERT INTO weekly_leaderboard_scores (week_start, player_hash, display_name, best_score, submitted_at, source_platform) VALUES (?, ?, ?, ?, ?, ?)",
        ).bind(
          "2026-10-26T09:00:00.000Z",
          "p".repeat(64),
          "BILLU_1234",
          100,
          "2026-10-30T10:00:00.000Z",
          "invalid_platform",
        ),
      ])
    );
    const leakedRuns = db.sqlite.prepare(
      "SELECT COUNT(*) AS n FROM leaderboard_submitted_runs",
    ).get();
    assert.equal(leakedRuns.n, 0);

    // Verify 9th write in the same minute returns 429 (MAX_WRITES_PER_MINUTE = 8)
    let nowMs = Date.parse("2026-10-30T11:00:10.000Z");
    const handler = createCloudflareLeaderboardHandler(() => nowMs);
    const env = { DB: db, PLAYER_HASH_SALT: TEST_SALT, ALLOWED_ORIGINS: "*" };

    for (let i = 1; i <= 9; i++) {
      const res = await handler(
        new Request("https://lb.example.workers.dev", {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "CF-Connecting-IP": "198.51.100.250",
          },
          body: JSON.stringify({
            runId: `run-ratelimit-test-${String(i).padStart(4, "0")}`,
            playerId: "player-ratelimit-0001",
            displayName: "BILLU_7777",
            score: String(i * 100),
            durationSeconds: 10,
            platform: "web",
          }),
        }),
        env,
      );
      assert.equal(res.status, i <= 8 ? 200 : 429);
    }

    // Next minute window resets write quota
    nowMs += 60_000;
    const afterWindowRes = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "CF-Connecting-IP": "198.51.100.250",
        },
        body: JSON.stringify({
          runId: "run-ratelimit-test-0010",
          playerId: "player-ratelimit-0001",
          displayName: "BILLU_7777",
          score: "1000",
          durationSeconds: 10,
          platform: "web",
        }),
      }),
      env,
    );
    assert.equal(afterWindowRes.status, 200);
  } finally {
    db.close();
  }
});

test("custom player name rename in-place, profanity filter, cross-device Save Code sync, and v1->v2 SQL migration", async () => {
  // 1. Verify 0002 migration preserves existing v1 weekly score (e.g. BILLU_9100 -> 15760)
  const v1Db = new DatabaseSync(":memory:");
  try {
    v1Db.exec(`
      CREATE TABLE weekly_leaderboard_scores (
        week_start TEXT NOT NULL,
        player_hash TEXT NOT NULL CHECK (length(player_hash) = 64),
        display_name TEXT NOT NULL CHECK (length(display_name) BETWEEN 3 AND 12 AND display_name NOT GLOB '*[^A-Z0-9_]*'),
        best_score INTEGER NOT NULL CHECK (best_score > 0 AND best_score <= 50000000 AND best_score % 10 = 0),
        submitted_at TEXT NOT NULL,
        source_platform TEXT NOT NULL CHECK (source_platform IN ('local', 'web', 'crazygames', 'poki', 'desktop')),
        PRIMARY KEY (week_start, player_hash)
      ) STRICT;
    `);
    v1Db.prepare(
      "INSERT INTO weekly_leaderboard_scores VALUES (?, ?, ?, ?, ?, ?)",
    ).run(
      "2026-10-26T09:00:00.000Z",
      "a".repeat(64),
      "BILLU_9100",
      15760,
      "2026-10-30T08:00:00.000Z",
      "web",
    );
    v1Db.exec(
      fs.readFileSync(
        "backend/cloudflare/migrations/0002_profiles_and_custom_names.sql",
        "utf8",
      ),
    );
    const preserved = v1Db.prepare(
      "SELECT display_name, best_score FROM weekly_leaderboard_scores",
    ).get();
    assert.equal(preserved.display_name, "BILLU_9100");
    assert.equal(preserved.best_score, 15760);

    // Confirm upgraded constraints accept 2-letter custom names and 'android'/'ios'
    v1Db.prepare(
      "INSERT INTO weekly_leaderboard_scores VALUES (?, ?, ?, ?, ?, ?)",
    ).run(
      "2026-10-26T09:00:00.000Z",
      "b".repeat(64),
      "AJ",
      500,
      "2026-10-30T08:05:00.000Z",
      "android",
    );
  } finally {
    v1Db.close();
  }

  // 2. End-to-end rename + cross-device Save Code restore
  const db = createD1SqliteMock();
  try {
    let nowMs = Date.parse("2026-10-30T09:30:00.000Z");
    const handler = createCloudflareLeaderboardHandler(() => nowMs);
    const env = { DB: db, PLAYER_HASH_SALT: TEST_SALT, ALLOWED_ORIGINS: "*" };

    const legacyDevicePlayerId = "9f8e7d6c5b4a39281706f5e4d3c2b1a0";
    const connectCode = "K7M9-P4X2-R8W3";

    // Device A plays first run before choosing a custom name
    const run1 = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.5" },
        body: JSON.stringify({
          runId: "run-device-a-first-0001",
          playerId: legacyDevicePlayerId,
          displayName: "BILLU_9100",
          score: "15760",
          durationSeconds: 35,
          platform: "web",
        }),
      }),
      env,
    );
    assert.equal(run1.status, 200);

    // Profane name is rejected with 400
    const badNameRes = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.5" },
        body: JSON.stringify({
          action: "save_profile",
          playerId: legacyDevicePlayerId,
          connectCode,
          displayName: "F_U_C_K",
          personalBest: "15760",
          platform: "web",
        }),
      }),
      env,
    );
    assert.equal(badNameRes.status, 400);

    // Valid custom name renames BILLU_9100 -> GREEN in-place on the live weekly board!
    const saveRes = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "203.0.113.5" },
        body: JSON.stringify({
          action: "save_profile",
          playerId: legacyDevicePlayerId,
          connectCode,
          displayName: "Green",
          personalBest: "15760",
          platform: "web",
        }),
      }),
      env,
    );
    assert.equal(saveRes.status, 200);
    const saveJson = await saveRes.json();
    assert.equal(saveJson.displayName, "GREEN");
    assert.equal(saveJson.entries.length, 1);
    assert.equal(saveJson.entries[0].displayName, "GREEN");
    assert.equal(saveJson.entries[0].score, "15760");

    // Unknown Save Code returns 404
    const missingRestore = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "198.51.100.77" },
        body: JSON.stringify({
          action: "restore_profile",
          connectCode: "ZZZZ-ZZZZ-ZZZZ",
          platform: "android",
        }),
      }),
      env,
    );
    assert.equal(missingRestore.status, 404);

    // Device B (e.g. Android phone) restores using K7M9-P4X2-R8W3
    const restoreRes = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "198.51.100.77" },
        body: JSON.stringify({
          action: "restore_profile",
          connectCode: "k7m9-p4x2-r8w3",
          platform: "android",
        }),
      }),
      env,
    );
    assert.equal(restoreRes.status, 200);
    const restored = await restoreRes.json();
    assert.equal(restored.found, true);
    assert.equal(restored.displayName, "GREEN");
    assert.equal(restored.personalBest, "15760");
    assert.equal(restored.playerRank, 1);

    // Device B beats the score using K7M9-P4X2-R8W3 -> updates the SAME single player row!
    nowMs += 10_000;
    const phoneRun = await handler(
      new Request("https://lb.example.workers.dev", {
        method: "POST",
        headers: { "Content-Type": "application/json", "CF-Connecting-IP": "198.51.100.77" },
        body: JSON.stringify({
          runId: "run-device-b-phone-0002",
          playerId: connectCode,
          displayName: "GREEN",
          score: "22500",
          durationSeconds: 45,
          platform: "android",
        }),
      }),
      env,
    );
    assert.equal(phoneRun.status, 200);
    const phoneJson = await phoneRun.json();
    assert.equal(phoneJson.entries.length, 1);
    assert.equal(phoneJson.entries[0].displayName, "GREEN");
    assert.equal(phoneJson.entries[0].score, "22500");
    assert.equal(phoneJson.personalBest, "22500");
  } finally {
    db.close();
  }
});

test("duplicate labels are distinct, guests are clear, and snapshots are never cached", async () => {
  const db = createD1SqliteMock();
  try {
    const stamp = "2026-10-30T12:00:00.000Z";
    const week = "2026-10-26T09:00:00.000Z";
    for (const [hash, name, score] of [["a", "GR33N", 2000], ["b", "GR33N", 1000], ["c", "BILLU_9100", 500]]) {
      db.sqlite.prepare("INSERT INTO weekly_leaderboard_scores VALUES (?, ?, ?, ?, ?, ?)")
        .run(week, hash.repeat(64), name, score, stamp, "web");
    }
    const handler = createCloudflareLeaderboardHandler(() => Date.parse(stamp));
    const response = await handler(new Request("https://lb.example/"), {DB:db, PLAYER_HASH_SALT:TEST_SALT});
    const snapshot = await response.json();
    assert.equal(new Set(snapshot.entries.map(e => e.displayName)).size, 3);
    assert(snapshot.entries[2].displayName.startsWith("GUEST"));
    assert.deepEqual(snapshot.entries.map(e => e.score), ["2000", "1000", "500"]);
    assert.equal(response.headers.get("Cache-Control"), "no-store");
    assert.equal(db.sqlite.prepare("SELECT COUNT(*) AS n FROM weekly_leaderboard_scores").get().n, 3);
  } finally { db.close(); }
});

test("a stale device cannot rename the cloud profile by submitting a run", async () => {
  const db = createD1SqliteMock();
  try {
    const handler = createCloudflareLeaderboardHandler(() => Date.parse("2026-10-30T12:00:00.000Z"));
    const env = {DB:db, PLAYER_HASH_SALT:TEST_SALT};
    const post = body => handler(new Request("https://lb.example/", {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}), env);
    await post({action:"save_profile",playerId:"PLAYERONE1234",displayName:"NEW_NAME",personalBest:"0"});
    const response = await post({playerId:"PLAYERONE1234",runId:"stale-device-run-0001",displayName:"OLD_NAME",score:"100",durationSeconds:10});
    assert.equal(response.status, 200);
    assert.equal((await response.json()).displayName, "NEW_NAME");
  } finally { db.close(); }
});

test("Save Code collisions fail explicitly and never connect the wrong profile", async () => {
  const db = createD1SqliteMock();
  try {
    const handler = createCloudflareLeaderboardHandler(() => Date.parse("2026-10-30T12:00:00.000Z"));
    const env = {DB:db, PLAYER_HASH_SALT:TEST_SALT};
    const post = body => handler(new Request("https://lb.example/", {method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(body)}), env);
    const first = await post({action:"save_profile",playerId:"LEGACYPLAYER111111",connectCode:"ABCD-EFGH-JKLM",displayName:"FIRST"});
    assert.equal(first.status, 200);
    const second = await post({action:"save_profile",playerId:"LEGACYPLAYER222222",connectCode:"ABCD-EFGH-JKLM",displayName:"SECOND"});
    assert.equal(second.status, 409);
    assert.equal((await second.json()).error, "SAVE CODE ALREADY IN USE");
    const restored = await post({action:"restore_profile",connectCode:"ABCD-EFGH-JKLM"});
    assert.equal((await restored.json()).displayName, "FIRST");
  } finally { db.close(); }
});

test("v1 raw UUID and v2 normalized UUID reconcile only with possession of that identity", async () => {
  const db = createD1SqliteMock();
  try {
    const raw = "9f8e7d6c-5b4a-3928-1706-f5e4d3c2b1a0";
    const normalized = raw.replaceAll("-", "").toUpperCase();
    const hash = async value => Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(`${TEST_SALT}:player:${value}`)))).map(x => x.toString(16).padStart(2,"0")).join("");
    const legacy = await hash(raw), modern = await hash(normalized);
    const stamp = "2026-10-30T12:00:00.000Z", week = "2026-10-26T09:00:00.000Z";
    for (const [id,name,score] of [[legacy,"BILLU_9100",9000],[modern,"GR33N",5000]]) {
      db.sqlite.prepare("INSERT INTO player_profiles (player_hash,display_name,all_time_best,source_platform,created_at,updated_at) VALUES (?,?,?,?,?,?)").run(id,name,score,"web",stamp,stamp);
      db.sqlite.prepare("INSERT INTO weekly_leaderboard_scores VALUES (?,?,?,?,?,?)").run(week,id,name,score,stamp,"web");
    }
    const handler = createCloudflareLeaderboardHandler(() => Date.parse(stamp));
    const env = {DB:db, PLAYER_HASH_SALT:TEST_SALT};
    const response = await handler(new Request(`https://lb.example/?playerId=${encodeURIComponent(raw)}`), env);
    assert.equal(response.status,200);
    const result = await response.json();
    assert.equal(result.entries.length,1);
    assert.equal(result.entries[0].displayName,"GR33N");
    assert.equal(result.entries[0].score,"9000");
    assert.equal(result.entries[0].isCurrentPlayer,true);
    assert.equal(result.personalBest,"9000");
    assert.equal(db.sqlite.prepare("SELECT COUNT(*) AS n FROM player_profiles").get().n,1);
    const repeat = await handler(new Request(`https://lb.example/?playerId=${normalized}`),env);
    assert.equal((await repeat.json()).entries.length,1);
  } finally {db.close();}
});
