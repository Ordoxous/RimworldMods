# Driving the game: GABS, RimBridgeServer and the scenario suite

## The pieces

- **RimBridgeServer** (v2.3.0, in `RimWorld\Mods\RimBridgeServer`): a mod that exposes the running game as tools (`rimworld/*`, `rimbridge/*`) over GABP, and loads companion DLLs from `RimWorld\BridgeTools\*`. Ours is `tools/BridgeTools`, which adds `ordo/*`.
- **GABS** (v1.1.2, `~/.gabs/gabs.exe`, config `~/.gabs/config.json`): launches the game and mirrors the bridge's tools into MCP. It's registered as the local-scope MCP server `gabs`. Neither lives in the repo.
- Start with `games_status` or `games_list`, then `games_start` (game id `rimworld`, with a profile) and `games_connect`. For game tools, call `games_tool_names` with `brief: true`, inspect one with `games_tool_detail`, and call it with `games_call_tool`. If an `ordo/*` tool seems missing, reconnect or retry `games_tool_names` before assuming it is.
- `games_status` gives the game's PID as `gamePid`.

## Profiles

Each GABS profile is a set of launch arguments under `games.rimworld.profiles` in `~/.gabs/config.json`, with its own data folder under `~/.gabs/rimworld/<folder>` (via `-savedatafolder=`). That keeps the user's normal saves and mod list untouched. A data folder holds `Config/ModsConfig.xml` (the mod list), `Config/Prefs.xml` (dev mode, window, sound) and `Saves/autostart.rws`.

- **Dev mode must be on**, because the game only honours an `autostart` save in dev mode. Profiles open straight into `Saves/autostart.rws`.
- **`-Headless`** (a Waylith flag) runs with no window or rendering. Suites take seconds. Screenshots and recording don't work, nothing that draws is exercised, and `games_stop` fails because there's no window to signal, so stop with `games_kill`. A headless profile can share its windowed twin's data folder. `-nographics` and `-batchmode` do nothing on Waylith.
- Existing profiles: `sillyanimalthings`, `sillyanimalthings-biotech`, their `-headless` twins, `sillyanimalthings-profiling` (adds DPA) and `sillyanimalthings-showcase` (1080p, no music, runs in the background). To add profiles for another mod, see [new-mod-setup.md](new-mod-setup.md).
- Windows: a minimized window stops rendering, so the suite still passes but screenshots time out. Fullscreen windows minimize themselves on losing focus. Keep test windows at 720p, windowed.
- While Windows is locked, a windowed profile stalls at "Loading game from file autostart" with idle CPU and nothing listening, so `games_connect` times out. Screenshot the desktop with .NET `CopyFromScreen` to check for the lock screen, then use a headless profile or wait for the user.
- `games_start` sometimes fails with "Access is denied" renaming `runtime.json`. That leaves a 30 s "starting" lease with no game running: wait and retry.

## The test save

A 33x33 map of flat soil with no pawns, things, roofs or fog, at midday. It's just big enough for one 33-cell stage site, which every scenario shares in turn.

- An empty colony gets the Stranger in Black rescue event, so `ordo/run_scenarios` and `ordo/record_showcase` turn off the storyteller and remove every pawn before starting. That lasts until the save is next loaded. **Never save after a run.**
- To make a save: with no `autostart.rws` in the profile's `Saves`, start the profile (it stops at the main menu), then call `ordo/start_test_colony` (`mapSize` 33), `rimbridge/wait_for_long_event_idle`, `ordo/blank_map`, `ordo/set_hour` (12), and `rimworld/save_game` with **`saveName`** `autostart`. `save_game` ignores `name` and saves under a generated name.

## The `ordo/*` tools

| Tool | Use |
|---|---|
| `run_scenarios`, `list_scenarios` | The regression suite. `suite` (space or comma separated), `filter` (name contains), `mod`, `profile` |
| `spawn`, `list_pawns`, `pawn_state`, `things_at` | Spawn a pawn kind as wild, trained or manhunter, and read state |
| `select`, `cast`, `train`, `make_manhunter`, `damage`, `set_food` | Poke pawns by hand. Use `ordo/select`: `rimworld/click_cell` never selects on Waylith (the release uses the real cursor) |
| `ability_targets`, `ability_def` | Which pawns an area ability's checks accept, over 1000 rolls; an ability def's live values |
| `hot_reload_defs`, `apply_update`, `runtime_info`, `long_events` | Reloading without restarting; see [reload-and-debug.md](reload-and-debug.md) |
| `profiling_targets` | Every method of a mod, for DPA; see [profiling.md](profiling.md) |
| `record_showcase` | See [showcase.md](showcase.md) |
| `start_test_colony`, `blank_map`, `set_hour` | Making a test save |

