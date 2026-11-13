# MechUpgradesUi handoff

Last updated 2026-11-13. It has no tests or showcase yet: adding them is the current task (see `tools/HANDOFF.md` for the brief and the shared work).

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

## What to test

The scenario file is `tools/BridgeTools/Scenarios/MechUpgradesUiScenarios.cs` (not yet written). This mod's logic is mostly pure decisions over MU's data, so most scenarios can call the methods directly. Keep a few that go end to end through the think tree.

| Scenario | Check |
|---|---|
| Patches applied | A player mech's comp is `CompUpgradableMechWithPolicy`; `MU_MechUpgrader` spawns as `Building_AutoMechUpgrader`; the `Mechs` `PawnTableDef` has the three columns; the `Mechanoid` think tree contains `JobGiver_OptimizeUpgrades` |
| Policy database | Create, default is first, `SetDefaultPolicy` moves one to the front, delete is refused while a living mech uses it and clears dead mechs' references |
| Keep and want | With a filter allowing A and not B, a mech with B: `UpgradesToKeep` drops B, `WantedUpgrades` includes A |
| Point budget | More wanted upgrades than points: `WantedOperations` adds only what fits, preferring nothing in particular (it sorts by defName) |
| Exclusions | Two wanted upgrades that MU says conflict: only one is added |
| Removals run first | Operations list ends with the removals (MU runs from the end) |
| Only active linked storage | Upgrades in an unlinked or unpowered storage aren't offered |
| No policy, no trip | Unassigned mech with the default setting: no job, upgrades untouched |
| Default-for-unassigned setting | Turn it on: the unassigned mech now goes. Restore the setting in cleanup; settings persist to the profile's config |
| Interval | After one check, no new trip until `nextApparelOptimizeTick` |
| End to end | Mech with a policy walks to the upgrader, goes in without a dialog, and comes out with the upgrade installed and the storage item consumed |
| Manual insert dialog | A player-ordered (`playerForced`) insert with the setting on opens MU's dialog instead. Close it in the scenario |

Set-up needs that `Stage` doesn't have yet, so check each in game first:

- **A controlled colony mech.** Biotech mechs need an overseer, a mechanitor colonist (with a mechlink), and must be in command range. The think tree node this mod hooks into only runs for player-controlled mechs. Look at how the game's dev actions spawn a controlled mech. `Stage.Spawn` is written for animals (it fixes an adult age and has animal-only modes), so add a mech helper.
- **Mech energy.** A mech low on energy goes to recharge first, which outranks this job.
- **Power.** `MU_MechUpgrader` is a 3x3 building with a 150 W `CompPowerTrader`, a `CompFlickable`, and `CompAffectedByFacilities` linking to `MU_UpgradesStorage` or `MU_LargeUpgradesStorage`. The storages need to be linked (placed close enough) and active. Find a dependable way to power them on the blank test map: a generator and conduits, or forcing `PowerOn` if the game doesn't immediately undo it.
- **Upgrade items.** MU's upgrades are items with `CompMechUpgrade`, put into a storage's `CompUpgradesStorage.innerContainer`. MU's defs are in its `1.6\Defs\MechUpgradeDefs` and `ThingDefs_Items`. Decompile `MechanoidUpgrades.dll` (see the skill's build-deploy reference) to read `CompUpgradesStorage`, `MechUpgradeDef.CanAdd`, `MechUpgradeUtility` and `Building_MechUpgrader`.
- **Research.** MU's buildings require research. Spawning them directly ignores research, but check that the upgrader's own logic doesn't check it too.
- **Fabrication time.** Each operation takes 2500 ticks per upgrade point (`fabricationTicksLeft`). Stepping that many ticks is fast headless. In a showcase clip, shorten it by setting `fabricationTicksLeft` and say so in the clip's comment.
- **Settings.** `MechUpgradesUiMod.Settings` is static and saved to the profile's config. Restore anything a scenario changes in a `finally`.

The test save is a 33x33 blank map; a site is 32 cells across. An upgrader, two storages and a mechanitor fit.

## Showcase

Suggested clips:

1. The policy dialog: a policy allowing two upgrades, assigned to a mech in the Mechs tab. This needs the UI visible.
2. The trip: a lifter with a policy walks to the auto upgrader and comes out upgraded, with the storage emptied (fabrication time shortened for the clip).
3. Replacing: a mech with an unwanted upgrade comes out with it swapped for a wanted one.

Both need the showcase generalised beyond abilities; see `tools/HANDOFF.md`.

## Open questions for the user

- Should MU's own behaviour be tested where this mod relies on it (for example, that an operation really installs the upgrade), or only this mod's decisions? The brief assumes one end-to-end scenario is enough.
