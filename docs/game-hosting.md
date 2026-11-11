# Game hosting

Play at **https://billus-bucket.moooviez.workers.dev/**. The GitHub Pages mirror is still available at https://gr33nops.github.io/Billus-Bucket/.

## Why there are two addresses

On 11 November 2026, a direct IPv4 request to GitHub Pages timed out after 15 seconds while IPv6 returned the page. Retesting individual GitHub IPv4 addresses also showed variable response times. This can leave a browser waiting before it even receives the HTML loading screen; reducing game download size cannot resolve that connection failure.

The Cloudflare host serves the same published game as static assets. It does not proxy game files from GitHub or need a game server to wake up. Missing files return 404 instead of HTML, which keeps broken runtime requests diagnosable.

The new host returned HTML over IPv4, and a fresh Chromium session explicitly pinned to IPv4 loaded the game, moved Billu, paused, and read the shared leaderboard without browser errors. These checks do not guarantee every network or browser will have identical response times.

## Update the Cloudflare game

GitHub Actions continues to publish the GitHub Pages mirror. Publish the Cloudflare copy after game updates with:

```powershell
# Uses existing Wrangler login; publishes the web game and uploads its static assets.
./tools/deploy_cloudflare_game.ps1

# Check configuration and packaging without deploying.
./tools/deploy_cloudflare_game.ps1 -DryRun
```

The script pins Wrangler 4.156.0 and uses `hosting/cloudflare/wrangler.jsonc`. Authentication remains in Wrangler's credential store. No account tokens are stored in source control. The output is generated under `dist/cloudflare/`.

The leaderboard stays in the existing `billus-bucket-leaderboard` Worker and its existing D1 database. Its allowed origins include both game addresses. A future hostname change must update that allowlist and redeploy the leaderboard configuration too.

## Existing profiles

Browsers keep local storage separate for each website. A name or best score saved only on the GitHub address does not automatically appear on the Cloudflare address. Use the leaderboard's Save Code to reconnect an existing cloud profile on the new site. All-time online rankings use the same database and never reset.

Cloudflare references: [static asset configuration](https://developers.cloudflare.com/workers/static-assets/binding/) and [static asset serving](https://developers.cloudflare.com/workers/static-assets/get-started/).
