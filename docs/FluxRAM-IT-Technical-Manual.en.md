# FluxRAM IT Technical Manual

Document version: 1.2
Updated: 2026-08-31
Audience: IT administrators, desktop engineering teams, endpoint security teams and maintainers

## 1. Technical Positioning

FluxRAM is a local Windows memory Boost tool built with C# / .NET 8 / WPF.

Core goals:

1. Reduce memory pressure by trimming cold background process working sets.
2. Provide `Daily`, `Gaming` and `Extreme` profiles.
3. Run locally without built-in cloud telemetry upload.
4. Support tray residency, manual Boost and memory-pressure-triggered Auto Boost.

Non-goals:

1. FluxRAM is not a kernel driver.
2. FluxRAM is not a long-term system service tuner.
3. FluxRAM does not replace enterprise endpoint management policy.
4. FluxRAM does not promise a fixed memory recovery ratio. Results depend on current system load and process state.

## 2. Repository Structure

```text
src/FluxRAM.App        WPF desktop app, UI, licensing, tray and runtime orchestration
src/FluxRAM.Core       memory snapshots, process enumeration, policy planning and Win32 calls
tests/FluxRAM.*        unit tests
scripts/               publishing and runtime installer scripts
docs/                  commercial and IT technical manuals
```

## 3. Permissions And Security Model

1. The main app requests `requireAdministrator` in `app.manifest`.
2. Elevation is required because working set trimming and access to some processes can fail under normal user permissions.
3. FluxRAM does not inject code into remote processes.
4. FluxRAM does not create startup entries by default.
5. Normal Boost flow does not call cloud APIs.
6. Update checks are user-initiated and query the public GitHub latest-release endpoint.
7. Local diagnostic logs are written to `%LOCALAPPDATA%\FluxRAM\fluxram.log`.
8. The runtime installer script only downloads the official .NET installer when `.NET 8 Desktop Runtime` is missing.

Licensing model:

1. FluxRAM Pro uses machine ID + RSA signature verification.
2. The app performs local verification after activation.
3. Pro Keys are generated from machine IDs and work only on the corresponding computers.

## 4. Editions And Feature Gates

| Feature | FluxRAM | FluxRAM Pro |
| --- | --- | --- |
| Daily / Gaming | Yes | Yes |
| Extreme | No | Yes |
| Deep Release and background service guidance | No | Yes |
| Boost Now | Yes | Yes |
| Auto Boost | Yes | Yes |
| Tray Boost | Yes | Yes |
| Add / remove protected apps | Yes | Yes |
| Pick protected apps from running processes | Yes | Yes |
| Process-name protection | Yes | Yes |
| Exact EXE path protection | No | Yes |
| Child-process association protection | No | Yes |
| Window recognition protection | No | Yes |

Public delivery uses a single `FluxRAM.exe`. The app starts as FluxRAM by default. Pro features require a Pro Key bound to the current machine ID and cannot be unlocked through a public build flag.

## 5. Boost Execution Flow

Triggers:

1. `Boost Now`: one user-triggered pass.
2. `Auto Boost`: lightweight monitoring triggers a pass only after the selected profile's memory pressure threshold and cooldown are met.
3. Tray `Boost Now`: one pass from the system tray menu.

Single-pass flow:

1. Read memory state with `GlobalMemoryStatusEx`.
2. Enumerate processes and sample CPU, window, foreground and memory information.
3. Generate candidates with `PurgePolicyService`.
4. Exclude system whitelist entries, foreground processes, cooldown processes and protected apps.
5. Exclude high-activity candidates by CPU and I/O hard gates before trimming.
6. Rank remaining candidates by coldness, working-set yield and activity risk.
7. Under severe memory pressure, expand the number of safe candidates instead of touching active apps.
8. Trim candidates with `SetProcessWorkingSetSize(-1, -1)` and `EmptyWorkingSet`.
9. Update recent trimmed memory, total trimmed memory, net gain, rebound rate, self overhead and recent events.

## 6. Current Policy Matrix

