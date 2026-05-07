# FluxRAM

Windows 11 local-first memory Boost tool based on C#/.NET 8 + WPF.

## Solution Layout

- `src/FluxRAM.App`: high-privilege desktop shell (`Boost Now`, protected app list, Free/Pro activation, tray lifecycle, bilingual UI).
- `src/FluxRAM.Core`: native interop + core optimization services.
- `tests/FluxRAM.Core.Tests`: core unit tests.
- `tests/FluxRAM.App.Tests`: UI/ViewModel unit tests.

## Build Requirements

- .NET 8 SDK (Windows Desktop workload)
- .NET 8 Desktop Runtime (for default small publish mode)
- Windows 11 (for full Mica backdrop behavior)

## Run

```powershell
dotnet restore FluxRAM.sln
dotnet build FluxRAM.sln
dotnet test FluxRAM.sln
dotnet run --project .\src\FluxRAM.App\FluxRAM.App.csproj
```

## Product Editions

FluxRAM now ships as a runtime-gated product:

- `FluxRAM`: Light / Standard profiles, manual Boost, pressure-gated Auto Boost, protected app list, basic tray lifecycle and visible boost metrics.
- `FluxRAM Pro`: unlocks Extreme Performance and advanced target app protection.

FluxRAM shows a machine ID on launch. To activate FluxRAM Pro permanently on that computer:

```powershell
.\scripts\generate-pro-key.ps1 -MachineId "FLX-...."
```

The script requires either `-PrivateKeyXmlPath` or `FLUXRAM_LICENSE_PRIVATE_KEY_B64`. Generate a signing pair with:

```powershell
.\scripts\new-license-keypair.ps1
```

Keep the private key outside the repo. Replace the public key embedded in `src\FluxRAM.App\Licensing\LicenseKeyVerifier.cs` before production release.

There is also an internal GUI key generator:

```powershell
.\scripts\publish-keygen-win-x64.ps1
```

Output:

- `dist\keygen-win-x64\FluxRAM-Keygen.exe`

Put `fluxram-license.private-key.xml` next to the keygen exe, or choose the private key file from the UI. Do not send the keygen or private key to customers.

## Safe Optimization Policy (v3)

The product model is now boost-first instead of endless background trimming:

- Primary action is `Boost Now` (single boost cycle)
- Optional `Auto Boost` keeps a lightweight monitor running and triggers only when the current profile's memory-pressure threshold is reached
- Tray Boost is available from the system tray menu
- FluxRAM protected apps are excluded by process name; FluxRAM Pro adds exact path, child-process and window-title recognition
- Cooldown window after boost (default `120s`) avoids repeated disturbance
- Candidate selection uses `ColdnessScore` (CPU + foreground + window + recency + working set)
- `Protect List` excludes user-defined processes from trimming
- `ServiceKiller` is disabled in all default profiles

### Runtime Profiles

Profiles are now exposed as:

- `Light`: stricter candidate floor and higher coldness requirement
- `Standard`: balanced reclaim for common gaming / local AI loads
- `Extreme Performance` (Pro): threshold bypass and broad eligibility, still no service stop by default

### Main Metrics

UI now reports:

- `Last Boost Trimmed`
- `Total Trimmed`
- `Boost Net Gain`
- `Rebound Rate`
- FluxRAM self overhead (`CPU`, `Working Set`, `Private Bytes`, `Handle Count`)

### Protect List

FluxRAM protect list now supports:

- Add/remove through the UI, no comma-separated typing
- Adding from currently running applications
- Persistent storage under common application data

FluxRAM Pro adds advanced protection:

- Exact executable paths (for example `C:\Tools\OBS\obs64.exe`)
- Child-process association for helpers launched by protected apps
- Visible window-title recognition for launchers and wrapped apps

## Manuals

- `docs\FluxRAM-Commercial-Manual.zh-CN.md`: product positioning, edition strategy, sales notes and Mem Reduct comparison.
- `docs\FluxRAM-IT-Technical-Manual.zh-CN.md`: architecture, deployment, build, licensing and IT acceptance notes.

## Package as EXE

Single-file publish (recommended):

```powershell
.\scripts\publish-win-x64.ps1
```

Default mode is `Small` (framework-dependent single file, much smaller package size) and publishes both editions.
If you need a fully portable build that includes the runtime:

```powershell
.\scripts\publish-win-x64.ps1 -Mode Portable
```

To publish one edition:

```powershell
.\scripts\publish-win-x64.ps1 -Edition Free
.\scripts\publish-win-x64.ps1 -Edition Pro
```

Output:

- `dist\fluxram-win-x64\FluxRAM.exe`
- `dist\fluxram-pro-win-x64\FluxRAM-Pro.exe`

Direct command equivalent:

```powershell
dotnet publish .\src\FluxRAM.App\FluxRAM.App.csproj -c Release -f net8.0-windows -r win-x64 --self-contained false -p:FluxRAMEdition=Free -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o .\dist\fluxram-win-x64
```

## Install Runtime on Target PC

If you distribute the `Small` build, run this once on the target Windows machine:

```bat
scripts\install-dotnet-desktop-runtime-8.bat
```

This script checks for `.NET 8 Desktop Runtime`, and if missing, downloads the latest 8.0 x64 installer from official .NET release metadata and installs it silently.
It now auto-requests administrator privileges (UAC) and keeps the window open to avoid "flash close" on double-click.

If you run it from an existing terminal and don't want it to pause at the end:

```bat
scripts\install-dotnet-desktop-runtime-8.bat --no-pause
```

Or use one-click launcher (installs runtime if needed, then starts app):

```bat
scripts\run-fluxram.bat
```

### Recommended files to send to another PC

Put these in the same folder on the target machine:

- `FluxRAM.exe`
- or `FluxRAM-Pro.exe`
- `run-fluxram.bat`
- `install-dotnet-desktop-runtime-8.bat`
