---
name: rimworld-mod-dev
description: How to build, deploy, test, reload, debug, profile and record RimWorld mods in this repo (RimworldMods) — running the game through the gabs MCP server and RimBridgeServer, the ordo/* bridge tools, the in-game scenario suite, def and C# hot reload, and the pitfalls of RimWorld 1.7 on Waylith. Use when building or deploying any mod here, starting the game, testing a change in game, writing scenarios, setting up testing for a new mod, reloading without a restart, attaching a debugger, profiling with Dubs Performance Analyzer, or recording a showcase video.
---

# RimWorld mod development in this repo

Each mod is a folder at the repo root (`SillyAnimalThings`, `Tweaks`, `MechUpgradesUi`, ...) holding `<Mod>.csproj`, C# under `src/` and the mod's files (About, Defs, Patches, Languages, textures) under `mod/`. The project name, assembly name and deployed folder under `RimWorld\Mods` are all the same. `tools/` holds the shared test tooling:

| Path | What it is |
|---|---|
| `tools/BridgeTools` | A RimBridgeServer companion DLL that adds the `ordo/*` tools: spawning, casting, scenarios, reloading, profiling, recording. Not a mod. |
| `tools/BridgeTools/Scenarios` | The in-game regression suite: one `<Mod>Scenarios.cs` per mod, registered in `ScenarioSets.cs` |
| `tools/BridgeTools/Showcase` | Records captioned clips with sound: one `<Mod>Showcase.cs` per mod, registered in `ShowcaseSets.cs` |
| `tools/HotReloadDelta` | Makes .NET hot reload deltas for a running game (`hot_reload.ps1 -Project <Mod>`) |
| `tools/ProcessAudioCapture` | Records only the game's sound, for the showcase |

Each mod's `README.md` is its developer documentation: design, how to test it, test results, history and open questions. Read it before working on the mod and update it after. The repo's own `README.md` lists the mods and the layout. A mod still being set up may have a `HANDOFF.md` instead, until its work is done and folded into a README.

## The everyday loop

1. **Build and deploy**, with the game stopped if the DLL changed (see [build-deploy.md](references/build-deploy.md)). Always pass `-c Debug`, as two separate commands:
   ```
   dotnet build <Mod>/<Mod>.csproj -c Debug
   dotnet build <Mod>/<Mod>.csproj -c Debug -t:CopyToRimworld
   ```
   Do the same for `tools/BridgeTools/BridgeTools.csproj` when the tools changed. `dotnet` may not be on `PATH`; it's at `~/.dotnet/dotnet.exe`.
2. **Start the game** through the `gabs` MCP server (`games_start`, game id `rimworld`, with the mod's profile), then `games_connect`. Profiles open straight into a blank 33x33 test map. Prefer a `-headless` profile unless you need to see the game. Before starting a windowed profile, start `pwsh -NoProfile -File tools/return_focus.ps1` in the background: the user works or plays while tests run, and it gives focus back if the game's window takes it. See [live-testing.md](references/live-testing.md).
3. **Test** with `ordo/run_scenarios` (pass `suite` or `filter` to narrow it, and `mod` if several mods are loaded). Call bridge tools with `games_call_tool`; find them with `games_tool_names` (brief) and `games_tool_detail`.
4. **Change and retest without restarting**: XML via deploy + `ordo/hot_reload_defs`; C# via `tools/HotReloadDelta/hot_reload.ps1` + `ordo/apply_update`. See [reload-and-debug.md](references/reload-and-debug.md). The bridge tools hot reload the same way (`-Project BridgeTools`, `mod: BridgeTools`). Restart for Harmony patches, changed signatures, new bridge tools or tool parameters, and new scenarios or clips.
5. **Stop** with `games_stop`, or `games_kill` for a headless game.

The running game is the fixture: keep one running and run suites one after another. Each scenario sets up its own site and cleans up.

## References

Load what the task needs:

- [build-deploy.md](references/build-deploy.md): build configuration traps, deploying, checking that the deployed DLL is the new one, decompiling the game.
- [live-testing.md](references/live-testing.md): GABS and its profiles, the test save, the `ordo/*` tools, the scenario suite and how to write scenarios, and every known pitfall when driving the game.
- [new-mod-setup.md](references/new-mod-setup.md): setting up a new or existing mod for live testing, with its own GABS profiles, scenarios and showcase.
- [reload-and-debug.md](references/reload-and-debug.md): def hot reload, C# hot reload and its limits, attaching Rider's debugger.
- [profiling.md](references/profiling.md): timing a mod with Dubs Performance Analyzer during a scenario run, and the mod's own `Analyzer.xml` tab.
- [showcase.md](references/showcase.md): recording captioned clips and a highlight reel with the game's sound.
- [rimworld-gotchas.md](references/rimworld-gotchas.md): RimWorld 1.7 and Waylith API behaviour that looks like a bug but isn't, and C# and XML conventions for this repo.

## Rules that save the most time

- Always build with `-c Debug`. Without it, `CopyToRimworld` deploys whatever stale DLL was already in `out/`.
- Stop the game before deploying a DLL or the bridge tools; companions and mod DLLs load only at startup.
- Have the Steam client running before starting the game. Without it, Harmony failed to load and the bridge never came up. When `games_connect` times out, read `~/.gabs/rimworld/launch.log` before anything else.
- A failed bridge call leaves a blocking "attention" item: clear it with `games_ack_attention` before carrying on.
- Read errors with `list_logs` at `minimumLevel: error`. Warnings are flooded by the bridge's own startup timing.
- Never record the user's microphone, which is ffmpeg's only audio input on this machine.
- Test animal AI against manhunters: enemy-faction animals flee.
- The game is Waylith (Vulkan/GLFW on .NET 10), not Unity. Don't assume Unity flags, Mono or Unity APIs.
