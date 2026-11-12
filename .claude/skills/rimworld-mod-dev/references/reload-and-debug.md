# Reloading without restarting, and debugging

Defs and C# can both be changed in a running game, headless or windowed. Checked on 2026-11-12, with the full SillyAnimalThings suite passing on games changed both ways.

## Defs (XML): `ordo/hot_reload_defs`

Deploy the changed XML (`dotnet build ... -c Debug -t:CopyToRimworld`; the DLL can stay loaded), then call `ordo/hot_reload_defs`. It takes about 1.5 s and returns when the reload has finished. Check the change took, for example with `ordo/ability_def`.

- It runs the same reload as dev mode's `Actions\Hot reload Defs` (`PlayDataLoader.HotReloadDefs`), but also works headless, where the debug action hangs. A synchronous long event only runs once its loading screen has been drawn (`QueuedLongEvent.alreadyDisplayed`), and nothing is drawn headless, so the tool marks each event as drawn by reflection. `ordo/long_events` shows the loading queue.
- **The reload rebuilds every def from XML, so anything a mod adds to defs in code at startup is lost.** For example, SillyAnimalThings adds abilities and trainables to races. Such a mod must patch `PlayDataLoader.HotReloadDefs` with a Harmony postfix that queues its setup again with `LongEventHandler.QueueLongEvent`. Neither `LongEventHandler.ExecuteWhenFinished` (it runs before the reload has started) nor `LoadAllPlayData`'s flag (it means `recovering`, not a hot reload) works for this. See `SillyAnimalThings/src/SillyAnimalThings.cs`.

## C#: .NET hot reload through `tools/HotReloadDelta`

Waylith's runtime (`.NET 10.0.15-waylithclr`) accepts hot reload updates for a mod's Debug build with no setup: `MetadataUpdater.IsSupported` is true, and no `DOTNET_MODIFIABLE_ASSEMBLIES` or debugger is needed. `ordo/runtime_info` reports this. The game loads mods from their files into the default load context.

1. Deploy a Debug build the normal way, start the game, then run `tools/HotReloadDelta/hot_reload.ps1 -Project <Mod> -Reset`. That saves the exact `csc` command line and snapshots `src` as the baseline the game loaded. It warns when the deployed DLL isn't the one just built.
2. Edit the code, then run `hot_reload.ps1 -Project <Mod>`. It prints a folder; pass that to `ordo/apply_update` as `directory` (plus `mod` when several Ordo mods with code are loaded). Making the delta takes about 4 s and applying it a few milliseconds.
3. Repeat. Deltas chain: the script snapshots the source each delta applied, and the tool replays the chain to reach the game's current state. State lives in `tools/HotReloadDelta/state/<Mod>` (git-ignored).

- **Applies:** changed method bodies, new methods, new fields, and new types (whose static initializer runs on first use).
- **Needs a restart** (the script says so and writes nothing): removed methods, changed signatures, and new constructors or virtual methods on existing types. Removed types and fields are only noted, since they stay loaded harmlessly.
- **Not rerun:** static constructors, such as `[StaticConstructorOnStartup]` Harmony patching. A new or changed Harmony patch needs a restart.
- **Lost on restart:** the deployed DLL on disk is unchanged, so after a restart deploy a fresh build and `-Reset`.
- Bridge tool changes always need a restart.

How it works: `HotReloadDelta` does what `dotnet watch` does on the build side. It compiles the baseline and current source with Roslyn using the build's own arguments, finds changed and new members, and emits metadata, IL and PDB deltas with `EmitDifference`. Its baseline comes from the deployed DLL and its embedded PDB. `ordo/apply_update` calls `MetadataUpdater.ApplyUpdate` in the game.

Tools that don't work here: Reloader and Rimworld-Doorstop need Unity's Mono runtime or Doorstop, which Waylith doesn't have. Lib.QuickReload is deprecated and HotSwap archived. EditCompileReload would probably work, since it's plain .NET, but it supports fewer kinds of change than the runtime's own hot reload.

## Debugging in Rider: attach to the running game

The game ships the .NET debugging files (`mscordaccore.dll`, `mscordbi.dll`) next to `coreclr.dll`, and a Debug build embeds its PDB, so breakpoints map to the source with no setup.

- Start the game through GABS; `games_status` gives its PID as `gamePid`. In Rider, use Run > Attach to Process and pick **.NET Debugger**, not ".NET and Native" or LLDB. The user does this in Rider; Rider's MCP `xdebug_*` tools may also attach.
- Breakpoints in mod code stop the game, with locals, stepping, method-calling expressions and the call stack down through the game's own code. Rider reports "didn't find the associated module" until the mod's DLL loads, then binds the breakpoint.
- While stopped, the game's main thread is paused, so bridge calls hang until you resume. A scenario run started before the stop carries on afterwards.
- Stopping the debug session detaches; the game keeps running.
- **Not combinable with `ordo/apply_update`:** while a debugger is attached, the runtime refuses updates ("Assembly updates cannot be applied while a debugger is attached"). Detach first, or use Rider's own hot reload through the debugger (untested here).
