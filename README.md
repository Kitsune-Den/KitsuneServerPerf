# KitsuneServerPerf

🦊 **Part of the [Kitsune Systems Collection](https://github.com/Kitsune-Den)**

**Server-side CPU fixes for 7 Days to Die, plus a profiler that tells you which mod is eating your server.**

Someone on r/7daystodie asked for server performance mods for Asylum 3.3 because it was hammering their CPU. The answers were "try a performance mod" without naming one. So we went and looked at where a 3.3 dedicated server actually spends its time, and at all ~80 code mods in the Asylum pack.

Some of it is just how many zombies are alive. Asylum's blood moon cap is 192 at every gamestage, and there are three extra horde systems on top of vanilla. No mod fixes that without changing the game.

But a lot of it isn't. It's code that runs every frame for every entity and does work nobody ever uses. That part we can fix.

## What it does

### `kperf`, the profiler

A console command (telnet, web console or F1). It times the game's hot methods **and every Harmony patch every other mod has installed**, for as long as you tell it to, then prints a report:

```
kperf start 60      time for 60 seconds, then print the report
kperf stop          stop early
kperf report        print the last report again
kperf census        what's alive right now and how fast its AI runs
```

The report shows ms of CPU per second for things like `World.TickEntities`, `EntityBuffs.Tick` and `EffectManager.GetValue`, then the same per mod, then the slowest individual mod patches. Reports are also saved to `<UserDataFolder>/KitsuneServerPerf/`.

Run it with players online and zombies around. An empty server pauses most of its work, so there's nothing to measure. It adds some overhead while it runs and removes every timing patch when it stops.

### Fixes that don't change gameplay

Each of these does exactly what the original code did, with less work. All on by default, each one can be switched off in `Settings.xml`.

**Vanilla**

- **Network priority bug.** `NetEntityDistribution.OnUpdateEntities` works out each zombie's distance to the nearest player as `x*x + z + z`. That's a typo for `x*x + z*z` (the player loop in the same method gets it right). Because of it, lots of zombies get marked "right next to a player" and their position goes out to clients every tick instead of every 3 to 10 ticks. Fixed with a transpiler that only applies if it finds the bug exactly once.
- **Line-of-sight checks.** `EntityAlive.CanEntityBeSeen` moves the zombie's whole model to the Ignore Raycast layer before every sight raycast and back after. That's every bone, twice per check. A raycast only hits colliders, so now only the colliders get moved. Dedicated servers only (on a host that's also rendering, the other layers matter).

**Mods in the Asylum pack** (these only switch on when that mod is loaded)

- **SCore**: on a dedicated server, the utility AI builds a multi-line debug label for every NPC on every AI update. Only the client HUD ever draws it. Skipped on dedicated.
- **SCore**: `Configuration.CheckFeatureStatus` re-scans the config block on every call, including once per zombie per frame. Results are now cached per world.
- **CrystalHellHusbandry**: its `EffectManager.GetValue` postfix runs on every stat lookup in the game and does string work before it checks which stat it's looking at. It only changes 6 or so stats. Now it only runs for those.
- **AsylumHealFix**: reads a field by reflection after every CVar change in the game to see if it's the one CVar it cares about. Now it checks directly first.
- **WMMZombieRandomBuffs + AsylumZRBCompat**: both run every frame on every entity, players and animals included, lower-casing the class name each time. Now they only run on entities they'd actually do something to.

None of these replace the other mod's code. Each one is a small filter in front of the original patch that says "skip it" only in the cases where it would have returned without doing anything anyway. The original patch keeps its place in the patch order.

### AI distance throttling (off by default, changes gameplay)

Vanilla already slows down AI for zombies that aren't among the closest few to a player: 0.3 within 15 m, 0.1 past that (1.0 means the AI thinks every tick). This adds slower tiers for zombies farther out:

```xml
<AiThrottle enabled="false" enemies="true" animals="false" npcs="false" skipWhenAlert="true" skipDuringBloodMoon="true">
	<Tier distance="40" scale="0.05" />
	<Tier distance="80" scale="0.025" />
</AiThrottle>
```

It never touches the zombies vanilla keeps at full speed, never raises a rate, and by default leaves alone anything with a target, anything investigating a noise, and everything during blood moon. Far zombies still move and animate every tick, they just make decisions less often. Which means they're slower to notice you from far away. That's the trade.

Turn it on, play a night, see if it feels off.

## Install

Server-side only. Clients don't need it.

1. Drop the `KitsuneServerPerf` folder into your server's `Mods/`
2. Restart. The log lists every fix and whether it's on, off or skipped:

```
[KitsuneServerPerf] NetPriorityDistance: on
[KitsuneServerPerf] SightCheckLayers: on
[KitsuneServerPerf] AI throttle is off (Settings.xml)
[KitsuneServerPerf] SCoreFeatureFlagCache: on
[KitsuneServerPerf] HusbandryGetValueFilter: skipped, CrystalHellHusbandry not loaded
```

Needs 7 Days to Die V3.x with the built-in `0_TFP_Harmony` mod. EAC has to be off, like any code mod. It doesn't ship its own Harmony.

## Settings

`Mods/KitsuneServerPerf/Settings.xml`, read once at startup. Every fix has an `enabled` switch. If a fix ever misbehaves on your server, switch it off and tell us.

## Build it yourself

You need .NET SDK 8+ and a copy of the game (or the dedicated server). Copy these from `7DaysToDieServer_Data/Managed/` into `libs/`:

```
Assembly-CSharp.dll
Assembly-CSharp-firstpass.dll
LogLibrary.dll
UnityEngine.dll
UnityEngine.CoreModule.dll
UnityEngine.PhysicsModule.dll
```

and `0Harmony.dll` from `Mods/0_TFP_Harmony/`. Then:

```
dotnet build -c Release
```

The mod folder is ready at `KitsuneServerPerf/`.

## What's next

The profiler is the point of v0.1. Real numbers from a busy Asylum server decide what gets fixed next. Candidates already spotted in the code: SCore's per-zombie `LateUpdate` hook, BiomeLevelScaling rebuilding a save path every frame, Spiders Can Climb's per-tick string checks, and the RAM affix patches on item stat lookups.

## License

MIT
