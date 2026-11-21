# DisallowedZone

A zone you paint once and switch on when there's danger, such as a mech cluster, an insect hive or raiders holed up somewhere. While it's on, the zone is taken out of every colonist's and animal's allowed area, Unrestricted ones included. Pawns treat it exactly like cells outside their allowed area. Switch it off and everyone's own areas apply again.

## Dependencies

- Harmony
- No DLC

## How it works

**The idea.** While a map's zone is on, a pawn's *effective* allowed area becomes its own area minus the zone, or the whole map minus the zone for an Unrestricted pawn. The mod does nothing else. Vanilla's allowed-area code then does all the work:

- Work and hauling skip targets in the zone (`ForbidUtility.IsForbidden` → `InAllowedArea`).
- Pawns standing in it walk out (`JobGiver_SeekAllowedArea`).
- Paths avoid it where there's another way: `PathGridJob` adds `Cost_OutsideAllowedArea` (600) per cell outside the area, and `RegionCostCalculator` does the same.
- Food searches and wandering follow it too.
- Drafted pawns, pawns in a mental state, pawns following their master, and mechs that need a mechanitor ignore it, because vanilla's `CaresAboutForbidden` already exempts them.

**One patch carries the rule.** A postfix on `Pawn_PlayerSettings.EffectiveAreaRestrictionInPawnCurrentMap` swaps in an `EffectiveArea`. Every vanilla allowed-area check reads that property. It exits at once while no zone is on anywhere (`DisallowedZone.ActiveZones` is empty). A null result can mean an Unrestricted pawn or a pawn that doesn't respect allowed areas (a lord, a guest), so the postfix checks `RespectsAllowedArea` before treating null as Unrestricted.

**`EffectiveArea`** is an `Area` subclass kept outside the area manager, so it isn't listed, assignable or saved. There is one per base area per map, plus one for Unrestricted, made the first time a pawn asks for it.
- Its cells are kept up to date cell by cell. The zone's `Set` updates every effective area. Changes to a base area come through postfixes on `Area.MarkDirty` (painting), `Area.Invert` and `Area.Clear` (full recompute). `AreaManager.Remove` disposes the effective area of a deleted area.
- The path finder keeps bit grids only for the manager's areas (`AreaSource.DataForArea`), so a prefix returns the effective area's own `NativeBitArray`.
- The path finder reuses its cost grids until it gathers a change to the map's path data, at most once a tick. Every change therefore also calls `PathFinderMapData.Notify_CellDelta`, so the grids are rebuilt by the next tick even when nothing else on the map changed. Region area-overlap caches are cleared through `Region.Notify_AreaChanged`.
- Its `Label` is the base area's label, or "Unrestricted", so vanilla messages such as "outside allowed area (Home)" still read correctly.

**The zone itself** (`DisallowedZone`, a `MapComponent`) is a `BoolGrid` plus an on/off flag, both saved with the map. It isn't an `Area`, so it never appears in the area lists.
- **Switching it on** ends any current job that targets a cell the pawn may no longer use. Vanilla does the same when you change a pawn's area. Expanding the zone while it's on does this too.
- **Clearing its last cell** switches it off. An empty zone can't be switched on.
- **While it's on**, the zone is drawn in red, unless it's hidden with the play-settings icon. The zone designators draw it either way. Whether it's shown is saved with the map.

**UI:**
- Zone tab: expand, clear (vanilla's allowed-area icons, tinted red) and a toggle button whose label shows the state. They're added after "Clear allowed area" by `Patches/DesignationCategories.xml`.
- A show/hide icon among the bottom-right play settings, shown once the map has a zone. It only hides the overlay; switching the zone on and off is the Zone tab button or the key binding.
- An alert while any zone is on. Clicking it jumps to that map.
- The key binding "toggle disallowed zone", unbound by default.

## Testing

**Profiles:**
- `disallowedzone`: Harmony, Core, RimBridgeServer and the mod.
- `disallowedzone-headless`: the same with no window.

**Scenarios** (`tools/BridgeTools/Scenarios/DisallowedZoneScenarios.cs`). The zone isn't a thing on the stage, so each scenario clears it and switches it off in a `finally`.

| Suite | Scenarios | What it checks |
|---|---|---|
| `setup` | `setup_patches_and_designators` | The component, the getter postfix, the `DataForArea` prefix, the three Zone tab designators, the key binding and the icon |
| `area` | `area_unrestricted_pawns` | Unrestricted colonist and tamed animal: allowed while off, forbidden while on, drafted ignores it, allowed again when off |
| | `area_restricted_pawns_follow_changes` | A restricted colonist gets area minus zone. Painting the area, growing and shrinking the zone, inverting the area while on all take effect. Deleting the area leaves unrestricted minus zone |
| | `area_toggle_needs_cells` | An empty zone won't switch on; clearing the last cell switches it off |
| `ai` | `ai_pawns_leave_zone` | A colonist and an animal inside walk out when it's switched on |
| | `ai_job_in_zone_interrupted` | Switching on ends a job that targets the zone |
| | `ai_hauling_skips_zone` | A hauler takes the steel outside the zone and leaves the steel inside until it's switched off |
| `path` | `path_routes_around_zone` | With a wall of zone across the site, a path crosses it while off and still crosses while on, because there's no way around. A tick after a gap is cleared, the path goes through the gap |

**Results:** 2026-11-20: 8/8 passed in `disallowedzone-headless`, and 8/8 in the windowed `disallowedzone` profile, both with no errors in the log. The windowed run drew the zone overlay, the alert and the play-settings icon while zones were on, so drawing errors would have failed it. Nothing was checked by eye. 2026-11-21, after the visibility change: 8/8 in the windowed profile. No scenario covers hiding the zone.

Planted fault: with the `Notify_CellDelta` calls removed, `path_routes_around_zone` still passed, both in the same tick and one tick later. On the test map something else makes the path finder rebuild its grids every tick, so the test can't show the call is needed. It's kept because it costs nothing and covers a quiet map. Within a single tick, paths can use a grid from before a zone edit; vanilla allowed areas behave the same.

## History

- 2026-11-20: first version.
- 2026-11-21: the play-settings icon shows or hides the zone instead of switching it on and off.

## Open questions

- Nothing has been checked by eye: the overlay colour, the tinted icons and the toggle button's label.
- Reassigning a pawn's allowed area while a zone is on doesn't end its current job. Vanilla's setter compares the new area with the effective one, which is now the mod's. It's harmless: the pawn simply finishes that job.
- One zone per map. Several named zones, each with its own switch, would be a modest addition if it's wanted.
- Prisoners, slaves and guests follow vanilla's `RespectsAllowedArea`: only player-faction pawns without a host faction respect allowed areas.
