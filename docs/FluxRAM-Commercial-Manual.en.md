# FluxRAM Commercial Manual

Document version: 1.0
Updated: 2026-05-09
Audience: product, sales, channel partners and customer decision makers

## 1. Product Positioning

FluxRAM is a local Windows memory Boost tool for high-memory-pressure workloads such as gaming, local AI, editing, design and development.

The product is Boost-first:

1. Users can click `Boost Now` when they need immediate cleanup.
2. Users can enable `Auto Boost`, and FluxRAM will trigger only when memory pressure is high.
3. Protected apps are excluded from cleanup to reduce disruption to games, launchers, AI inference, streaming and creator tools.
4. Core optimization runs locally and does not depend on a cloud service.

## 2. Edition Strategy

FluxRAM follows a simple product model: the free edition should be useful enough for most people, while Pro solves advanced scenarios.

| Edition | Positioning | Best For |
| --- | --- | --- |
| FluxRAM | Free edition covering most daily workflows | gamers, local AI users, office users and light creators |
| FluxRAM Pro | Paid edition with stronger profiles and more precise protection | heavy gamers, streamers, editors, designers, local AI power users and IT-managed pilots |

The free edition should cover roughly 80%-90% of the everyday experience. Pro should add stronger, more stable and more precise controls instead of removing basic functionality from free users.

## 3. FluxRAM Free Edition

The free edition includes:

1. `Light` and `Standard` optimization profiles.
2. Manual `Boost Now`.
3. Memory-pressure-based `Auto Boost`.
4. Tray resident mode, tray restore, tray Boost and tray exit.
5. Add / remove protected apps.
6. Pick protected apps from currently running processes.
7. Basic protection by process name.
8. Metrics for recent trimmed memory, total trimmed memory, net gain, rebound rate and FluxRAM's own overhead.

The goal is to make the free edition comfortable for long-term daily use.

## 4. FluxRAM Pro

Pro unlocks:

1. `Extreme Performance` profile.
2. Exact EXE path protection to reduce false matches between processes with the same name.
3. Child-process association protection for launchers, game clients, AI toolchains and creator software.
4. Window recognition protection for wrapper launchers and apps with unstable process names.
5. Permanent activation on the current computer with no online verification required after activation.

Pro value is not about taking basic features away. It is about giving advanced users a stronger, safer and more precise Boost experience.

## 5. Licensing Flow

FluxRAM shows a machine ID inside the app.

Commercial licensing flow:

1. The customer sends the machine ID to sales or support.
2. The operator uses an internal licensing tool outside the public repository to generate a Pro key.
3. The customer enters the Pro key in FluxRAM.
4. After successful verification, FluxRAM Pro is permanently activated on that computer.

Important notes:

1. Private keys and internal licensing tools are for internal use only.
2. Customers should only receive the FluxRAM main executable and their Pro key.
3. A Pro key is bound to the current computer. A replacement computer requires a new key.

## 6. Comparison With Mem Reduct

Mem Reduct is a mature Windows memory cleanup tool with clear user expectations around monitoring and clearing cache / working sets.

FluxRAM differentiates around lower configuration cost and safer targeting:

| Dimension | Mem Reduct | FluxRAM |
| --- | --- | --- |
| Default experience | real-time monitoring and cleanup | manual Boost + pressure-triggered Auto Boost |
| Accidental disruption control | mostly user-configured cleanup areas | built-in whitelist, foreground protection, cooldowns and protected app list |
| Observability | mainly memory change | trimmed memory, net gain, rebound rate and self overhead |
| Protected apps | not the central product story | free protected app list; Pro advanced protection |
| Monetization | open-source free tool | free edition with complete daily workflow; Pro for advanced users |

FluxRAM should feel easier: no manual process-name typing, one-click protected app selection, one-click Boost and automatic handling when pressure rises.

## 7. Sales Messaging

Short version:

> FluxRAM is a local Windows memory Boost tool. The free edition already includes manual Boost, Auto Boost, protected apps and tray workflow. FluxRAM Pro unlocks Extreme Performance and advanced app protection for gaming, local AI, streaming and editing workloads.

For heavy users:

> FluxRAM Pro is not only about freeing memory. It tries to know which apps should not be touched. Path protection, child-process association and window recognition let Boost become more aggressive while staying more controlled.

For IT / business:

> FluxRAM runs locally, does not require cloud telemetry, and supports machine-bound licensing for controlled pilots and small deployments.

## 8. Delivery Guidance

For free users:

1. `FluxRAM.exe`
2. If using the Small build, include `run-fluxram.bat` and `install-dotnet-desktop-runtime-8.bat`

For paid users:

1. `FluxRAM.exe`
2. Pro key
3. If using the Small build, include the runtime helper scripts

Internal only:

1. Internal licensing tool
2. `fluxram-license.private-key.xml`
3. Licensing tools and private keys must not enter the public GitHub repository and must not be sent to customers