| Parameter | Daily | Gaming | Extreme |
| --- | ---: | ---: | ---: |
| MaxPurgeTargetsPerPass | 2 | 7 | 0, meaning all eligible candidates |
| MinimumCandidateWorkingSetBytes | 280 MB | 96 MB | 64 MB |
| PurgeWhenAvailableMemoryBelowBytes | 5 GB | 12 GB | 0 |
| PurgeWhenAvailableMemoryBelowPercentOfTotal | 26% | 48% | 0 |
| IgnoreMemoryPressureThreshold | false | false | true |
| AllowForegroundProcessPurge | false | false | true |
| ProcessCooldownSeconds | 60 | 18 | 0 |
| NormalIntervalSeconds | 8 | 4 | 1 |
| BackoffIntervalSeconds | 18 | 10 | 1 |
| LowYieldThresholdBytes | 96 MB | 24 MB | 0 |
| MinimumColdnessScore | 65 | 45 | 20 |
| BoostCooldownSeconds | 120 | 90 | 120 |
| MinimumGroupedProcessWorkingSetBytes | 24 MB | 8 MB | 4 MB |
| EnableGamingProcessProtection | false | true | false |
| EnablePriorityAdjustment | false | false | false |
| EnableServiceKiller | false | false | false |

Notes:

1. `Extreme` is available only in FluxRAM Pro.
2. Default profiles do not stop system services.
3. Manual Boost lowers candidate thresholds and shortens process cooldowns for Daily / Gaming while keeping foreground and protected-app exclusions.
4. `Extreme` is more aggressive and should be tested on controlled machines first.

## 7. Protected App Rules

Free edition protection:

1. Users can add protected apps from EXE files or running processes.
2. Users can remove protected entries from the list.
3. Runtime protection skips protected apps by process name.

Pro advanced protection:

1. Exact EXE path matching.
2. Child-process association detection.
3. Visible window title recognition.

The protected app list is stored under the common application data path and managed by `ProtectedAppsStore`.

## 8. Packaging And Distribution

Build requirements:

1. Windows 11.
2. .NET 8 SDK with Windows Desktop workload.

Lite build:

```powershell
.\scripts\publish-win-x64.ps1
```

Output:

```text
dist\fluxram-lite-win-x64\FluxRAM.exe
dist\release-assets\FluxRAM-Lite-Windows-x64.zip
dist\release-assets\FluxRAM-Lite-Windows-x64.zip.sha256
```

Lite mode is the default and requires `.NET 8 Desktop Runtime` on the target machine.

Portable build with runtime included:

```powershell
.\scripts\publish-win-x64.ps1 -Mode Portable
```

Output:

```text
dist\fluxram-win-x64\FluxRAM.exe
dist\release-assets\FluxRAM-Portable-Windows-x64.zip
dist\release-assets\FluxRAM-Portable-Windows-x64.zip.sha256
```

Portable is larger because it includes the desktop runtime, but it is easier for non-technical users.

Optional code signing:

```powershell
$env:FLUXRAM_SIGNTOOL_PATH = "C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe"
$env:FLUXRAM_SIGN_CERT_SHA1 = "<certificate thumbprint>"
.\scripts\publish-win-x64.ps1 -Mode Portable
```

When signing variables are configured, the script signs `FluxRAM.exe` before creating the release zip and SHA256 file.

## 9. Target Runtime

Lite packages can include:

```bat
scripts\install-dotnet-desktop-runtime-8.bat
scripts\run-fluxram.bat
```

Script behavior:

1. Detect `Microsoft.WindowsDesktop.App` 8.x.
2. Request UAC when needed.
3. Prefer an offline installer in the same folder.
4. When online, fetch the installer URL from official .NET release metadata.
5. Write installer logs to `%TEMP%\fluxram-dotnet-runtime-install.log`.

## 10. Licensing And Release Safety

1. Public download packages should be distributed through GitHub Release assets, not committed to the source tree.
2. Production releases should use the official licensing configuration.
3. Production executables should be code-signed before broad distribution.
4. Release assets should include the generated `.sha256` file.

## 11. Win32 API Surface

Memory and process APIs:

1. `OpenProcess`
2. `SetProcessWorkingSetSize`
3. `EmptyWorkingSet`
4. `GetProcessMemoryInfo`
5. `CloseHandle`
6. `GlobalMemoryStatusEx`
7. `GetForegroundWindow`
8. `GetWindowThreadProcessId`

Service-control code remains in the codebase, but default profiles do not enable service stopping.

## 12. Acceptance Checklist

1. `dotnet test FluxRAM.sln` passes.
2. Free edition can only select `Daily` and `Gaming`.
3. A valid Pro Key for the current machine ID unlocks `Extreme` and Deep Release.
4. Free edition can add and remove protected apps, including from running processes.
5. Pro advanced protection copy displays correctly.
6. `Boost Now` runs and refreshes metrics.
7. `Auto Boost` only triggers when pressure and cooldown conditions are met.
8. Minimize, close-to-tray, tray restore and tray exit work.
9. GitHub repository does not contain `dist`, `bin`, `obj`, `.secrets` or logs.
