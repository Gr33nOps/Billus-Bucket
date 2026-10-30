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
    assert.match(firstJson.entries[0].displayName, /^BILLU_[1-9][0-9]{3}$/);
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
    assert.equal(lowerJson.entries[0].displayName, "BILLU_2222");

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

test("rate limiting enforces 6 writes/min and atomic batch rolls back failed SQL constraints", async () => {
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

    // Verify 7th write in the same minute returns 429
    let nowMs = Date.parse("2026-10-30T11:00:10.000Z");
    const handler = createCloudflareLeaderboardHandler(() => nowMs);
    const env = { DB: db, PLAYER_HASH_SALT: TEST_SALT, ALLOWED_ORIGINS: "*" };

    for (let i = 1; i <= 7; i++) {
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
      assert.equal(res.status, i <= 6 ? 200 : 429);
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
          runId: "run-ratelimit-test-0008",
          playerId: "player-ratelimit-0001",
          displayName: "BILLU_7777",
          score: "800",
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
