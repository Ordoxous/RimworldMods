# BlueprintMaterials handoff

Last updated 2026-11-13. It has no tests or showcase yet: adding them is the current task (see `tools/HANDOFF.md` for the brief and the shared work).

## What the mod is

Adds a **Select Material** gizmo to build blueprints (`Blueprint_Build`), so a placed blueprint's material can be changed in place instead of cancelling and re-placing it. It's all in `src/ReplaceBlueprintMaterials.cs`, a Harmony postfix on `Blueprint_Build.GetGizmos`.

- **Stuffed buildings** (walls, doors, furniture): the gizmo lists the stuffs the building allows (`GenStuff.AllowedStuffsFor`). Picking one sets `stuffToUse` on every selected blueprint the gizmo stands for, then calls `map.enrouteManager.InterruptEnroutePawns` so anyone already hauling the old material gets a new job.
- **Floors** (`TerrainDef` with a `designatorDropdown`): floors carry their material in the def, so the gizmo lists the other terrains in the same dropdown group, and picking one destroys each selected blueprint (`DestroyMode.Cancel`) and places a new one for the chosen terrain, then selects it.
  - Groups whose terrains share a material (carpets, which differ by colour) are labelled by terrain (`def.LabelCap`); the others by material (`LabelAsStuff`).
- **Availability:** only materials with at least one thing of that def on the map are listed, plus the current one. In god mode everything is. With no alternative, the gizmo is disabled with "No other material is available."
- **Grouping:** `Command_Action_AlternativeMaterial.GroupsWith` and `SelectedBlueprints` use the same rule: same blueprint def and same current material. So with wood walls and steel walls selected, there are two gizmos, and each changes only its own group.
- Caches (`SimilarTerrainCache`, `StuffCache`) are filled once per def, since defs don't change at runtime. After `ordo/hot_reload_defs` they'd hold old def objects; the mod doesn't clear them. Worth a scenario, or a note if it's harmless.

Dependencies: Harmony only. No DLC. `selectedStyleDef` (Ideology styles) only affects the gizmo's icon.

Style: this mod's C# uses 4 spaces and K&R braces in places, unlike SillyAnimalThings' tabs. Match the file you're in.

## What to test

The scenario file is `tools/BridgeTools/Scenarios/BlueprintMaterialsScenarios.cs` (not yet written). Suggested cases, one per mechanism:

| Scenario | Set-up | Check |
|---|---|---|
| Stuff change | Wood wall blueprints, granite blocks on the map, all selected | Picking granite sets `stuffToUse` on all of them |
| Only the gizmo's group changes | Wood and steel wall blueprints selected, granite on the map | Two gizmos; changing the wood one leaves the steel walls alone |
| Unselected blueprints untouched | Two wood wall blueprints, one selected | Only the selected one changes |
| Disabled with no alternative | Wood wall blueprint, no other stuff on the map, god mode off | Gizmo `Disabled`, with the "No other material" reason |
| Only available materials listed | Steel on the map, no granite | Menu has wood (current) and steel, not granite |
| God mode lists everything | As above, `DebugSettings.godMode` on (turn it off again in cleanup) | Granite is listed |
| Floor change | Wood floor blueprint, the right material for another floor in the same dropdown group on the map | Old blueprint gone, new blueprint for the other terrain at the same cell, and selected |
| Carpet labels | A carpet blueprint, cloth on the map | Options are labelled by terrain, not all "cloth" |
| Haulers re-planned | A colonist carrying wood to a wood wall blueprint | After switching to steel, the colonist drops the wood job (`enrouteManager` no longer lists them for that blueprint, or their job changes) |

How to drive the gizmo without clicking (to try in game first):

- Get gizmos with `blueprint.GetGizmos()` (the postfix runs), and find the `Command_Action_AlternativeMaterial` among them. It's public, so scenarios can reference the type once BridgeTools has a `ProjectReference` to this mod.
- Select the blueprints with `Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false)`; `SelectedBlueprints()` reads the live selection. Clear the selection in cleanup.
- `command.action()` adds a `FloatMenu` to `Find.WindowStack`. Read its `options` and invoke the chosen one's `action`, then close the menu. The runner fails a scenario that leaves a dialog open.

Things to check first, because they may not hold headless (no rendering):

- Building the gizmo touches textures (`Widgets.GetIconFor`, `Widgets.CroppedTerrainTextureRect`, `CachedTexture`). If those throw or log errors headless, run this mod's suite windowed, and say so in the profiles.
- Whether `Find.WindowStack.Add(new FloatMenu(...))` works headless.

Placing blueprints: `GenConstruct.PlaceBlueprintForBuild(def, cell, map, rot, Faction.OfPlayer, stuff)`. Materials: `Stage.Place(ThingDefOf.BlocksGranite, x, z)` (set `stackCount` as needed). Colonists: `Stage.Colonist`, which drafts them, so undraft one before giving it hauling work.

## Showcase

Suggested clip: a row of wood wall blueprints and a stack of granite blocks, with a colonist on the way to build. Select the walls, open the menu, pick granite: the blueprints turn grey, and the colonist turns round to fetch granite instead. A second clip for floors would show the dropdown.

The gizmo and the menu are the point here, and `ordo/record_showcase` currently hides the UI and captions clips from an ability. See `tools/HANDOFF.md` for the changes that need. A float menu opened from code draws where the mouse is, so check where it lands in the 1280x720 crop.

## Open questions for the user

- If a scenario shows the caches go stale after `ordo/hot_reload_defs`, should that be fixed? It only affects dev-mode reloads.
