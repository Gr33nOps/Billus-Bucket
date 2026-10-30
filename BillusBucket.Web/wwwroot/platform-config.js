// Runtime platform & leaderboard configuration for Billu's Bucket (WebAssembly).
// tools/publish_web.ps1 generates portal-specific copies of this file for CrazyGames, Poki, and Local/Web.
window.BILLU_PLATFORM_CONFIG = Object.assign({
    // "local" | "web" | "crazygames" | "poki"
    activePlatform: "local",

    // CrazyGames native weekly leaderboard:
    // Keep FALSE until CrazyGames grants leaderboard access to your game and provides your base64 AES-GCM key.
    // Never place a server-side REST X-API-Key here; client builds only use the portal's score encryption key.
    crazyGamesLeaderboardEnabled: false,
    crazyGamesEncryptionKeyBase64: "",

    // Poki External Resources Policy:
    // Keep FALSE unless Poki has explicitly approved sharedLeaderboardEndpointUrl in
    // Poki for Developers -> Settings -> CSP alongside your game's Privacy Policy.
    pokiApprovedExternalLeaderboardEnabled: false,

    // Optional shared weekly Top-10 leaderboard backend:
    // - Cloudflare Worker + D1 (recommended): set sharedLeaderboardEndpointUrl to your *.workers.dev URL and leave sharedLeaderboardPublicKey as "".
    // - Supabase Edge Function: set sharedLeaderboardEndpointUrl and public anon key (never service_role).
    sharedLeaderboardEnabled: true,
    sharedLeaderboardEndpointUrl: "https://billus-bucket-leaderboard.moooviez.workers.dev",
    sharedLeaderboardPublicKey: ""
}, window.BILLU_PLATFORM_CONFIG || {});
