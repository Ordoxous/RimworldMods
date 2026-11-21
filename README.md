# RimworldMods

Ordoxous' RimWorld 1.7 mods, plus the tooling used to test them in a running game. Each mod has its own folder and its own `README.md` for working on it.

## Mods

| Folder | Name in game | Package ID | What it does | Needs | In-game tests |
|---|---|---|---|---|---|
| [SillyAnimalThings](SillyAnimalThings/README.md) | SillyAnimalThings | `Ordo.SillyAnimalThings` | Gives animals abilities, granted by tags and gated by training, that the AI uses when animals fight | Harmony, Odyssey (Biotech optional) | 49 scenarios, 22 showcase clips |
| [MechUpgradesUi](MechUpgradesUi/README.md) | Mech Upgrade Policies | `Ordo.MechUpgradesUi` | Apparel-style upgrade policies for Mechanoid Upgrades: mechs fetch allowed upgrades and drop unwanted ones by themselves | Biotech, Mechanoid Upgrades | 34 scenarios, 4 showcase clips |
| [LegendaryIsUnique](LegendaryIsUnique/README.md) | LegendaryIsUnique | `Ordo.LegendaryIsUnique` | A Legendary craft of a weapon that has an Odyssey unique variant makes the unique version | Harmony, Odyssey (VSE optional) | 7 scenarios |
| [Tweaks](Tweaks/README.md) | Tweaks | `Ordo.Tweaks` | Small, individually switchable changes to the base game and other mods | Harmony, Biotech, WVC Work Modes, XML Extensions | 32 scenarios |
| [BlueprintMaterials](BlueprintMaterials/README.md) | BlueprintMaterials | `Ordo.BlueprintMaterials` | A Select Material gizmo on blueprints: change a placed blueprint's material, or a floor to another in its group, without cancelling it | Harmony | 11 scenarios, 2 showcase clips |
| [ColonistBarJobs](ColonistBarJobs/README.md) | Colonist Bar Job Icons | `Ordo.ColonistBarJobs` | An icon on each colonist bar portrait for what the colonist is doing now: the bench, item, bed or building they work on, or the kind of work | Harmony | 7 scenarios |
| [NatureTab](NatureTab/README.md) | Nature Tab | `Ordo.NatureTab` | A Nature main tab, in place of Animals and Wildlife, with four pages: harvestable wild plants by kind (counts, days until ready, yields, plants at risk, one-click harvest), visible ore by kind (cells, veins, yield, value, overhead mountain, one-click mining), and the Animals and Wildlife tabs moved in, hosting Animal Tab's window when it's loaded | none | 27 scenarios |
| [DisallowedZone](DisallowedZone/README.md) | Disallowed Zone | `Ordo.DisallowedZone` | A zone you switch on in danger: while on, it's taken out of every colonist's and animal's allowed area, Unrestricted included, so pawns leave it, skip work in it and route around it | Harmony | 8 scenarios |
| [Saddlebound](Saddlebound/README.md) | Saddlebound | `Ordo.Saddlebound` | Ideology memes and precepts for Giddy-Up riders: riding as a cultural need, preferred mount species, mourning fallen mounts, glory for kills from the saddle, and Horde Riders factions that raid mounted | Harmony, Ideology, Giddy-Up 2 - Continued | 17 scenarios |

## Layout

A mod's folder holds:

- `<Mod>.csproj`, which lists only the mod's references to other mods (see [Conventions](#conventions))
- its C# under `src/`
- what ships in the mod (About, Defs, Patches, Languages, textures) under `mod/`
- its `README.md`

The project name, assembly name and deployed folder under `RimWorld\Mods` are the same.

| Path | What it is |
|---|---|
| `Directory.Build.props` | Settings for every project: .NET 10, `Ordo.<Mod>` namespaces, the RimWorld reference packages, publicizing `Assembly-CSharp`, and the `out/Assemblies` output in every configuration. `SteamLibraryDirectory` defaults to `D:\SteamLibrary`; override it (or `RimWorldDirectory`) in a git-ignored `Directory.Build.local.props` |
| `Directory.Build.targets` | Targets for every project: `WorkshopReference`, copying `mod/` into `out/`, the `CopyToRimworld` deploy target, and the About.xml check |
| `build/LocalRimWorldReferences.targets` | In Debug builds, swaps the `Krafs.Rimworld.Ref` assemblies for the installed game's when the versions match, so the IDE shows real method bodies |
| `tools/BridgeTools` | A companion DLL for RimBridgeServer that adds the `ordo/*` tools: spawning, scenarios, def and C# reloading, profiling, and showcase recording. Not a mod |
| `tools/BridgeTools/Scenarios` | The in-game regression suites, one `<Mod>Scenarios.cs` per mod |
| `tools/BridgeTools/Showcase` | Records captioned clips with the game's sound, one `<Mod>Showcase.cs` per mod |
| `tools/HotReloadDelta` | Makes .NET hot reload deltas for a running game |
| `tools/ProcessAudioCapture` | Records only the game's sound, for the showcase |
| `tools/return_focus.ps1` | Gives keyboard focus back when a windowed test game takes it on launch; start it in the background before starting a windowed profile |
| `.run/Deploy.ps1` | Builds and deploys one mod, or `All` |
| `.claude/skills/rimworld-mod-dev` | How to build, deploy, test, reload, debug, profile and record any mod here. Written for coding agents, but it's the most complete guide to the workflow |

