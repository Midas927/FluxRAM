# FluxRAM

English | [简体中文](README.md)

FluxRAM is a local Windows memory Boost tool. It is not designed to aggressively clean memory all the time. Instead, it frees cold background working sets when you ask it to, or when memory pressure rises, while keeping protected apps such as games, creator tools, local AI workloads and streaming software out of the target list.

## Download

[Download the latest FluxRAM release](https://github.com/Midas927/FluxRAM/releases/latest)

Recommended package: `FluxRAM-Portable-Windows-x64.zip`. Unzip it and run `FluxRAM.exe`.

FluxRAM and FluxRAM Pro use the same executable. Pro features can only be activated with a key generated for the current machine ID.

Small builds are also available for users who want a smaller download. The Small build does not remove features, but it requires `.NET 8 Desktop Runtime` to be installed on the computer.

## Core Features

| Feature | FluxRAM | FluxRAM Pro |
| --- | --- | --- |
| Boost Now | Yes | Yes |
| Auto Boost when memory pressure rises | Yes | Yes |
| Light / Standard profiles | Yes | Yes |
| Extreme Performance profile | No | Yes |
| Tray resident mode and tray Boost | Yes | Yes |
| Add / remove protected apps | Yes | Yes |
| Pick protected apps from running processes | Yes | Yes |
| Process-name protection | Yes | Yes |
| Exact EXE path protection | No | Yes |
| Child-process association protection | No | Yes |
| Window recognition protection | No | Yes |

## FluxRAM

The free edition is meant to feel complete for everyday use:

1. `Boost Now`: run a memory Boost when you need it.
2. `Auto Boost`: trigger automatically when memory pressure becomes high.
3. `Light` / `Standard`: conservative profiles for gaming, office work, local AI and creator workloads.
4. Protected app list: add, remove, or pick apps directly from running processes.
5. Tray workflow: minimize to tray and run Boost from the tray menu.
6. Visible metrics: RAM delta, available memory, recent trimmed memory, total trimmed memory, net gain and FluxRAM's own overhead.

## FluxRAM Pro

Pro is for heavier local workloads and more precise protection needs:

1. `Extreme Performance` profile.
2. Exact EXE path protection to reduce false matches between processes with the same name.
3. Child-process association protection for launchers, game clients, local AI toolchains and creator apps.
4. Window recognition protection for apps with unstable process names or wrapper launchers.
5. Permanent activation on the current computer, with no online verification required after activation.

## Pro Activation

FluxRAM Pro can only be activated with a machine-bound key:

1. Open FluxRAM.
2. Copy the machine ID shown in the app.
3. Send the machine ID to sales or support.
4. Enter the Pro Key you receive inside FluxRAM.
5. The key is bound to the current computer. A new computer requires a new key.

There is no universal Pro installer and no public Pro key generation flow.

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
