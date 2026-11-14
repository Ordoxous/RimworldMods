# RimworldMods

Ordoxous' RimWorld 1.7 mods, plus the tooling used to test them in a running game. Each mod has its own folder and its own `README.md` for working on it.

## Mods

| Folder | Name in game | Package ID | What it does | Needs | In-game tests |
|---|---|---|---|---|---|
| [SillyAnimalThings](SillyAnimalThings/README.md) | SillyAnimalThings | `Ordo.SillyAnimalThings` | Gives animals abilities, granted by tags and gated by training, that the AI uses when animals fight | Harmony, Odyssey (Biotech optional) | 49 scenarios, 22 showcase clips |
| [MechUpgradesUi](MechUpgradesUi/README.md) | Mech Upgrade Policies | `Ordo.MechUpgradesUi` | Apparel-style upgrade policies for Mechanoid Upgrades: mechs fetch allowed upgrades and drop unwanted ones by themselves | Biotech, Mechanoid Upgrades | 27 scenarios, 3 showcase clips |
| [LegendaryIsUnique](LegendaryIsUnique/README.md) | LegendaryIsUnique | `Ordo.LegendaryIsUnique` | A Legendary craft of a weapon that has an Odyssey unique variant makes the unique version | Harmony, Odyssey | None yet |
| [Tweaks](Tweaks/README.md) | Tweaks | `Ordo.Tweaks` | Small, individually switchable changes to the base game and other mods | Harmony, Biotech, WVC Work Modes, XML Extensions | None yet |
| BlueprintMaterials | BlueprintMaterials | `Ordo.BlueprintMaterials` | Replaces a blueprint's ingredients in place | Harmony | Being set up; see `BlueprintMaterials/HANDOFF.md` |

## Layout

A mod's folder holds:

- `<Mod>.csproj`
- its C# under `src/`
- what ships in the mod (About, Defs, Patches, Languages, textures) under `mod/`
- its `README.md`

The project name, assembly name and deployed folder under `RimWorld\Mods` are the same.

| Path | What it is |
|---|---|
| `Directory.Build.props` | Shared build settings for every project: .NET 10, the RimWorld reference packages, publicizing `Assembly-CSharp`, the `out/Assemblies` output, and the `CopyToRimworld` deploy target. `SteamLibraryDirectory` defaults to `D:\SteamLibrary`; override it in a git-ignored `Directory.Build.local.props` |
| `Directory.Build.targets` | Swaps the reference packages for the locally installed game's assemblies when the versions match, so the IDE shows real method bodies |
| `tools/BridgeTools` | A companion DLL for RimBridgeServer that adds the `ordo/*` tools: spawning, scenarios, def and C# reloading, profiling, and showcase recording. Not a mod |
| `tools/BridgeTools/Scenarios` | The in-game regression suites, one `<Mod>Scenarios.cs` per mod |
| `tools/BridgeTools/Showcase` | Records captioned clips with the game's sound, one `<Mod>Showcase.cs` per mod |
| `tools/HotReloadDelta` | Makes .NET hot reload deltas for a running game |
| `tools/ProcessAudioCapture` | Records only the game's sound, for the showcase |
| `.claude/skills/rimworld-mod-dev` | How to build, deploy, test, reload, debug, profile and record any mod here. Written for coding agents, but it's the most complete guide to the workflow |

## Building

Build with `-c Debug`, then deploy as a separate command:

```
dotnet build <Mod>/<Mod>.csproj -c Debug
dotnet build <Mod>/<Mod>.csproj -c Debug -t:CopyToRimworld
```

- **Always pass `-c Debug`.** Without it, the output paths in `Directory.Build.props` don't apply, and `CopyToRimworld` deploys whatever stale DLL is in `out/`.
- **Stop the game before deploying a DLL.** Mods load only at startup.
- Mods that depend on Workshop mods compile against the Workshop copies under `steamapps\workshop\content\294100`. When a dependency has no 1.7 folder, they fall back to its 1.6 one.

## Testing

Tests run inside the real game. GABS launches RimWorld with a per-mod profile, a separate data folder that opens straight into a blank test map. RimBridgeServer exposes the game as tools, and `ordo/run_scenarios` runs the mod's scenario suite against it. A headless profile runs a suite in seconds. Each mod's README lists its profiles, suites and latest results. The `rimworld-mod-dev` skill covers the whole workflow, including setting up a new mod.

## Conventions

- Package IDs are `Ordo.<Mod>`, and C# namespaces `Ordo.<Mod>`. New defNames use the `Ordo_` prefix.
- C#: file-scoped namespaces and one parameter per line in method signatures. Newer mods use tabs; MechUpgradesUi and Tweaks use 4 spaces, so match the file you're in.
- XML: 2 spaces. Guard DLC content with `MayRequire="Ludeon.RimWorld.<DLC>"`.
- Each mod's `README.md` is its developer documentation: design, how to test it, results, history and open questions. Update it with any change to the mod.
