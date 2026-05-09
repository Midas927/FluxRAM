param(
    [string]$Configuration = "Release",
    [ValidateSet("Small", "Portable")]
    [string]$Mode = "Small"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FluxRAM.App\FluxRAM.App.csproj"
$outputFolderName = if ($Mode -eq "Portable") { "fluxram-win-x64" } else { "fluxram-small-win-x64" }
$outputPath = Join-Path $repoRoot ("dist\" + $outputFolderName)
$obsoleteOutputPaths = @(
    (Join-Path $repoRoot "dist\fluxram-pro-win-x64"),
    (Join-Path $repoRoot "dist\free-win-x64"),
    (Join-Path $repoRoot "dist\pro-win-x64")
)
$isSelfContained = $Mode -eq "Portable"
$selfContainedValue = if ($isSelfContained) { "true" } else { "false" }
$enableCompressionInSingleFile = if ($isSelfContained) { "true" } else { "false" }

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

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

Write-Host ("Publishing FluxRAM (" + $Mode + " mode, single-file exe)...") -ForegroundColor Cyan

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

$sourceExePath = Join-Path $outputPath "FluxRAM.App.exe"
$targetExePath = Join-Path $outputPath "FluxRAM.exe"

if (Test-Path $sourceExePath) {
    Move-Item -Path $sourceExePath -Destination $targetExePath -Force
    Write-Host ("Output ready: " + $targetExePath) -ForegroundColor Green
} else {
    Write-Warning "Publish succeeded but expected executable was not found at $sourceExePath"
}
