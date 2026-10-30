# Cloudflare Workers + D1 Weekly Leaderboard Backend

Free, **never-pausing** serverless SQLite leaderboard backend for **Billu's Bucket** (100,000 requests/day + 5 GB D1 storage on Cloudflare's Free plan, no credit card required, and never auto-pauses when idle).

---

## Option A — 1-Command Setup from Terminal (Recommended)

From the repository root in PowerShell:

```powershell
pwsh ./tools/deploy_cloudflare_leaderboard.ps1
```

What this script does automatically:
1. Opens your browser once to log into your free Cloudflare account (`wrangler login`).
2. Creates the `billus-bucket-leaderboard` D1 SQLite database and binds its UUID in `wrangler.jsonc`.
3. Runs `schema.sql` against the remote D1 database.
4. Deploys `src/index.mjs` and generates a secure 64-character `PLAYER_HASH_SALT` secret (never stored in git or client files).
5. Writes your live `https://billus-bucket-leaderboard.<your-subdomain>.workers.dev` URL into `BillusBucket.Web/wwwroot/platform-config.js`.

After running it, commit and push `BillusBucket.Web/wwwroot/platform-config.js` (and `backend/cloudflare/wrangler.jsonc`) to `main` so GitHub Actions updates `https://gr33nops.github.io/Billus-Bucket/`.

---

## Option B — 100% Browser Setup (Cloudflare Dashboard, No Terminal)

1. Sign in at https://dash.cloudflare.com (Free plan, no credit card needed).
2. **Create the D1 Database**:
   - In the left sidebar, open **Storage & Databases → D1 SQL Database → Create Database**.
   - Name it **`billus-bucket-leaderboard`** and click **Create**.
   - Open the **Console** tab inside your new D1 database, paste the entire contents of `backend/cloudflare/schema.sql`, and click **Execute**.
3. **Create the Worker**:
   - In the left sidebar, open **Compute (Workers) → Workers & Pages → Create → Start with Hello World!**.
   - Name it **`billus-bucket-leaderboard`** and click **Deploy**.
   - Click **Edit code**, replace `worker.js` with the entire contents of `backend/cloudflare/src/index.mjs`, and click **Deploy** (top right).
4. **Connect D1 & Secrets to the Worker**:
   - Open your Worker's **Settings** tab:
     - Under **Bindings → Add → D1 database**: set Variable name to **`DB`** and select **`billus-bucket-leaderboard`**, then click **Deploy**.
     - Under **Variables and Secrets → Add**:
       - Type **Secret**, Name **`PLAYER_HASH_SALT`**, Value: any random 32+ character string.
       - Type **Text**, Name **`ALLOWED_ORIGINS`**, Value: `https://gr33nops.github.io,http://127.0.0.1:5099,http://localhost:5099`
5. **Connect the Game**:
   - Copy your Worker URL (e.g. `https://billus-bucket-leaderboard.<your-subdomain>.workers.dev`) into `BillusBucket.Web/wwwroot/platform-config.js`:
     ```javascript
     sharedLeaderboardEnabled: true,
     sharedLeaderboardEndpointUrl: "https://billus-bucket-leaderboard.<your-subdomain>.workers.dev",
     sharedLeaderboardPublicKey: ""
     ```
   - Commit and push to `main` to publish to GitHub Pages.
