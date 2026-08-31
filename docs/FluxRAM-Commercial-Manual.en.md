# FluxRAM Commercial Manual

Document version: 1.0
Updated: 2026-08-31
Audience: product, sales, channel partners and customer decision makers

## 1. Product Positioning

FluxRAM is a local Windows memory Boost tool for high-memory-pressure workloads such as gaming, local AI, editing, design and development.

The product centers on safe, explainable Boost operations:

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

The free edition covers the complete everyday Boost workflow. Pro adds stronger, more precise and more controllable capabilities.

## 3. FluxRAM Free Edition

The free edition includes:

1. `Daily` and `Gaming` optimization profiles.
2. Manual `Boost Now`.
3. Memory-pressure-based `Auto Boost`.
4. Tray resident mode, tray restore, tray Boost and tray exit.
5. Add / remove protected apps.
6. Pick protected apps from currently running processes.
7. Basic protection by process name.
8. Metrics for recent trimmed memory, total trimmed memory, net gain, rebound rate and FluxRAM's own overhead.

The free edition is designed to cover the complete everyday Boost workflow.

## 4. FluxRAM Pro

Pro unlocks:

1. `Extreme` profile.
2. Deep Release with continuous background observation, grouped idle-app candidates and user-confirmed closing.
3. Classification and stop guidance for optional background services.
4. Exact EXE path protection to reduce false matches between processes with the same name.
5. Smart protection for child processes and related applications.
6. Permanent activation on the current computer with no online verification required after activation.

Pro retains the complete free workflow and adds stronger, more precise controls for advanced workloads.

## 5. Licensing Flow

FluxRAM shows a machine ID inside the app.

Commercial licensing flow:

1. The customer submits the machine ID through the purchase channel.
2. FluxRAM issues a Pro Key for that specific machine ID.
3. The customer enters the Pro Key in FluxRAM.
4. After successful verification, FluxRAM Pro is permanently activated on that computer.

Important notes:

1. Customers only need the FluxRAM release package and their Pro Key.
2. A Pro Key is bound to the current computer. A replacement computer requires a new key.

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

> FluxRAM is a local Windows memory Boost tool. The free edition includes manual Boost, Auto Boost, protected apps and tray controls. FluxRAM Pro adds Extreme, Deep Release and smart related-app protection for gaming, local AI, streaming and editing workloads.

For heavy users:

> FluxRAM Pro uses exact paths, child-process relationships and related-app detection to protect active workloads. Deep Release then presents idle background candidates for explicit confirmation.

For IT / business:

> FluxRAM runs locally, does not require cloud telemetry, and supports machine-bound licensing for controlled pilots and small deployments.

## 8. Delivery Guidance

For free users:

1. Prefer `FluxRAM-Portable-Windows-x64.zip`, which includes .NET 8 Desktop Runtime.
2. For a smaller download, use `FluxRAM-Lite-Windows-x64.zip`; the target PC must already have .NET 8 Desktop Runtime.

For paid users:

1. The same Portable or Lite package used for the free edition.
2. A Pro Key generated for the current machine ID.

Operations notes:

1. Pro Keys should be generated only through the published purchase and fulfillment channels.
2. Do not promise a universal cross-machine license.
