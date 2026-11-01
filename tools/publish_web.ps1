param(
    [ValidateSet("local", "web", "crazygames", "poki")]
    [string]$Platform = "local",

    [string]$OutputDir = "",

    [switch]$CreateZip
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$webProject = Join-Path $repoRoot "BillusBucket.Web\BillusBucket.Web.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "dist\$Platform"
}

$publishTemp = Join-Path $repoRoot "dist\_publish_tmp_$Platform"
# Resolve before deleting anything; generated bundles must remain inside this repo's dist.
$distRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "dist"))
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$publishTemp = [IO.Path]::GetFullPath($publishTemp)
foreach ($target in @($OutputDir, $publishTemp)) {
    if (-not $target.StartsWith($distRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace a directory outside $distRoot : $target"
    }
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to replace a linked output directory: $target"
        }
    }
}
if ($OutputDir -eq $publishTemp -or $OutputDir.StartsWith($publishTemp + [IO.Path]::DirectorySeparatorChar) -or $publishTemp.StartsWith($OutputDir + [IO.Path]::DirectorySeparatorChar)) {
    throw "Output and temporary publish directories must be separate."
}

if (Test-Path $publishTemp) {
    Remove-Item -Recurse -Force $publishTemp
}
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}

Write-Host "Publishing BillusBucket.Web (Release) for platform '$Platform'..."
# An absolute PublishDir containing an apostrophe breaks MSBuild item transforms.
# Keep this property relative to the project; the validated output remains under dist.
$relativePublishDir = [IO.Path]::GetRelativePath((Split-Path -Parent $webProject), $publishTemp).Replace('\', '/') + '/'
dotnet publish $webProject -c Release "-p:PublishDir=$relativePublishDir"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedWwwroot = Join-Path $publishTemp "wwwroot"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -Path (Join-Path $publishedWwwroot "*") -Destination $OutputDir -Recurse -Force
New-Item -ItemType File -Force -Path (Join-Path $OutputDir ".nojekyll") | Out-Null
Remove-Item -Recurse -Force $publishTemp

# Remove precompressed .br / .gz duplicates so portal ZIP bundles stay compact
# (CrazyGames, Poki, and modern static hosts compress assets at the CDN edge).
Get-ChildItem -Path $OutputDir -Recurse -Include *.br, *.gz | Remove-Item -Force

# Inject ONLY the target portal's official SDK script into index.html (strict CSP compliance).
$indexHtmlPath = Join-Path $OutputDir "index.html"
$indexHtml = Get-Content -Path $indexHtmlPath -Raw
$safeRepoRoot = $repoRoot.Replace('\', '/')
$buildRevision = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (git -c "safe.directory=$safeRepoRoot" -C $repoRoot rev-parse --short HEAD).Trim() }
if ($buildRevision -notmatch '^[0-9a-f]{7,40}$') {
    throw "Could not determine a safe asset revision for this build."
}

$sdkTag = switch ($Platform) {
    "crazygames" { '<script src="https://sdk.crazygames.com/crazygames-sdk-v3.js"></script>' }
    "poki"       { '<script src="https://game-cdn.poki.com/scripts/v2/poki-sdk.js"></script>' }
    default      { '' }
}

$indexHtml = $indexHtml.Replace("<!-- PORTAL_SDK_SCRIPT_SLOT -->", $sdkTag)
$indexHtml = $indexHtml.Replace("__BILLU_ASSET_VERSION__", $buildRevision)
Set-Content -Path $indexHtmlPath -Value $indexHtml -NoNewline -Encoding UTF8

# Set activePlatform in platform-config.js
$configPath = Join-Path $OutputDir "platform-config.js"
$configJs = Get-Content -Path $configPath -Raw
$configJs = $configJs -replace 'activePlatform:\s*"[^"]*"', "activePlatform: `"$Platform`""
Set-Content -Path $configPath -Value $configJs -NoNewline -Encoding UTF8

Write-Host "Static browser bundle ready at: $OutputDir"

if ($CreateZip) {
    $zipPath = Join-Path (Split-Path -Parent $OutputDir) "billus-bucket-$Platform.zip"
    if (Test-Path $zipPath) {
        Remove-Item -Force $zipPath
    }
    Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $zipPath -Force
    Write-Host "Portal upload archive created at: $zipPath"
}
