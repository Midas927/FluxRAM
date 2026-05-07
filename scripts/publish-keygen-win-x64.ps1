param(
    [string]$Configuration = "Release",
    [ValidateSet("Small", "Portable")]
    [string]$Mode = "Small"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "tools\FluxRAM.Keygen\FluxRAM.Keygen.csproj"
$outputPath = Join-Path $repoRoot "dist\keygen-win-x64"
$isSelfContained = $Mode -eq "Portable"
$selfContainedValue = if ($isSelfContained) { "true" } else { "false" }
$enableCompressionInSingleFile = if ($isSelfContained) { "true" } else { "false" }

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

if (Test-Path $outputPath) {
    Remove-Item -Path $outputPath -Recurse -Force
}

Write-Host ("Publishing FluxRAM Keygen (" + $Mode + " mode, single-file exe)...") -ForegroundColor Cyan

dotnet publish $projectPath `
    -c $Configuration `
    -f net8.0-windows `
    -r win-x64 `
    --self-contained $selfContainedValue `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$enableCompressionInSingleFile `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$sourceExePath = Join-Path $outputPath "FluxRAM.Keygen.exe"
$targetExePath = Join-Path $outputPath "FluxRAM-Keygen.exe"

if (Test-Path $sourceExePath) {
    Move-Item -Path $sourceExePath -Destination $targetExePath -Force
    Write-Host ("Output ready: " + $targetExePath) -ForegroundColor Green
} else {
    Write-Warning "Publish succeeded but expected executable was not found at $sourceExePath"
}

$privateKeyPath = Join-Path $repoRoot ".secrets\fluxram-license.private-key.xml"
if (Test-Path $privateKeyPath) {
    Copy-Item `
        -Path $privateKeyPath `
        -Destination (Join-Path $outputPath "fluxram-license.private-key.xml") `
        -Force
    Write-Warning "Private key copied next to the keygen exe for local internal use. Do not distribute this folder to customers."
}