Tools that act on one mod take `mod` (packageId, name, folder or assembly name). It can be left out when only one `Ordo.*` mod with code is loaded.

Useful built-in tools: `rimworld/*` debug actions, gizmos, tick stepping and screenshots all work on 1.7/Waylith. `list_logs` reads the game log.

## The scenario suite

`ordo/run_scenarios` runs the scenarios of every running mod listed in `tools/BridgeTools/Scenarios/ScenarioSets.cs`, or of `mod`. A scenario **fails** on a broken check, an exception, any error logged to the game log, or a dialog left open. Errors are counted by `LogMessage.repeats`, because the log merges repeated entries.

### Writing scenarios

One scenario per mechanism and per case that behaves differently, not one per item of content. Add a final scenario that checks content wiring (for example, that every tagged race got its abilities). Each scenario is `new("name", "suite1 suite2", Run, requiresAbility: null)` in the mod's `<Mod>Scenarios.All` list. Give each one suites: one per feature, plus cross-cutting ones such as `ai`, `player`, `visuals` and `setup`.

A scenario gets a `ScenarioContext` (a `Stage` in `tools/BridgeTools/Stage.cs`):

- `Spawn(kind, x, z, PawnMode.Wild | Tamed | Trained | Manhunter | Enemy)`, `Colonist(x, z)`, `Place(thingDef, x, z)` relative to the site's origin. Spawned pawns are always adults with no old injuries, because age changes body size, which changes results. `Enemy` animals flee, so stun them to use them as targets.
- `Cast(pawn, abilityDef, target)`, `Step(ticks)`, `await Until("description", () => condition, maxTicks)`, `Check("what went wrong", passed)`, `Fail(message)`.
- Helpers: `HasHediff`, `Hurt` (counts any injury or missing part, not only summary health), `HurtBelow`, `OnCooldown`, `Staggered`, `Terrified`, `Describe(pawn)` for failure messages.
- Cleanup is automatic: it waits for flyers to land, then destroys what the stage spawned and any fire, projectiles, corpses, explosions, items and filth on the site.

Practices that came from real flakes:

- Wait for effects with `Until` instead of checking straight away: explosions spread over several ticks, and spray projectiles land one after another.
- On failure, report the pawns' state (`Describe`) so a rare flake can be diagnosed from one run.
- Check visual effects with scenarios (a mote appeared and went), not by eye. Motes aren't in the map's normal thing lists, so expose the last one from the mod (as `HediffComp_Overlay.LastMote` does).
- Prove the suite catches bugs by planting faults on purpose and checking that exactly the expected scenarios fail.
- Bridge tool changes hot reload like a mod's (`hot_reload.ps1 -Project BridgeTools`, then `ordo/apply_update` with `mod: BridgeTools`). Restart for a new tool, changed tool parameters, or a new scenario or clip (see reload-and-debug.md).

## Pitfalls when driving the game

- **When `games_connect` times out, read `~/.gabs/rimworld/launch.log` first.** On 2026-11-13, a launch with the Steam client not running (`SteamAPI.Init() failed` in the log) failed to load Harmony's types: `ReflectionTypeLoadException ... RimBridgeServer: Could not load type 'HarmonyLib.CodeInstruction'`, and the mod's own Harmony patching failed too. The bridge never started. With Steam running, the same game files loaded fine. Make sure Steam is running before starting the game.
- The install is on Steam's `unstable` beta branch, so the game updates often. The test saves are from 1.7.5000 and load with a version warning, which is harmless.

- Bridge tool classes must be `public sealed class`, not `static class`, or the bridge finds no tools in them.
- Keep a mod's types out of tool method signatures and base classes in BridgeTools, so the DLL still loads in profiles without that mod. Use them only inside method bodies and scenario lambdas.
- A failed bridge call opens a blocking "attention" item: clear it with `games_ack_attention`.
- `list_logs` at `warning` is flooded by the bridge's `STARTUP_TIMING` lines: use `minimumLevel: error`.
- `screenshot_cell_rect` writes a full-window PNG to the game's `Screenshots` folder first, and can pick up a stale file with the same name. That gives an image of something else with no error, so delete the file before capturing. Each frame costs about 0.26 s whatever the window size.
- Windows paths ignore case: an output folder called `showcase` is the source folder `Showcase`.
- `claude` is at `~/.local/bin/claude.exe`, not on the tool shells' `PATH`.
