param(
    [string]$DatabaseName = "billus-bucket-leaderboard",
    [switch]$SkipGameConfig
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$cloudflareDir = Join-Path $repoRoot "backend\cloudflare"
$wranglerConfigPath = Join-Path $cloudflareDir "wrangler.jsonc"
$platformConfigPath = Join-Path $repoRoot "BillusBucket.Web\wwwroot\platform-config.js"

Push-Location $cloudflareDir
try {
    Write-Host "1/5 Checking Cloudflare authentication..."
    $null = npx --yes wrangler@4 whoami 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Opening browser to sign in to your free Cloudflare account..."
        npx --yes wrangler@4 login
        if ($LASTEXITCODE -ne 0) {
            throw "Cloudflare login was cancelled or failed."
        }
    }

    Write-Host "2/5 Finding or creating D1 SQLite database '$DatabaseName'..."
    $d1ListRaw = npx --yes wrangler@4 d1 list --json 2>$null | Out-String
    $d1List = if ([string]::IsNullOrWhiteSpace($d1ListRaw)) { @() } else { @($d1ListRaw | ConvertFrom-Json) }
    $existingDb = $d1List | Where-Object { $_.name -eq $DatabaseName } | Select-Object -First 1

    if (-not $existingDb) {
        npx --yes wrangler@4 d1 create $DatabaseName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create D1 database '$DatabaseName'."
        }
        $d1ListRaw = npx --yes wrangler@4 d1 list --json 2>$null | Out-String
        $d1List = @($d1ListRaw | ConvertFrom-Json)
        $existingDb = $d1List | Where-Object { $_.name -eq $DatabaseName } | Select-Object -First 1
    }

    if (-not $existingDb -or [string]::IsNullOrWhiteSpace($existingDb.uuid)) {
        throw "Could not resolve D1 database UUID for '$DatabaseName'."
    }

    $databaseId = $existingDb.uuid
    $wranglerObj = Get-Content -Path $wranglerConfigPath -Raw | ConvertFrom-Json
    $wranglerObj.d1_databases = @(
        [pscustomobject]@{
            binding       = "DB"
            database_name = $DatabaseName
            database_id   = $databaseId
        }
    )
    $wranglerObj | ConvertTo-Json -Depth 6 | Set-Content -Path $wranglerConfigPath -Encoding UTF8
    Write-Host "Bound D1 database '$DatabaseName' ($databaseId) in wrangler.jsonc."

    Write-Host "3/5 Applying weekly leaderboard SQLite schema to D1..."
    npx --yes wrangler@4 d1 execute $DatabaseName --remote --file=schema.sql --yes
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to apply schema.sql to remote D1 database."
    }

    # schema.sql creates new tables but cannot update constraints on existing ones.
    # Wrangler records applied migrations, so upgrades run exactly once.
    npx --yes wrangler@4 d1 migrations apply $DatabaseName --remote
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to apply D1 database migrations; Worker was not deployed."
    }

    Write-Host "4/5 Deploying Cloudflare Worker..."
    $deployOutput = npx --yes wrangler@4 deploy 2>&1 | Tee-Object -Variable deployLines | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Cloudflare Worker deployment failed."
    }

    Write-Host "5/5 Ensuring PLAYER_HASH_SALT secret is configured..."
    $secretsRaw = npx --yes wrangler@4 secret list --format json 2>$null | Out-String
    $secrets = if ([string]::IsNullOrWhiteSpace($secretsRaw)) { @() } else { @($secretsRaw | ConvertFrom-Json) }
    $hasSalt = $secrets | Where-Object { $_.name -eq "PLAYER_HASH_SALT" } | Select-Object -First 1

    if (-not $hasSalt) {
        $bytes = New-Object byte[] 32
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
        $randomSalt = -join ($bytes | ForEach-Object { $_.ToString("x2") })
        $randomSalt | npx --yes wrangler@4 secret put PLAYER_HASH_SALT
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to store PLAYER_HASH_SALT secret."
        }
        Write-Host "Generated and stored a 64-char secret PLAYER_HASH_SALT."
    } else {
        Write-Host "PLAYER_HASH_SALT secret already exists; keeping existing salt."
    }

    $urlMatch = [regex]::Match($deployOutput, 'https://[a-zA-Z0-9.-]+\.workers\.dev')
    if (-not $urlMatch.Success) {
        Write-Warning "Deployed Worker, but could not auto-detect *.workers.dev URL from output. Check your Cloudflare Dashboard for the URL."
        return
    }

    $workerUrl = $urlMatch.Value
    Write-Host ""
    Write-Host "Live Leaderboard URL: $workerUrl" -ForegroundColor Green

    if (-not $SkipGameConfig) {
        $cfg = Get-Content -Path $platformConfigPath -Raw
        $cfg = $cfg -replace 'sharedLeaderboardEnabled:\s*(true|false)', 'sharedLeaderboardEnabled: true'
        $cfg = $cfg -replace 'sharedLeaderboardEndpointUrl:\s*"[^"]*"', "sharedLeaderboardEndpointUrl: `"$workerUrl`""
        $cfg = $cfg -replace 'sharedLeaderboardPublicKey:\s*"[^"]*"', 'sharedLeaderboardPublicKey: ""'
        Set-Content -Path $platformConfigPath -Value $cfg -NoNewline -Encoding UTF8
        Write-Host "Updated BillusBucket.Web/wwwroot/platform-config.js with $workerUrl" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
