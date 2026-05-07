param(
    [string]$Configuration = "Release",
    [ValidateSet("Small", "Portable")]
    [string]$Mode = "Small",
    [ValidateSet("All", "Free", "Pro")]
    [string]$Edition = "All"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\FluxRAM.App\FluxRAM.App.csproj"
$isSelfContained = $Mode -eq "Portable"
$selfContainedValue = if ($isSelfContained) { "true" } else { "false" }
$enableCompressionInSingleFile = if ($isSelfContained) { "true" } else { "false" }
$editions = if ($Edition -eq "All") { @("Free", "Pro") } else { @($Edition) }

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

foreach ($currentEdition in $editions) {
    $folderName = if ($currentEdition -eq "Free") { "fluxram-win-x64" } else { "fluxram-pro-win-x64" }
    $legacyFolderName = if ($currentEdition -eq "Free") { "free-win-x64" } else { "pro-win-x64" }
    $binaryName = if ($currentEdition -eq "Free") { "FluxRAM.exe" } else { "FluxRAM-Pro.exe" }
    $displayName = if ($currentEdition -eq "Free") { "FluxRAM" } else { "FluxRAM Pro" }
    $outputPath = Join-Path $repoRoot ("dist\" + $folderName)
    $legacyOutputPath = Join-Path $repoRoot ("dist\" + $legacyFolderName)

    if (Test-Path $outputPath) {
        Remove-Item -Path $outputPath -Recurse -Force
    }

    if (Test-Path $legacyOutputPath) {
        try {
            Remove-Item -Path $legacyOutputPath -Recurse -Force
        } catch {
            Write-Warning ("Could not remove legacy output folder because it may be in use: " + $legacyOutputPath)
        }
    }

    Write-Host ("Publishing " + $displayName + " (" + $Mode + " mode, single-file exe)...") -ForegroundColor Cyan

    dotnet publish $projectPath `
        -c $Configuration `
        -f net8.0-windows `
        -r win-x64 `
        --self-contained $selfContainedValue `
        -p:FluxRAMEdition=$currentEdition `
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
    $targetExePath = Join-Path $outputPath $binaryName

    if (Test-Path $sourceExePath) {
        Move-Item -Path $sourceExePath -Destination $targetExePath -Force
        Write-Host ("Output ready: " + $targetExePath) -ForegroundColor Green
    } else {
        Write-Warning "Publish succeeded but expected executable was not found at $sourceExePath"
    }
}
