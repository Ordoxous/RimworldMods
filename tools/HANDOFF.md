# Task: tests and showcases for MechUpgradesUi and BlueprintMaterials

Written 2026-11-13 for the next agent. Delete this file once the task is done and its lasting content is in the two mods' `README.md` files and the `rimworld-mod-dev` skill.

## The goal

Give `BlueprintMaterials` and `MechUpgradesUi` what `SillyAnimalThings` has: GABS profiles, a test save, an in-game scenario suite run by `ordo/run_scenarios`, and a showcase recorded by `ordo/record_showcase`. Use the existing infrastructure. Where it's specific to animal abilities, generalise it without breaking SillyAnimalThings: its 49 scenarios must still pass, and its 22 clips must still record.

## Read first

1. The `rimworld-mod-dev` skill (`.claude/skills/rimworld-mod-dev/SKILL.md`), then its `new-mod-setup.md`, `live-testing.md` and `showcase.md` references. `new-mod-setup.md` is the checklist this task follows.
2. `BlueprintMaterials/HANDOFF.md` and `MechUpgradesUi/README.md`: what each mod does, what to test, how to demo it, and the traps found while writing this. MechUpgradesUi is done: 27 scenarios and 3 showcase clips. Its showcase is the model for clips that aren't abilities.
3. `tools/BridgeTools/Scenarios/SillyAnimalThingsScenarios.cs` and `tools/BridgeTools/Showcase/SillyAnimalThingsShowcase.cs`, as the models.

## Suggested order

1. **BlueprintMaterials first.** It's one file, has no dependencies beyond Harmony, and will shake out the UI-driving helpers (selection, gizmos, float menus) that MechUpgradesUi needs too.
   1. Profiles: `blueprintmaterials`, `blueprintmaterials-headless`, and `blueprintmaterials-showcase`. If gizmos can't be built headless, drop the headless one and say why.
   2. Scenarios, then the showcase.
2. **MechUpgradesUi.**
   1. Profiles with Biotech and MU: `brrainz.harmony`, `ludeon.rimworld`, `ludeon.rimworld.biotech`, `brrainz.rimbridgeserver`, `gogatio.mechanoidupgrades`, `ordo.mechupgradesui`. MU is a Workshop mod, so the Steam client must be running for it to load.
   2. Get a controlled mech, a powered upgrader and a linked storage working by hand with bridge tools before writing scenarios.
3. Each mod's developer docs now live in its `README.md`, which replaces its handoff. When BlueprintMaterials is done, turn its `HANDOFF.md` into a `README.md` shaped like the other mods' (how it works, testing and results, history, next steps), delete the handoff, and update its row in the repo `README.md`. Update the skill wherever you generalised something.

## Shared work

These changes are for everyone, not one mod:

| Area | Now | Needed |
|---|---|---|
| `ScenarioSets.All`, `ShowcaseSets.All` | SillyAnimalThings and MechUpgradesUi | A line for `("Ordo.BlueprintMaterials", ...)` |
| `BridgeTools.csproj` | References SillyAnimalThings and MechUpgradesUi, plus `MechanoidUpgrades.dll` with its 1.6 fallback | A `ProjectReference` to BlueprintMaterials with `Private="false"` |
| `ShowcaseClip` | Done: `ShowcaseClip.Feature(name, title, caption, run, showUi, frame)` makes a clip with its own title and caption. Ability clips are unchanged | Use it for BlueprintMaterials' clips |
| `ShowcaseRunner` | Done: `showUi` keeps the UI for a clip. `ShowcaseFrame.Window` shows the whole window scaled down, with the caption at the top, for gizmos and the inspect pane. `ShowcaseStage.Hold` waits in real time while a dialog pauses the game | See `showcase.md` in the skill. SillyAnimalThings' clips haven't been re-recorded since the recording changed to the whole window, with `make_showcase.ps1` cropping it to the same middle 1280x720 |
| `Stage` | Animal helpers: `Spawn` fixes adult age and has animal modes. `Colonist` is drafted and unarmed | Helpers for blueprints, selection (clear it in `CleanUp`), invoking a gizmo and picking from the resulting float menu, controlled mechs with a mechanitor, and powered buildings. General ones in `Stage.cs`, mod-specific ones in the mod's scenario file |
| `Scenario.RequiresAbility` | Skips a scenario whose ability def isn't loaded | Probably not needed: both mods' dependencies are hard. Add a general guard only if something optional turns up |
| Profiles in `~/.gabs/config.json` | SillyAnimalThings only | The new ones, each with a `description`. GABS reads its config at start, so it may need restarting before it sees new profiles; ask the user if so |

The scenario runner fails a scenario on any error in the log and on a dialog left open. A float menu or MU's upgrade dialog counts as a dialog, so close them.

## Working in this repo

- Build with `-c Debug`, as two commands, and stop the game before deploying a DLL. The skill explains why.
- C# hot reload works for the mods and for the bridge tools (`hot_reload.ps1 -Project <Mod|BridgeTools>`, then `ordo/apply_update`). Restart for Harmony patches, new bridge tools or tool parameters, and new scenarios or clips. Both mods patch with Harmony at startup: BlueprintMaterials in a static constructor, MechUpgradesUi through XML patches and a think-tree insert.
- `ordo/record_showcase` with `append: true` re-records some clips without losing the rest.
- Ask before committing or pushing. When asked to push, the user wants a fast-forward of `master` on the `base` remote.
- If a test finds a bug in a mod, fix it with a scenario that covers it, and note it in that mod's handoff. Ask the user before changing how a mod is meant to behave.

## Done when

- Both mods' suites pass on their profiles, and SillyAnimalThings' still passes.
- Each new scenario that checks a behaviour has been seen to fail when that behaviour is broken, by breaking it on purpose or running against an older build, as SillyAnimalThings' `ai_*` scenarios were.
- Each mod has showcase clips cut by `make_showcase.ps1`, which you've checked by pulling a frame from each.
- The handoffs and the skill are up to date.
