# Nexus listing fields

Source text for the Nexus Mods page. The description lives in `description.bbcode`.

## Mod name
Kitsune Performance - Server CPU Fixes and Profiler

## Category
Utilities

## Version
0.2.0

## Brief overview
Server-side CPU fixes for 7 Days to Die (V2.6 and V3), plus kperf, a profiler that shows which mods eat your server. On The Asylum, one cosmetic mod was costing more CPU than SCore. Players don't need to install anything.

## Main file: KitsuneServerPerf
Server-side only. Unzip to Mods on the server and restart. V2.6 and V3.x, EAC off. Includes kperf, the vanilla fixes, the Asylum Optimized fixes and the optional AI throttle (off by default).

## Optional file: KitsuneServerPerf_UI (Settings Page)
In-game settings page for admins (Esc menu, Server Perf). The server sends its UI to every player, so if your server runs this, every player needs it too. Needs the main file. Works with or without KitsuneUI.

## Changelog 0.2.0
- Switch any fix on or off while the server runs: kperf settings, kperf set. Changes save to Settings.xml.
- kperf set group:mods switches all the Asylum Optimized fixes at once.
- New optional Settings Page add-on (Esc menu, Server Perf) for admins.
- Confirmed working on V2.6 (tested with Undead Legacy 2.7.40).

## Changelog 0.1.0
- kperf profiler: times the game's busy code and every mod's Harmony patches.
- Vanilla fixes: zombie network priority distance bug, cheaper line-of-sight checks.
- Asylum fixes: ShockShaderPatch, four SCore hot spots, CrystalHellHusbandry, AsylumHealFix, Zombie Random Buffs.
- Optional AI distance throttling, off by default.

## Requirements
None. The game's built-in 0_TFP_Harmony is all it needs. KitsuneUI is optional for the Settings Page add-on (it picks up KitsuneUI's look).

## Permissions (matches the CC BY-NC-SA 4.0 licence on GitHub)
- Upload: allowed, with credit; not for sale.
- Modification: allowed, with credit; changed versions under the same licence.
- Conversion: allowed, with credit; under the same licence.
- Asset use: allowed, with credit; under the same licence.
- Commercial use: not allowed. Running it on a server that takes donations or sells perks is fine.

## Images
1. kitsune-performance.jpg: main image. "Kitsune Performance"
2. docs/settings-page.png: "The optional in-game Settings Page: every fix with its state, and one switch for the whole Asylum Optimized group."
3. Take a screenshot of a kperf report from your own server (telnet or F1 console, after kperf start 60): "kperf on a busy server: where the CPU goes, per mod and per patch."

## Reply for the original r/7daystodie thread
Bit late, but I went and built one. It's called Kitsune Performance, server-side only, and it runs on Asylum 3.3. The biggest thing it found on Asylum was a cosmetic shader mod using more CPU than SCore every second. That's fixed, along with a few SCore hot spots and a vanilla netcode bug. It also has a kperf console command that shows which mods are actually costing you CPU on your own server, so you can see what's going on with yours. Nexus: [link] / GitHub: https://github.com/Kitsune-Den/KitsuneServerPerf