## Building

```
dotnet build <Mod>/<Mod>.csproj                      # build into <Mod>/out
dotnet build <Mod>/<Mod>.csproj -t:CopyToRimworld    # build, then deploy to RimWorld\Mods\<Mod>
```

- **Stop the game before deploying a DLL.** Mods load only at startup.
- The configuration defaults to Debug, which hot reload and the debugger need. Every configuration builds to `out/Assemblies`.
- Building copies `mod/` into `out/` and removes files deleted from `mod/`. Deploying replaces the mod's folder in the game with `out/`. `CopyToRimworldDebug` deploys to the install at `RimWorldDebugDirectory` instead.
- Building checks `mod/About/About.xml` against the About.xml rules below, and fails with an `ORDO001` error on the line at fault.
- Mods that depend on Workshop mods compile against the Workshop copies under `steamapps\workshop\content\294100` (see `WorkshopReference` below). When a dependency has no 1.7 folder, they fall back to its 1.6 one.

## Testing

Tests run inside the real game. GABS launches RimWorld with a per-mod profile, a separate data folder that opens straight into a blank test map. RimBridgeServer exposes the game as tools, and `ordo/run_scenarios` runs the mod's scenario suite against it. A headless profile runs a suite in seconds. Each mod's README lists its profiles, suites and latest results. The `rimworld-mod-dev` skill covers the whole workflow, including setting up a new mod.

## Conventions

Rules marked **(checked)** fail the build when broken.

### Names and layout

- Package IDs are `Ordo.<Mod>`, and C# namespaces `Ordo.<Mod>`. New defNames use the `Ordo_` prefix.
- A mod has `src/`, `mod/About/About.xml`, `mod/Languages/English/Keyed` if its code shows any text, a `README.md`, a row in the mods table above, and a scenarios file in `tools/BridgeTools/Scenarios`.
- Each mod's `README.md` is its developer documentation: design, how to test it, results, history and open questions. Update it with any change to the mod.

### Project files

- A mod's `.csproj` holds only references to other mods. Build settings belong in `Directory.Build.props` and `Directory.Build.targets`, so a mod with no dependencies is just `<Project Sdk="Microsoft.NET.Sdk" />`.
- Reference a Workshop mod by assembly name and Workshop ID, never by `HintPath`:
  ```xml
  <ItemGroup>
      <WorkshopReference Include="MechanoidUpgrades" WorkshopId="3365118555" />
  </ItemGroup>
  ```
  It resolves to the mod's 1.7 `Assemblies` folder, or its 1.6 one, and stays out of the output. A missing mod fails the build with a message saying which one.

### About.xml

- **(checked)** `packageId` is `Ordo.<Mod>`, `author` is `Ordoxous`, `url` is the mod's folder on GitHub, `supportedVersions` includes the current RimWorld version, and `name` and `description` aren't empty.
- **(checked)** Every dependency has a `displayName`, and every dependency other than a DLC has a `steamWorkshopUrl` or `downloadUrl`.
- **(checked)** Every dependency, DLCs included, is also in `loadAfter`. Optional integrations go in `loadAfter` too.
- **(checked)** `brrainz.harmony` is a dependency if and only if the code uses `HarmonyLib`. Every project compiles against Harmony, so About.xml is the only place that says whether a mod needs it.

### Harmony

- One Harmony ID per mod: its package ID, `Ordo.<Mod>`. `typeof(X).Namespace` gives it from any class in the root namespace.
- Patch from a `[StaticConstructorOnStartup]` class. Use the `Mod` constructor only when a patch must be in place before defs finish loading, and say why in a comment.
- Patch by hand with `AccessTools` lookups, not `[HarmonyPatch]` attributes. When a target isn't found, log a warning naming what stops working, and skip that feature rather than throw.

### Other mods

- A hard dependency is a `WorkshopReference`, and the calls into it go through one adapter class, as Saddlebound's `GiddyUpAdapter` does.
- An optional mod's types are touched only inside a `[MethodImpl(MethodImplOptions.NoInlining)]` method that runs when the mod is active, as Tweaks does for BiotechPatch, so they're never resolved when it's missing. Its XML is guarded with `MayRequire`.
- Each optional integration gets its own GABS profile pair, so its scenarios run.

### Code and XML

- Formatting is set in `.editorconfig`, which Rider and `dotnet format` both read: spaces, never tabs; 4 for C#, project files, JSON and PowerShell, and 2 for XML and Markdown; LF line endings.
- C#: file-scoped namespaces, braces on their own line, and one parameter per line in method signatures. `dotnet format whitespace <project>` fixes indentation and braces; Rider's reformat also wraps parameters.
- Text the code shows players goes through `Keyed` translations and `.Translate()`.
- Log lines start with `[<Mod>]`.
- Settings are a `<Mod>Settings : ModSettings` and a `<Mod>Mod : Mod`.
- Fields RimWorld loads from XML or saves keep its camelCase names; `.editorconfig` allows them.
- XML: guard DLC content with `MayRequire="Ludeon.RimWorld.<DLC>"`.
