import { createLeaderboardHandler } from "./index.ts";
function assert(value: boolean, message: string) {
  if (!value) throw new Error(message);
}
Deno.env.set("SUPABASE_URL", "https://example.supabase.co");
Deno.env.set("SUPABASE_SERVICE_ROLE_KEY", "test-only");
Deno.env.set("PLAYER_HASH_SALT", "test-only-salt-at-least-32-characters");
const post = () =>
  new Request("https://example.test", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      runId: "run-1234567890123456",
      playerId: "player-123456789012",
      score: "100",
      durationSeconds: 10,
    }),
  });
Deno.test("database rate-limit errors fail closed", async () => {
  const handler = createLeaderboardHandler(() =>
    ({
      rpc: () => Promise.resolve({ data: null, error: { message: "offline" } }),
    }) as any
  );
  for (const request of [new Request("https://example.test"), post()]) {
    const response = await handler(request);
    assert(response.status === 503, `expected 503, got ${response.status}`);
  }
});
Deno.test("score and run ID are saved through one atomic RPC", async () => {
  const calls: string[] = [];
  const handler = createLeaderboardHandler(() =>
    ({
      rpc: (name: string) => {
        calls.push(name);
        return Promise.resolve({
          data: name === "check_leaderboard_rate_limit"
            ? true
            : { entries: [] },
          error: null,
        });
      },
    }) as any
  );
  const response = await handler(post());
  assert(response.status === 200, `expected success, got ${response.status}`);
  assert(
    calls.includes("submit_weekly_run"),
    "atomic submission RPC was not used",
  );
});
Deno.test("missing player salt refuses service", async () => {
  const salt = Deno.env.get("PLAYER_HASH_SALT")!;
  Deno.env.delete("PLAYER_HASH_SALT");
  try {
    const handler = createLeaderboardHandler(() => {
      throw new Error("must not reach DB");
    });
    assert(
      (await handler(new Request("https://example.test"))).status === 500,
      "must reject missing configuration",
    );
  } finally {
    Deno.env.set("PLAYER_HASH_SALT", salt);
  }
});
