param([switch]$SkipBuild, [switch]$DryRun)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$config = Join-Path $repoRoot 'hosting/cloudflare/wrangler.jsonc'

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'publish_web.ps1') -Platform web -OutputDir (Join-Path $repoRoot 'dist/cloudflare')
}

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'dist/cloudflare/index.html'))) {
    throw 'Game bundle not found. Run this script without -SkipBuild first.'
}

# Pin the CLI used to verify this deployment. Authentication stays in Wrangler's credential store.
$arguments = @('--yes', 'wrangler@4.156.0', 'deploy', '--config', $config)
if ($DryRun) { $arguments += '--dry-run' }
& npx @arguments
if ($LASTEXITCODE -ne 0) { throw 'Cloudflare game deployment failed.' }
