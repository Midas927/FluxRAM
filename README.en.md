# FluxRAM

English | [简体中文](README.md)

FluxRAM is a local Windows memory Boost tool. When you run it manually or memory pressure rises, it evaluates and trims idle background working sets while excluding active and protected apps such as games, creator tools, local AI workloads and streaming software.

## Why FluxRAM Exists

On memory-constrained PCs, browsers, chat clients and launchers can gradually reduce the memory available to active work, causing slowdowns or application exits. FluxRAM focuses on one specific problem: reclaiming idle background memory while minimizing disruption to active applications.

FluxRAM cannot make 8GB behave like 16GB, and it does not promise a fixed amount of reclaimed memory. Results depend on the running applications, system load and protection settings. Memory usage increasing again when an application becomes active is normal.

## Download

Current stable release: **v0.4.2**.

[China mirror (GitCode)](https://gitcode.com/Midas927/FluxRAM/releases/download/v0.4.2/FluxRAM-Portable-Windows-x64.zip) | [Release notes](https://gitcode.com/Midas927/FluxRAM/releases/v0.4.2) | [GitHub fallback](https://github.com/Midas927/FluxRAM/releases/tag/v0.4.2)

Recommended package: `FluxRAM-Portable-Windows-x64.zip`. Unzip it and run `FluxRAM.exe`.

FluxRAM and FluxRAM Pro use the same executable. Pro features can only be activated with a key generated for the current machine ID.

`FluxRAM-Lite-Windows-x64.zip` is available as a smaller download with the same features. It requires `.NET 8 Desktop Runtime` to be installed. If you are unsure, use the Portable package.

## What's New In v0.4.2

- **Clearer interface**: sidebar icon navigation, a circular memory gauge and a two-minute memory trend. Long entries wrap, with selectable, copyable full details.
- **Startup update prompts**: choose Update now, Skip this version or Later. Skipped versions are remembered; Later prevents repeat prompts during the current session.
- **Background app search**: find apps by name or path and see why they qualify or are skipped. Pro Deep Release also supports search.
- **Per-app yield tracking**: inspect working-set changes at 30, 60 and 120 seconds after trimming during the current session. Outside severe memory pressure, Auto Boost temporarily defers repeated trims of apps with consistently low yield.
- **Clearer results**: available-memory changes and working-set reductions are shown separately, missing measurements are marked unknown, and skipped Auto Boost passes preserve the previous result.
- **More control over Pro Deep Release**: follow batch progress and cancel remaining actions. Force-closing an app that has not exited normally requires a separate confirmation. Cancellation does not reopen closed apps.

The free/Pro feature split is unchanged. Deep Release and Extreme remain Pro features. See the [v0.4.2 release notes (Chinese)](docs/releases/v0.4.2.md).

## Demo

These screenshots show FluxRAM running next to Windows Task Manager. Results vary by machine, workload and background process count, so treat them as a real-world interface and effect preview rather than a fixed promise.

<p>
  <img src="docs/assets/fluxram-demo-main.jpg" alt="FluxRAM compact UI with Windows Task Manager memory demo" width="900">
</p>

<p>
  <img src="docs/assets/fluxram-demo-details.jpg" alt="FluxRAM detailed UI with Windows Task Manager memory demo" width="900">
</p>

## Core Features

| Feature | FluxRAM | FluxRAM Pro |
| --- | --- | --- |
| Boost Now | Yes | Yes |
| Auto Boost when memory pressure rises | Yes | Yes |
| Daily / Gaming profiles | Yes | Yes |
| Extreme profile | No | Yes |
| Deep Release and background service guidance | No | Yes |
| Tray resident mode and tray Boost | Yes | Yes |
| Add / remove protected apps | Yes | Yes |
| Pick protected apps from running processes | Yes | Yes |
| Process-name protection | Yes | Yes |
| Exact EXE path protection | No | Yes |
| Child-process association protection | No | Yes |
| Window recognition protection | No | Yes |

## FluxRAM

The free edition includes the complete everyday Boost workflow:

1. `Boost Now`: run a memory Boost when you need it.
2. `Auto Boost`: trigger automatically when memory pressure becomes high.
3. `Daily` / `Gaming`: conservative profiles for office work, gaming, local AI and creator workloads.
4. Protected app list: add, remove, or pick apps directly from running processes.
5. Tray workflow: minimize to tray and run Boost from the tray menu.
6. Visible metrics: RAM delta, available memory, recent trimmed memory, total trimmed memory, net gain and FluxRAM's own overhead.

## FluxRAM Pro

Pro is for heavier local workloads and more precise protection needs:

1. `Extreme` profile.
2. Deep Release for grouped background processes, idle-state detection and user-confirmed closing.
3. Classification and stop guidance for optional background services.
4. Exact EXE path protection to reduce false matches between processes with the same name.
5. Smart child-process and related-app protection.
6. Permanent activation on the current computer, with no online verification required after activation.

## Pro Activation

FluxRAM Pro can only be activated with a machine-bound key:

1. Open FluxRAM.
2. Copy the machine ID shown in the app.
3. Submit the machine ID through the purchase channel.
4. Enter the Pro Key you receive inside FluxRAM.
5. The key is bound to the current computer. A new computer requires a new key.

Each Pro Key is generated from a machine ID and works only on the corresponding computer.

## Purchase

Click `Upgrade Pro` inside the edition help dialog:

- Simplified Chinese opens the in-app Alipay QR and payment steps for RMB 10.
- Other languages open the Whop purchase page for USD $3.

## Local And Safe

1. FluxRAM runs locally and does not rely on cloud telemetry.
2. Boost mainly trims cold background process working sets.
3. Default profiles do not stop system services.
4. Protected apps are excluded from Boost candidates to reduce accidental disruption.

## Documentation

- [Commercial Manual](docs/FluxRAM-Commercial-Manual.en.md)
- [商业说明书](docs/FluxRAM-Commercial-Manual.zh-CN.md)
- [IT Technical Manual](docs/FluxRAM-IT-Technical-Manual.en.md)
- [IT 技术说明书](docs/FluxRAM-IT-Technical-Manual.zh-CN.md)
