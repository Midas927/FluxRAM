param(
    [string]$Configuration = "Release",
    [ValidateSet("Lite", "Portable")]
    [string]$Mode = "Lite",
    [ValidateSet("Stable", "Beta")]
    [string]$Channel = "Stable"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FluxRAM.App\FluxRAM.App.csproj"
if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

$projectXml = [xml](Get-Content -Path $projectPath -Raw)
$version = [string]($projectXml.Project.PropertyGroup.Version | Select-Object -First 1)
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Unable to read the FluxRAM version from $projectPath"
}

$isBeta = $Channel -eq "Beta"
if ($isBeta -and -not $version.Contains("-")) {
    throw "Beta packaging requires a prerelease version. Current version: $version"
}

if (-not $isBeta -and $version.Contains("-")) {
    throw "Prerelease version $version must be packaged with -Channel Beta"
}

$outputFolderName = if ($Mode -eq "Portable") { "fluxram-win-x64" } else { "fluxram-lite-win-x64" }
$distributionRoot = if ($isBeta) { Join-Path $repoRoot "dist\beta" } else { Join-Path $repoRoot "dist" }
$outputPath = Join-Path $distributionRoot $outputFolderName
$releaseAssetsPath = Join-Path $distributionRoot "release-assets"
$releaseAssetName = if ($isBeta) {
    "FluxRAM-" + $version + "-" + $Mode + "-Windows-x64.zip"
} elseif ($Mode -eq "Portable") {
    "FluxRAM-Portable-Windows-x64.zip"
} else {
    "FluxRAM-Lite-Windows-x64.zip"
}
$releaseAssetPath = Join-Path $releaseAssetsPath $releaseAssetName
$releaseAssetHashPath = $releaseAssetPath + ".sha256"
$obsoleteOutputPaths = @(
    (Join-Path $repoRoot "dist\fluxram-small-win-x64"),
    (Join-Path $repoRoot "dist\fluxram-pro-win-x64"),
    (Join-Path $repoRoot "dist\free-win-x64"),
    (Join-Path $repoRoot "dist\pro-win-x64")
)
$isSelfContained = $Mode -eq "Portable"
$selfContainedValue = if ($isSelfContained) { "true" } else { "false" }
$enableCompressionInSingleFile = if ($isSelfContained) { "true" } else { "false" }

if (Test-Path $outputPath) {
    Remove-Item -Path $outputPath -Recurse -Force
}

foreach ($obsoleteOutputPath in $obsoleteOutputPaths) {
    if (Test-Path $obsoleteOutputPath) {
        try {
            Remove-Item -Path $obsoleteOutputPath -Recurse -Force
        } catch {
            Write-Warning ("Could not remove obsolete output folder because it may be in use: " + $obsoleteOutputPath)
        }
    }
}

Write-Host ("Publishing FluxRAM " + $version + " (" + $Channel + ", " + $Mode + ", single-file exe)...") -ForegroundColor Cyan

dotnet publish $projectPath `
    -c $Configuration `
    -f net8.0-windows `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$enableCompressionInSingleFile `
    -p:PublishTrimmed=false `
    -p:FluxRAMDistributionMode=$Mode `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$sourceExePath = Join-Path $outputPath "FluxRAM.App.exe"
$targetExePath = Join-Path $outputPath "FluxRAM.exe"

if (Test-Path $sourceExePath) {
    Move-Item -Path $sourceExePath -Destination $targetExePath -Force
    Write-Host ("Output ready: " + $targetExePath) -ForegroundColor Green
} else {
    Write-Warning "Publish succeeded but expected executable was not found at $sourceExePath"
}

$signToolPath = $env:FLUXRAM_SIGNTOOL_PATH
$signCertSha1 = $env:FLUXRAM_SIGN_CERT_SHA1
$timestampUrl = if ([string]::IsNullOrWhiteSpace($env:FLUXRAM_TIMESTAMP_URL)) { "http://timestamp.digicert.com" } else { $env:FLUXRAM_TIMESTAMP_URL }
if (-not [string]::IsNullOrWhiteSpace($signToolPath) -and -not [string]::IsNullOrWhiteSpace($signCertSha1)) {
    Write-Host "Signing FluxRAM.exe..." -ForegroundColor Cyan
    & $signToolPath sign /fd SHA256 /tr $timestampUrl /td SHA256 /sha1 $signCertSha1 $targetExePath
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed with exit code $LASTEXITCODE"
    }
    Write-Host "Executable signed." -ForegroundColor Green
} elseif (-not [string]::IsNullOrWhiteSpace($signToolPath) -or -not [string]::IsNullOrWhiteSpace($signCertSha1)) {
    Write-Warning "Code signing skipped because FLUXRAM_SIGNTOOL_PATH and FLUXRAM_SIGN_CERT_SHA1 must both be set."
} else {
    Write-Host "Code signing skipped: signing environment variables are not configured." -ForegroundColor Yellow
}

if (-not (Test-Path $releaseAssetsPath)) {
    New-Item -Path $releaseAssetsPath -ItemType Directory | Out-Null
}

if (Test-Path $releaseAssetPath) {
    Remove-Item -Path $releaseAssetPath -Force
}

if (Test-Path $releaseAssetHashPath) {
    Remove-Item -Path $releaseAssetHashPath -Force
}

Compress-Archive -Path (Join-Path $outputPath "*") -DestinationPath $releaseAssetPath -Force
$hash = (Get-FileHash -Path $releaseAssetPath -Algorithm SHA256).Hash.ToLowerInvariant()
($hash + "  " + $releaseAssetName) | Set-Content -Path $releaseAssetHashPath -Encoding ASCII

Write-Host ("Release asset ready: " + $releaseAssetPath) -ForegroundColor Green
Write-Host ("SHA256: " + $hash) -ForegroundColor Green
