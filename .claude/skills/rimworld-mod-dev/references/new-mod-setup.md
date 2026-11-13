# Setting up a mod for live testing

Everything in `tools/` works for any mod in the repo. A mod needs four things: GABS profiles, a test save, scenarios, and optionally a showcase.

## 1. GABS profiles

Copy an existing profile's data folder and change its mod list. The game must be stopped while you do this.

1. Copy `~/.gabs/rimworld/sillyanimalthings` to `~/.gabs/rimworld/<mod>`. That brings dev mode, a 720p window and the blank `Saves/autostart.rws`. Delete the `*_backup.rws` saves from the copy.
2. Edit `Config/ModsConfig.xml`. `activeMods` must list, in load order: `brrainz.harmony`, `ludeon.rimworld`, any DLCs the mod needs, `brrainz.rimbridgeserver`, the mod's dependencies, then the mod's own packageId in lower case. The `knownExpansions` list can stay as it is.
3. Add profiles under `games.rimworld.profiles` in `~/.gabs/config.json`, each with a short `description` saying what it loads:
   ```json
   "<mod>": { "description": "Harmony, Core, RimBridgeServer and <Mod>.", "args": ["-savedatafolder=C:\\Users\\Ben\\.gabs\\rimworld\\<mod>"] },
   "<mod>-headless": { "description": "As <mod>, with no window or rendering. Screenshots and recording don't work.", "args": ["-Headless", "-savedatafolder=C:\\Users\\Ben\\.gabs\\rimworld\\<mod>"] }
   ```
   Make another pair for each DLC combination that changes what's tested. GABS reads its config at startup, so a new profile may need GABS (the MCP server) restarting before `games_start` can see it.
4. Start the profile and check the mod list with `ordo/runtime_info` (for a mod with code) or by checking the log for errors.

If the copied save doesn't load with the new mod list, make a fresh one; see "The test save" in [live-testing.md](live-testing.md). A save from another mod list may not fail loudly: for MechUpgradesUi it stopped at the main menu with nothing in the log. Making the save from the main menu there worked.

## 2. Scenarios

1. Create `tools/BridgeTools/Scenarios/<Mod>Scenarios.cs`: a `public static class <Mod>Scenarios` with `public static readonly List<Scenario> All`. Model it on `SillyAnimalThingsScenarios.cs`.
2. Add a line to `ScenarioSets.All`: `("Ordo.<Mod>", () => <Mod>Scenarios.All)`.
3. If the scenarios use the mod's types, add `<ProjectReference Include="..\..\<Mod>\<Mod>.csproj" Private="false" />` to `tools/BridgeTools/BridgeTools.csproj`. `Private="false"` keeps the mod's DLL out of BridgeTools' output, because the game loads the mod itself.
4. Build and deploy BridgeTools (`-c Debug`, two commands), restart the game, and check that `ordo/list_scenarios` shows them.

Scenarios for a mod that isn't loaded never run, so one BridgeTools build serves every profile.

`Stage` and the scenario helpers came from an animal-abilities mod. They cover spawning pawns, things and colonists, casting, stepping ticks and checking health and hediffs. MechUpgradesUi added buildings, power, mechanitors and controlled mechs (see live-testing.md). For a different kind of mod, such as UI, work types or buildings, add general helpers to `Stage.cs` and mod-specific ones to the mod's scenario file. `Scenario.RequiresAbility` skips a scenario when an ability def isn't loaded (for DLC content); add a similar guard if you need one for another kind of def.

## 3. Showcase (optional)

1. Create `tools/BridgeTools/Showcase/<Mod>Showcase.cs` with `public static readonly List<ShowcaseClip> All`, and add a line to `ShowcaseSets.All`.
2. Add a `<mod>-showcase` profile: a copy of the data folder with a 1920x1080 window, `volumeMusic` 0 and `runInBackground` True in `Prefs.xml`.

Clips are captioned from an `AbilityDef`'s label and description. A mod without abilities needs another caption source in `ShowcaseRunner.WriteCaption`.

## 4. Hot reload, profiling, debugging

These need no setup per mod:

- `tools/HotReloadDelta/hot_reload.ps1 -Project <Mod> -Reset`, then `ordo/apply_update` with `mod` if several Ordo mods with code are loaded.
- `ordo/profiling_targets mod=<Mod>` for the method list. `ordo/run_scenarios profile=true` profiles every mod whose scenarios it runs. The mod needs a profile with DPA; see [profiling.md](profiling.md).
- `ordo/hot_reload_defs` reloads every def. If the mod adds anything to defs in code at startup, it must redo that after a reload; see [reload-and-debug.md](reload-and-debug.md).

## 5. Write it down

Give the mod a `HANDOFF.md` covering what it is, its current state, how to test it (profiles and suites), its in-game test results and its open questions. Update it as you go.
