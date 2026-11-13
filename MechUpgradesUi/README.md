# Mech Upgrade Policies (MechUpgradesUi)

`Ordo.MechUpgradesUi`. Upgrade policies for [Mechanoid Upgrades](https://steamcommunity.com/sharedfiles/filedetails/?id=3365118555) (MU), set up like apparel policies. A mech with a policy goes to an auto mech upgrader by itself. There it fills its upgrade points with what the policy allows and has unwanted upgrades removed.

This README is for working on the mod. The player-facing description is in `mod/About/About.xml`. For building, deploying, testing and reloading, load the repo's `rimworld-mod-dev` skill.

## Dependencies

- **Biotech.**
- **MU** (`GoGaTio.MechanoidUpgrades`, Steam Workshop 3365118555, installed at `D:\SteamLibrary\steamapps\workshop\content\294100\3365118555`).
  - MU has no 1.7 folder: its `loadFolders.xml` loads `1.6` for 1.7. The csproj falls back to MU's `1.6\Assemblies\MechanoidUpgrades.dll` the same way, through `RimworldFallbackVersion`.
  - MU's own types are in the `MU` namespace.
  - MU is a Workshop mod, so the Steam client must be running for the game to load it.
- **Optional: Work Tab.** It swaps one Mechs tab column for its own. Not worth testing.

The mod doesn't use Harmony. All its changes to MU and the game are XML patches and subclasses.

## How it works

| File | What it does |
|---|---|
| `mod/Patches/MechUpgradesUi_Patches.xml` | Replaces MU's `CompProperties_UpgradableMechanoid` with this mod's subclass on every overseeable mech. Makes `MU_MechUpgrader` a `Building_AutoMechUpgrader`, labelled "Auto mech upgrader". Adds three columns to the Mechs tab. Inserts `JobGiver_OptimizeUpgrades` into the `Mechanoid` think tree, after the recharging node, for player-controlled mechs |
| `UpgradePolicy.cs` | A vanilla `Policy` holding a `ThingFilter` over upgrade items |
| `WorldComponent_UpgradeDatabase.cs` | The list of policies. The first is the default. Deleting is refused while a living colony mech uses the policy, and clears it from dead ones |
| `CompUpgradableMechWithPolicy.cs` | Holds the mech's policy. `EffectiveUpgradePolicy` is that policy, or the default policy when the "use default for unassigned" setting is on, otherwise null, which means hands off. `UpgradesToKeep` and `WantedUpgrades` apply the filter. `WantedUpgrades` checks against what will be kept, so an upgrade can replace an unwanted one in the same trip |
| `JobGiver_OptimizeUpgrades.cs` | Every few hours (the interval setting, ±20%), sends the mech to whichever reachable auto upgrader can do the most operations for it. It reuses `mindState.nextApparelOptimizeTick`, and sets it before deciding, so a trip that fails partway can't be retried in a tight loop |
| `Building_AutoMechUpgrader.cs` | `WantedOperations` picks additions from the upgrades in the upgrader's linked, active storages. They're tried in defName order, within the point budget and MU's `CanAdd` rules, checked against everything the mech will end up with. Removals go after the additions because MU runs operations from the end of the list; otherwise removing an upgrade would also take a shared ability from the upgrade replacing it. `TryAcceptPawn` applies the operations without MU's dialog, except for a player-ordered insert with the "show dialog on manual insert" setting on. A trip of the mech's own with nothing left to do doesn't go in |
| `Dialog_ManageUpgradePolicies.cs` | The policy editor, a vanilla `Dialog_ManagePolicies`. It has a "Mech Type" filter for the upgrade list |
| `PawnColumnWorker_*.cs` | The Mechs tab columns: the policy picker (with "Manage Upgrade Policies" in its header), used and total upgrade points (click to open the mech's upgrades tab), and the mech's current job report |
| `MechUpgradesUiMod.cs` | Settings: use the default policy for unassigned mechs (default off), show the dialog on manual insert (on), and the check interval in hours (3, from 1 to 24) |

A player-ordered insert is recognised by `pawn.CurJob.playerForced`. MU's gizmo and float menu option both go through `Building_Enterable.SelectPawn`, which takes an ordered job. The think tree's trips don't.

Style: 4 spaces, as in the existing files (unlike the repo's other mods, which use tabs).

## Testing

**Profiles** (in `~/.gabs/config.json`):

- `mechupgradesui`, `mechupgradesui-headless` and `mechupgradesui-showcase` load Harmony, Core, Biotech, RimBridgeServer, MU and this mod.
- The windowed and headless profiles share the data folder `~/.gabs/rimworld/mechupgradesui`. The showcase profile has its own.
- The `mechupgradesui` save was made fresh for this mod list. The SillyAnimalThings save it was copied from stopped silently at the main menu, with no error logged.

Run the suite with `ordo/run_scenarios` (add `mod: Ordo.MechUpgradesUi` if anything else is loaded). The scenarios are in `tools/BridgeTools/Scenarios/MechUpgradesUiScenarios.cs`.

| Suite | Scenarios | What they check |
|---|---|---|
| `setup` | `patches_applied` | The mech comp swap, the upgrader's class and label, the three Mechs tab columns, and the think tree node |
| `policy` | `policy_database`, `policy_keep_and_want` | Create, default, `SetDefaultPolicy`, deleting refused for a living mech and clearing a dead one's; `UpgradesToKeep` drops a disallowed upgrade and `WantedUpgrades` wants an allowed one in the same trip |
| `operations` | `operations_point_budget`, `operations_exclusions`, `operations_removals_run_first`, `operations_linked_storage_only` | `WantedOperations` called directly |
| `ai` | `ai_no_policy_no_trip`, `ai_unpowered_no_trip`, `ai_default_for_unassigned`, `ai_interval`, `ai_end_to_end`, `ai_stock_gone_on_arrival` | Trips through the think tree, plus `JobGiver_OptimizeUpgrades` called directly where the think tree can't show the decision |
| `player` | `player_insert_dialog`, `player_insert_no_dialog` | A player-ordered insert (`upgrader.OrderForceTarget(mech)`, as MU's gizmo and float menu do) with the dialog setting on and off |
| `settings` | The scenarios that change a setting | Settings are static and saved to the profile's config, so each of these restores the setting in a `finally` |

How the scenarios are built:

- **The site:** a powered auto upgrader facing south at the origin, and a storage 4 cells east (or 14, outside its 11.9-cell link range). With them, a drafted mechanitor and a lifter they oversee, with the lifter's generated upgrades removed. The lifter has 4 upgrade points.
  - The `Stage` helpers used are `Build(..., powered: true)`, `Mechanitor` and `ControlledMech`.
  - Power is forced on, because a building on no power net keeps whatever power state it's given.
- **Upgrades are picked at runtime:** from MU's defs that come as items and that `CanAdd` accepts for the lifter, in defName order.
  - `TwoCompatible` gives two that fit together. On the current MU these are `MU_CargoCell_Double` (wanted) and `MU_ChemfuelMiniBurner` (unwanted).
  - The exclusion scenario finds two that share a tag: `MU_CargoCell_Double` and `MU_CargoCell_Small`.
  - This way the scenarios don't depend on MU's content staying the same.
- **Policies** made by a scenario are removed afterwards, and the policy list is restored, because the world keeps them.
- **Only one scenario relies on MU doing the work:** `ai_end_to_end`, as agreed with the user. It covers the mech going in with no dialog, the item leaving storage, waiting out the fabrication time (2500 ticks per upgrade point), the swap, and the removed upgrade going back into storage.
- **Storage can only be tested as unlinked:** MU's storages have no power comp, so a linked storage is always active. Only "unlinked" can be tested, not "inactive".

**Results on 2026-11-13:**

- 15/15 on `mechupgradesui-headless`, three runs in a row of about 3.5 s each.
- 15/15 on the windowed `mechupgradesui`, in 67 s.
- SillyAnimalThings still passed 49/49 with the shared `Stage` and runner changes this suite needed.

Every behaviour scenario was seen to fail with a fault planted by hot reload, in three batches, and each batch failed exactly the scenarios it targeted:

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

- `patches_applied` only checks wiring, so it wasn't faulted.
- `ai_unpowered_no_trip` at first passed with its fault planted. Vanilla's enter job drops a trip to an unpowered building at once, so no trip is ever seen. It now also asks the job giver directly.

## History

- **2026-11-13, fixed: an autonomous trip with nothing left to do waited behind MU's dialog.** If the stock was gone by the time the mech arrived (another mech took it, or someone hauled it away), `TryAcceptPawn` fell back to MU's `base.TryAcceptPawn`. That put the mech in and opened the configure dialog, so it sat in the upgrader until someone answered. Now a trip that isn't player-ordered and has nothing to do doesn't go in. A player-ordered insert with nothing to do still opens MU's dialog. Covered by `ai_stock_gone_on_arrival`, which failed before the fix.

## Next steps

- **A showcase.** Suggested clips:
  1. The policy dialog: a policy allowing two upgrades, assigned to a mech in the Mechs tab. This needs the UI visible.
  2. The trip: a lifter with a policy walks to the auto upgrader and comes out upgraded, with the storage emptied. Shorten the fabrication time for the clip by setting `fabricationTicksLeft`, and say so in the clip's comment.
  3. Replacing: a mech with an unwanted upgrade comes out with it swapped for a wanted one.

  The `mechupgradesui-showcase` profile is ready. The clips need `ShowcaseClip` and `ShowcaseRunner` generalised beyond animal abilities: a caption that isn't an ability's, and a per-clip option to keep the UI. The BlueprintMaterials work needs the same changes; see `tools/HANDOFF.md`.

## Notes

- `ApplyOperations` takes the item out of storage with `innerContainer.Remove`, so it doesn't destroy it. The upgrade object moves to the mech, which is what MU's own dialog does too.
- An upgrade whose def has no `linkedThingDef` is never kept, because the filter can't allow it. MU's `DoOperation` then just drops it. I haven't checked whether MU has any such defs that a player mech can get.
- MU's upgrader ejects its occupant if the mech stops being overseen (`OverseerSubject.State` isn't `Overseen`), so a test mech needs its mechanitor for the whole stay.
