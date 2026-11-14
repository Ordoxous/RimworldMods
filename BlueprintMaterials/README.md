# BlueprintMaterials

`Ordo.BlueprintMaterials`. Adds a **Select Material** gizmo to build blueprints, so a placed blueprint's material can be changed in place instead of cancelling it and placing it again. Floors switch to another floor from the same Architect dropdown group.

This README is for working on the mod. The player-facing description is in `mod/About/About.xml`. For building, deploying, testing and reloading, load the repo's `rimworld-mod-dev` skill.

## Dependencies

- **Harmony.** Nothing else, and no DLC. With Ideology, a blueprint's chosen style (`selectedStyleDef`) only changes the gizmo's icon.

## How it works

It's all in `src/ReplaceBlueprintMaterials.cs`: a Harmony postfix on `Blueprint_Build.GetGizmos`, patched in a static constructor.

| Part | What it does |
|---|---|
| Stuffed buildings (walls, doors, furniture) | The gizmo lists the stuffs the building allows (`GenStuff.AllowedStuffsFor`). Picking one sets `stuffToUse` on every selected blueprint the gizmo stands for, then calls `map.enrouteManager.InterruptEnroutePawns`, so anyone already hauling the old material gets a new job |
| Floors (a `TerrainDef` with a `designatorDropdown`) | Floors carry their material in the def, so the gizmo lists the other terrains in the same dropdown group. Picking one destroys each selected blueprint (`DestroyMode.Cancel`), places a new one for the chosen terrain and selects it. A group whose terrains share a material (carpets, which differ by colour) is labelled by terrain (`LabelCap`); the others by material (`LabelAsStuff`) |
| Availability | Only materials with at least one thing of that def on the map are listed, plus the current one; in god mode, everything is. With no alternative, the gizmo is disabled with "No other material is available." |
| Grouping (`Command_Action_AlternativeMaterial`) | `GroupsWith` and `SelectedBlueprints` use the same rule: same blueprint def and same current material. Wood and steel walls selected together show two gizmos, and each changes only its own walls. Vanilla's own grouping already separates gizmos with different icons; the mod's rule is what separates materials whose icons are the same, such as granite and marble walls |
| The gizmo's look | The building's (or floor's) icon in the current material, the dropdown triangle at the top right, and the mod's own swap icon (`mod/Textures/UI/Gizmos/Ordo_BlueprintMaterials_ChangeMaterial.png`) 24 px in the top-left corner |
| Caches | `SimilarTerrainCache` and `StuffCache` are filled once per def, since the gizmo is built every frame for every selected blueprint. A def hot reload makes new def objects, so the caches just miss and rebuild (checked; see below) |

Menus are built on click, not with the gizmo. Text is in `mod/Languages/English/Keyed/BlueprintMaterials.xml`.

Style: 4 spaces and K&R braces in places, unlike the repo's newer mods, which use tabs. Match the file you're in.

## Testing

**Profiles** (in `~/.gabs/config.json`):

- `blueprintmaterials`, `blueprintmaterials-headless` and `blueprintmaterials-showcase` load Harmony, Core, RimBridgeServer and this mod. No DLCs.
- The windowed and headless profiles share the data folder `~/.gabs/rimworld/blueprintmaterials`. The showcase profile has its own, with a 1920x1080 window on the main monitor.
- The save was made fresh for this mod list from the main menu, not copied from another mod's.
- Headless works: building the gizmos and opening their menus logs nothing without rendering. Drawing them is only exercised windowed, so run the suite both ways.

Run the suite with `ordo/run_scenarios` (add `mod: Ordo.BlueprintMaterials` if anything else is loaded). The scenarios are in `tools/BridgeTools/Scenarios/BlueprintMaterialsScenarios.cs`.

| Suite | Scenarios | What they check |
|---|---|---|
| `setup` | `setup_where_the_gizmo_appears`, `visuals_gizmos_draw` | A stuffed blueprint gets the gizmo; one not made from stuff, and a floor in no dropdown group, don't |
| `visuals` | `visuals_gizmos_draw` | A wall's and a floor's gizmos stay on screen for 60 ticks, so any error drawing them fails the scenario. Only windowed: headless it passes without testing anything |
| `stuff` | `stuff_change`, `stuff_only_the_gizmos_group`, `stuff_unselected_untouched`, `stuff_haulers_replanned` | The material changes on every selected wall; only the clicked gizmo's group changes (granite and marble walls, whose gizmos look alike); unselected blueprints keep theirs; a colonist carrying wood to the wall stops when it becomes steel, nothing is left en route, and they bring steel instead |
| `grouping` | `stuff_only_the_gizmos_group`, `stuff_unselected_untouched`, `floor_change` | Which blueprints a click changes |
| `availability` | `availability_disabled_without_alternative`, `availability_lists_what_is_on_the_map`, `availability_god_mode_lists_everything` | Disabled with its reason when nothing else is on the map; with steel on the map, exactly wood (current) and steel are offered; in god mode, every material a wall allows |
| `floor` | `floor_change`, `floor_labels_by_terrain_when_material_shared` | Two floor blueprints are replaced by the chosen floor at the same cells, and the new ones are selected; carpets are offered by their own names, not all as "cloth" |
| `ai` | `stuff_haulers_replanned` | The hauler, through the real work givers |

How the scenarios are built:

- **As a player drives the gizmo:** select the blueprints (`Stage.Select`), take the gizmo from what's selected, open its menu (`OpenMenu`) and pick an option by label (`Choose`). `Stage.SelectionGizmos()` gives the grid as the player sees it, with grouped gizmos shown once.
- **Floors are picked from the defs at runtime:** a dropdown group with floors of two materials (the stone tiles today), and one whose floors share a material (the carpets). So the scenarios don't depend on which floors Core has.
- **The hauler** is a `Stage.Worker` with only Construction enabled, generated until one is capable of it.
- God mode is set in a `finally` and restored.

**To look at the gizmo by hand**, start a windowed profile and call `ordo/stage_blueprintmaterials`. It leaves five wood wall blueprints selected, with granite and steel on the map, and reports the grid's gizmos and the UI size. Don't save afterwards.

**Results (2026-11-14):**

- 11/11 on `blueprintmaterials-headless` (0.6 s, twice in a row in one game), on the windowed `blueprintmaterials` (1.7 s) and at 1080p on `blueprintmaterials-showcase` with the new icon.
- 11/11 again after `ordo/hot_reload_defs` in the same game: the caches don't go stale in a way that matters, so no fix was needed.
- SillyAnimalThings (49/49) and MechUpgradesUi (34/34) still passed with the new `Stage` helpers.

Every behaviour scenario was seen to fail against a build with that behaviour broken. The faults were deployed as builds, because hot reload couldn't make deltas for edits inside lambdas. Each batch failed exactly the scenarios it targeted:

| Fault | Failed |
|---|---|
| No hauler interrupt | `stuff_haulers_replanned` |
| `Matches` ignores material | `stuff_only_the_gizmos_group` |
| The new floor isn't selected | `floor_change` |
| Carpets labelled by material | `floor_labels_by_terrain_when_material_shared` |
| God mode ignored | `availability_god_mode_lists_everything` |
| Every material counts as available | `availability_disabled_without_alternative`, `availability_lists_what_is_on_the_map` |
| `SelectedBlueprints` takes every blueprint on the map | `stuff_unselected_untouched` |
| `GroupsWith` ignores material | `stuff_only_the_gizmos_group`, once it used granite and marble; with wood and steel it passed, because their different icons already kept the gizmos apart |
| The Ideology texture (the build before the fix in History) | `visuals_gizmos_draw`, windowed |

## Showcase

`tools/BridgeTools/Showcase/BlueprintMaterialsShowcase.cs`, recorded on `blueprintmaterials-showcase` into `tools/BridgeTools/videos/blueprintmaterials` (about 30 s to record). Both clips keep the UI and film the whole window (`ShowcaseFrame.Window`), because the gizmos are at its bottom edge. The cursor is RimBridge's virtual one, placed on the gizmo with `ShowcaseStage.GizmoCenter`, and the game pauses while it works the menu, as a player would.

- `change_walls` ("Select Material"): a colonist sets off with wood for seven wall blueprints. The cursor hovers the gizmo (its tooltip shows), opens the menu (steel, wood, granite) and picks granite, and the colonist turns back for granite.
- `change_floors` ("Floors Too"): 28 sandstone-tile blueprints become marble tiles, from a menu of sandstone, granite and marble.

Blueprints look the same on the map whatever their material, so the change shows in the gizmo's icon and the inspect pane. Both clips were checked by pulling a frame every 2 s.

## History

- **2026-11-14, tests, a showcase and its own icon.**
  - Fixed: the gizmo logged "Could not load Texture2D at 'UI/Gizmos/ChangeStyle'" the first time it was drawn in any game without Ideology. That texture is Ideology's, and the gizmo drew it as an overlay. The gizmo now has its own swap icon in its top-left corner, in every game. The old overlay sat in the middle, covering the material's icon, and on floors it was cropped by the terrain icon's texture coordinates.
  - The scenario suite, profiles and showcase above.
- **2026-11-08:** floors that share a material, like carpets, show their own names and icons instead of repeating the material. Changing material interrupts haulers carrying the old one.
- **2026-11-07:** the current material stays in its own menu, and the gizmo is disabled when there's no alternative (no more empty menu). Gizmos group only by blueprint def and current material, and a click applies to exactly that group. The terrain and stuff lists are cached and menus built on click. The blueprint's own map is used instead of `Find.CurrentMap`. The text moved to Keyed translations.
- Earlier: the 1.7 update and the initial release.

## Next steps

- The mod's name in game is still "BlueprintMaterials", and its About description is one line. Both could say more.
- Not tested with Ideology loaded, where a blueprint's chosen style changes the gizmo's icon.
