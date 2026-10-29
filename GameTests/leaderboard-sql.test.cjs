const { PGlite } = require("@electric-sql/pglite");
const fs = require("fs");
const assert = require("assert/strict");
const { test } = require("node:test");
test("leaderboard schema, rollback, deduplication, ranking and permissions", async () => {
  const db = new PGlite();
  try {
    await db.exec(
      "create role anon; create role authenticated; create role service_role;",
    );
    await db.exec(fs.readFileSync("backend/supabase/schema.sql", "utf8"));
    for (
      const [time, expected] of [
        ["2026-10-26T08:59:59Z", "2026-10-19T09:00:00.000Z"],
        ["2026-10-26T09:00:00Z", "2026-10-26T09:00:00.000Z"],
      ]
    ) {
      const result = await db.query(
        "select current_leaderboard_week_start($1::timestamptz) as start",
        [time],
      );
      assert.equal(result.rows[0].start.toISOString(), expected);
    }
    const player = "a".repeat(64), run = "b".repeat(64);
    try {
      await db.query("select submit_weekly_run($1,$2,$3,$4,$5)", [
        run,
        player,
        "BILLU_1234",
        100,
        "invalid",
      ]);
      assert.fail("must reject");
    } catch (e) {
      assert.equal(e.code, "23514");
    }
    assert.equal(
      (await db.query(
        "select count(*)::int as n from leaderboard_submitted_runs",
      )).rows[0].n,
      0,
    );
    await db.query("select submit_weekly_run($1,$2,$3,$4,$5)", [
      run,
      player,
      "BILLU_1234",
      100,
      "web",
    ]);
    try {
      await db.query("select submit_weekly_run($1,$2,$3,$4,$5)", [
        run,
        player,
        "BILLU_1234",
        100,
        "web",
      ]);
      assert.fail("must reject duplicate");
    } catch (e) {
      assert.equal(e.code, "23505");
    }
    const r = await db.query(
      "select get_weekly_leaderboard_snapshot($1,10) as s",
      [player],
    );
    assert.equal(r.rows[0].s.playerRank, 1);
    assert.equal(r.rows[0].s.entries[0].score, "100");
    await db.exec("set role anon");
    try {
      await db.query("select submit_weekly_run($1,$2,$3,$4,$5)", [
        run,
        player,
        "BILLU_1234",
        100,
        "web",
      ]);
      assert.fail("must reject public RPC");
    } catch (e) {
      assert.equal(e.code, "42501");
    }
  } finally {
    await db.close();
  }
});
