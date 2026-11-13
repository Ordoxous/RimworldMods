# MechUpgradesUi handoff

Last updated 2026-11-13. It has an in-game scenario suite (15 scenarios, all passing). A showcase is still to do (see `tools/HANDOFF.md`).

## What the mod is

"Mech Upgrade Policies" (`Ordo.MechUpgradesUi`): upgrade policies for [Mechanoid Upgrades](https://steamcommunity.com/sharedfiles/filedetails/?id=3365118555) (MU), set up like apparel policies. Mechs with a policy go to an auto mech upgrader by themselves, fill their upgrade points with what the policy allows, and have unwanted upgrades removed.

Dependencies: Biotech and MU (`GoGaTio.MechanoidUpgrades`, Steam Workshop 3365118555, installed at `D:\SteamLibrary\steamapps\workshop\content\294100\3365118555`). MU has no 1.7 folder: its `loadFolders.xml` loads `1.6` for 1.7, and this mod's csproj falls back to MU's `1.6\Assemblies\MechanoidUpgrades.dll` the same way. MU's own types are in the `MU` namespace. Optional: Work Tab, which swaps one Mechs tab column (not worth testing).

| File | What it does |
|---|---|
| `mod/Patches/MechUpgradesUi_Patches.xml` | Replaces MU's `CompProperties_UpgradableMechanoid` on every overseeable mech with this mod's subclass; makes `MU_MechUpgrader` a `Building_AutoMechUpgrader` labelled "Auto mech upgrader"; adds three columns to the Mechs tab; inserts `JobGiver_OptimizeUpgrades` into the `Mechanoid` think tree, after the recharging node, for player-controlled mechs |
| `UpgradePolicy.cs` | A vanilla `Policy` holding a `ThingFilter` over upgrade items |
| `WorldComponent_UpgradeDatabase.cs` | The list of policies. The first is the default. Deleting is refused while a living colony mech uses the policy, and clears it from dead ones |
| `CompUpgradableMechWithPolicy.cs` | Holds the mech's policy. `EffectiveUpgradePolicy` is that, or the default policy when the "use default for unassigned" setting is on, otherwise null, which means hands off. `UpgradesToKeep` and `WantedUpgrades` apply the filter |
| `JobGiver_OptimizeUpgrades.cs` | Every few hours (setting, 1-24, default 3, ±20%; it reuses `mindState.nextApparelOptimizeTick`), sends the mech to whichever reachable auto upgrader can do the most operations for it |
| `Building_AutoMechUpgrader.cs` | `WantedOperations` picks additions from the upgrades in the upgrader's linked, active storage facilities, within the point budget and MU's `CanAdd` rules, then removals, which go last in the list because MU runs operations from the end. `TryAcceptPawn` applies them without MU's dialog, unless the insert was player-ordered and the "show dialog on manual insert" setting is on |
| `Dialog_ManageUpgradePolicies.cs`, `PawnColumnWorker_*.cs` | The policy editor and the Mechs tab columns |
| `MechUpgradesUiMod.cs` | Settings: use default policy for unassigned mechs (off), show the dialog on manual insert (on), check interval in hours (3) |

Style: 4 spaces, as in the existing files.

## Testing

Profiles (in `~/.gabs/config.json`): `mechupgradesui`, `mechupgradesui-headless` and `mechupgradesui-showcase`. They load Harmony, Core, Biotech, RimBridgeServer, MU and this mod. MU is a Workshop mod, so **the Steam client must be running**. The data folders are `~/.gabs/rimworld/mechupgradesui` (shared by the windowed and headless profiles) and `mechupgradesui-showcase`. The `mechupgradesui` save was made fresh for this mod list. The SillyAnimalThings save it was copied from stopped silently at the main menu, with no error logged.

Run with `ordo/run_scenarios` (`mod: Ordo.MechUpgradesUi` if anything else is loaded). Scenarios are in `tools/BridgeTools/Scenarios/MechUpgradesUiScenarios.cs`.

| Suite | Scenarios | What they check |
|---|---|---|
| `setup` | `patches_applied` | The mech comp swap, the upgrader's class and label, the three Mechs tab columns, and the think tree node |
| `policy` | `policy_database`, `policy_keep_and_want` | Create, default, `SetDefaultPolicy`, delete refused for a living mech and clearing a dead one's; `UpgradesToKeep` drops a disallowed upgrade and `WantedUpgrades` wants an allowed one in the same trip |
| `operations` | `operations_point_budget`, `operations_exclusions`, `operations_removals_run_first`, `operations_linked_storage_only` | `WantedOperations` called directly |
| `ai` | `ai_no_policy_no_trip`, `ai_unpowered_no_trip`, `ai_default_for_unassigned`, `ai_interval`, `ai_end_to_end`, `ai_stock_gone_on_arrival` | Trips through the think tree, plus `JobGiver_OptimizeUpgrades` called directly where the think tree can't show the decision |
| `player` | `player_insert_dialog`, `player_insert_no_dialog` | A player-ordered insert (`upgrader.OrderForceTarget(mech)`, as MU's gizmo and float menu do) with the dialog setting on and off |
| `settings` | the scenarios that change a setting | They restore it in a `finally` |

How the scenarios work:

- **The site:** a powered auto upgrader facing south at the origin, a storage 4 cells east (or 14, out of its 11.9-cell link range), a drafted mechanitor, and a lifter they oversee with its generated upgrades removed. The `Stage` helpers are `Build(..., powered: true)`, `Mechanitor` and `ControlledMech`. Power is forced on, because a building on no power net keeps whatever state it's given. The lifter has 4 upgrade points.
- **Upgrades are picked at runtime** from MU's defs that come as items and that `CanAdd` accepts for the lifter, in defName order. `TwoCompatible` gives two that fit together; on the current MU these are `MU_CargoCell_Double` (wanted) and `MU_ChemfuelMiniBurner` (unwanted). The exclusion scenario finds two that share a tag (`MU_CargoCell_Double` and `MU_CargoCell_Small`).
- **Policies** made by a scenario are removed afterwards, and the policy list is restored, because the world keeps them.
- `ai_end_to_end` is the one scenario that relies on MU doing the work, as agreed with the user. It covers the mech going in with no dialog, the item leaving storage, waiting out the fabrication time, the swap, and the removed upgrade going back into storage. The suite steps about 10,000 ticks.
- MU's storages have no power comp, so a linked storage is always active. Only "unlinked" can be tested, not "inactive".

Results on 2026-11-13: 15/15 on `mechupgradesui-headless` (about 3.5 s, three runs in a row) and on the windowed `mechupgradesui` (67 s). SillyAnimalThings still passes 49/49 on `sillyanimalthings-biotech-headless` with the shared changes.

Every behaviour scenario was seen to fail with a fault planted by hot reload, in three batches. Each batch failed exactly the scenarios it targeted:

| Fault | Failed |
|---|---|
| No point budget check | `operations_point_budget` |
| `CanAdd` checked against the kept upgrades only, not the ones being added | `operations_exclusions` |
| Removals inserted first | `operations_removals_run_first` |
| Every storage on the map offered, linked or not | `operations_linked_storage_only` |
| `UpgradesToKeep` keeps everything | `policy_keep_and_want`, `operations_removals_run_first`, `ai_end_to_end` |
| `TryDelete` ignores living mechs | `policy_database` |
| Default policy always used / never used | `ai_no_policy_no_trip` / `ai_default_for_unassigned` |
| `nextApparelOptimizeTick` never set | `ai_interval` |
| Job giver skips `CanAcceptPawn` (power) | `ai_unpowered_no_trip` |
| Dialog never / always shown | `player_insert_dialog` / `player_insert_no_dialog`, `ai_end_to_end` |

`patches_applied` only checks wiring and wasn't faulted. `ai_unpowered_no_trip` at first passed with its fault, because vanilla's enter job drops a trip to an unpowered building at once, so no trip is seen. It now also asks the job giver directly.

### Bug fixed

- **An autonomous trip with nothing left to do waited behind MU's dialog.** If the stock was gone by the time the mech arrived (another mech took it, or someone hauled it away), `TryAcceptPawn` fell back to MU's `base.TryAcceptPawn`. That put the mech in and opened the configure dialog, so it sat in the upgrader until someone answered. Now a trip that isn't player-ordered and has nothing to do doesn't go in. A player-ordered insert with nothing to do still opens MU's dialog, as before. Covered by `ai_stock_gone_on_arrival`, which failed before the fix.

## Showcase

Suggested clips:

1. The policy dialog: a policy allowing two upgrades, assigned to a mech in the Mechs tab. This needs the UI visible.
2. The trip: a lifter with a policy walks to the auto upgrader and comes out upgraded, with the storage emptied (fabrication time shortened for the clip).
3. Replacing: a mech with an unwanted upgrade comes out with it swapped for a wanted one.

Both need the showcase generalised beyond abilities; see `tools/HANDOFF.md`.

## Open questions for the user

None. The user confirmed that one end-to-end scenario is enough to cover MU's own behaviour.

## Notes for later

- `ApplyOperations` takes the item out of storage with `innerContainer.Remove`, so it doesn't destroy it. The upgrade object moves to the mech, which is what MU's own dialog does too.
- An upgrade whose def has no `linkedThingDef` is never kept, because the filter can't allow it. MU's `DoOperation` then just drops it. I haven't checked whether MU has any such defs that a player mech can get.
