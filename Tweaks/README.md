# Tweaks

`Ordo.Tweaks`. A grab bag of small, individually switchable changes to the base game and to other mods: mechs, colony management, balance and compatibility fixes. Most are XML patches. A few are C#.

This README is for working on the mod. For building, deploying and testing, load the repo's `rimworld-mod-dev` skill.

## Dependencies

- **Hard:**
  - Harmony.
  - Biotech.
  - [WVC - Work Modes](https://steamcommunity.com/sharedfiles/filedetails/?id=2888380373) (`wvc.sergkart.biotech.MoreMechanoidsWorkModes`), for its shutdown zones.
  - [XML Extensions](https://steamcommunity.com/sharedfiles/filedetails/?id=2574315206) (`imranfish.xmlextensions`), for the settings menu and the switchable patches.
- **Optional:** everything else. Each tweak for another mod only applies when that mod is loaded. Their settings are hidden otherwise (`MayRequire` in `Defs/TweaksSettings.xml`).

The csproj compiles against four Workshop assemblies, all with `Private` false and falling back to the 1.6 folder where there's no 1.7 one:

- XML Extensions
- WVC Work Modes
- BiotechPatch (3461060466), an optional mod. The code that touches its types is kept in a separate `[MethodImpl(NoInlining)]` method, called only when the mod is active, so those types are only resolved when they exist.
- MultiFloors (3384660931), an optional mod, the same way: `src/MultiFloorsShutdown.cs` is only reached through `TweaksSettings.ShutdownZonesAcrossLevels`, which is false without it. No type may *derive* from a MultiFloors type: the game lists every type in the assembly at startup, and a missing base type would make Tweaks unusable without MultiFloors. MultiFloors' types in member signatures are fine.

### Where the parent mods are

Workshop mods are in `D:\SteamLibrary\steamapps\workshop\content\<id>`. Several have no 1.7 folder and load their 1.6 one, so check a mod's `loadFolders.xml` before assuming which folder is live. When finding a mod by `packageId`, skip IDs that only appear inside `modDependencies` or `loadAfter`: a grep on `dubwise.dubsbadhygiene` first hits a DBH compatibility patch (2949772583).

| Parent mod | Workshop ID | Folder loaded on 1.7 |
|---|---|---|
| BiotechPatch | 3461060466 | 1.6 |
| ReSplice: Core | 3236303965 | 1.6, via `loadFolders` |
| VFE Production | 1880253632 | 1.6, via `loadFolders` |
| VFE Power | 2062943477 | 1.6, via `loadFolders` |
| Vanilla Temperature Expanded | 3202046258 | 1.6, via `loadFolders` |
| Dubs Bad Hygiene | 836308268 | 1.7 |
| Complex Jobs | 2069684319 | `1.7/Ludeon/Core` and others |
| Simple Improve | 3538863870 | 1.6 |
| Mechanoid Upgrades | 3365118555 | 1.6, via `loadFolders` |
| Mecha Weapon Changer | 3487306694 | 1.6 |
| Medieval Tailor | 3512201406 | 1.6 |
| Vanilla Skills Expanded | 3400246558 | 1.7 |
| WVC Work Modes | 2888380373 | 1.7 |
| MultiFloors | 3384660931 | 1.6 (its `loadFolders` has no `v1.7`; whether its `ModCompat` folders load is unchecked) |
| Fantasy Biotech (`VR_Construct_*`) | 3632363090 | 1.6 |
| Setosa's Power Tools | 3526160222 | 1.6 |
| TD Enhancement Pack - Continued | 3525414162 | 1.7 (its C# source ships in `Source/`) |

To read a mod's code, decompile it with ilspycmd as the skill's `build-deploy.md` describes for the game, passing `-r` with the game root and the mod's dependencies' folders. MultiFloors and WVC Work Modes decompile cleanly this way.

## Settings

All settings are declared once in `mod/Defs/TweaksSettings.xml`, an XML Extensions `SettingsMenuDef` (`Ordo_TweaksSettings`).

- **XML patches** read them through `XmlExtensions.OptionalPatch` (on/off) or `UseSetting` (a value).
- **C# reads them** through `src/TweaksSettings.cs`, which wraps `SettingsManager.TryGetSetting`.
- **A default lives in three places:** the menu def, the patch's `defaultValue` and `TweaksSettings.cs`. Keep them in step; `setup_setting_defaults` checks the menu against the C# ones.
- **Saved settings win.** XML Extensions writes every setting to the profile's `Config/Mod_2574315206_XmlMod.xml` on first start, so changing a `defaultValue` later doesn't change an existing install.
- **Restarts:** XML patches apply at load, so their settings need a restart; the menu marks them with `*`. C# features read the setting each time and apply at once. `ToolsCategory` is the exception: it runs once at startup.

## The tweaks

All are on by default.

| Setting key | What it does | Applies with | Where |
|---|---|---|---|
| `ArmorIndicators` | Floating numbers when a pawn takes damage: red for damage dealt, struck-through for damage armor blocked (white for metal armor, cyan otherwise) | Always | `src/ShowArmorIndicators.cs`, a postfix on `Thing.TakeDamage` |
| `DeteriorationAlertSkipTainted` | TD Enhancement Pack's "deteriorating things" alert stops counting tainted apparel (worn by a corpse). The apparel still deteriorates, and vanilla's "deteriorated away" message is untouched | TD Enhancement Pack | `src/DeteriorationAlertNoTainted.cs`, a postfix on TD's `Alert_Deterioration.GetReport` |
| `HaulDownedMechs` | Haulers (and mechs that can haul) carry downed colony mechs to a WVC shutdown zone, where they can repair | Always | `src/HaulMechForRepair.cs`, `Defs/HaulMechsForRepair.xml` |
| `HaulUnpoweredMechs` | Haulers (and mechs that can haul) carry colony mechs that shut down for lack of power to a WVC shutdown zone, when no charger is free for them: the zone cell nearest a charger the mech can use, so it's closer and out of the way until one frees up | Always | `src/HaulMechToShutdownZone.cs`, `Defs/HaulMechsToShutdownZone.xml` |
| `ShutdownZonesAcrossLevels` | When a mech's level has no free shutdown zone cell, the two hauling tweaks carry it up or down the stairs to a zone on another level, and a mech in a WVC work mode that goes to a shutdown zone walks there | MultiFloors | `src/MultiFloorsShutdown.cs`, `src/ShutdownCells.cs`, `Defs/HaulMechsToShutdownZone.xml` |
| `ChargersAcrossLevels` | When a mech vanilla would carry to a charger finds none free on its level, haulers carry it to a free one on another level, before either zone tweak and even from a zone | MultiFloors | `src/HaulMechToChargerOnOtherLevel.cs`, `src/MultiFloorsShutdown.cs`, `Defs/HaulMechsToShutdownZone.xml` |
| `ResourcesAcrossLevels` | The Architect's costs, beside the cursor while placing or painting and in the info panel, count what's stored on every level, so they aren't marked "not enough stored" when the materials are on another floor. A new build designator keeps its default material when it's stocked on any level | MultiFloors | `src/MultiFloorsResources.cs` |
| `ReadoutAcrossLevels` | The resource list at the top left shows the totals of every level, in both its simple and categorized views | MultiFloors | `src/MultiFloorsReadout.cs` |
| `BillsAcrossLevels` | A "Do until you have X" bill counts the product on every level, so a kitchen upstairs stops when the freezer downstairs is full. A bill counting one stockpile still counts only that one | MultiFloors | `src/MultiFloorsBills.cs` |
| `LightMechsUseStandardChargers` | Light mechs can use the standard recharger | Always | `Patches/ChargeSmallMechsInTheBigCharger.xml` |
| `MechItemsAtMachiningTable` | Things made at the subcore encoder can also be made at the machining table | Always | `Patches/CraftMechThingsAtMachiningTable.xml` |
| `SmallMechsInLargeGestator` | The large mech gestator can also make what the small one makes | Always | `Patches/CraftSmallMechsInTheBigGestator.xml` |
| `SmallUpgradesAtAdvancedAssembler` | Mechanoid Upgrades: the advanced assembler can make what the basic one makes | Mechanoid Upgrades | `Patches/CraftSmallMechUpgradesInTheAdvancedTable.xml` |
| `MechExtraWorkTypes` | Lifters get Complex Jobs' deliver work type, and its merge work type when that's enabled in Complex Jobs; constructoids get Simple Improve's improving work type. Fantasy Biotech's `VR_Construct_Lifter` and `VR_Construct_Constructoid` get the same | Complex Jobs, Simple Improve | `Patches/MechsAreCapable.xml` |
| `LimitHemogenFarming`, `HemogenPackCap` | BiotechPatch's "hemogen farm anyone" only queues extractions while the map has fewer packs than the cap (default 30) | BiotechPatch | `src/HemogenTilSet.cs`, a prefix on `Comp_HemogenFarm.CompTick` |
| `ToolsCategory` | A "tools" stockpile category under Weapons, holding weapons whose equipped stat offsets help work | Always (useful with a mod that adds tools, such as Setosa's Power Tools) | `src/ToolsCategory.cs`, `Defs/ToolsCategoryDef.xml` |
| `VseCriticalLearnRateOther` | Vanilla Skills Expanded's Critical passion learns other skills at 0.75 (0.25-1) | VSE | `Patches/VSE_Critical_Passions.xml` |
| `BlacksmithGlovesWorkSpeed` | Medieval Tailor's blacksmith gloves give +25% global work speed (0-50%) | Medieval Tailor | `Patches/BlacksmithGlovesAreUseful.xml` |
| `VfeGeneratorRebalance`, `VfePortableGeneratorWatts`, `VfeIndustrialGeneratorWatts` | VFE Power's portable generators make 200 W and industrial ones 3000 W, instead of VFE's 500 W portables that beat the bigger ones. The industrial ones become minifiable (mass 60) | VFE Power | `Patches/VFE_Power.xml` |
| `Minifiable*` | Can be uninstalled and moved: Mecha Weapon Changer, MU's mech upgrader (mass 50), ReSplice's gene vault, repository, centrifuge and xenogerm duplicator, VFE's component fabrication bench, and Vanilla Temperature's wall units (cooler 6, vent 3; the heater has its own 6) | The matching mod | `Patches/Minifiable*.xml`, `Patches/VFE_Temperature.xml` |
| `VfeBenchLinksFabricationCabinet` | VFE's component fabrication bench links to the fabrication cabinet | VFE Production | `Patches/VFE_AssemblyBenchLinkable.xml` |
| `DubsWellsOnGravships` | Dubs Bad Hygiene's water wells can be placed on gravship substructure | Dubs Bad Hygiene | `Patches/DubsOnGravships.xml` |
| `ReGrowthExtinctNoDuplicateAnimals` | Removes the `wildAnimals` entries ReGrowth: Extinct Animals adds for the dodo, thrinaxodon and synthetion, which ReGrowth 2's defs already place through race `wildBiomes` at the same commonality | ReGrowth 2 and ReGrowth: Extinct Animals (Continued) | `Patches/ReGrowthExtinctNoDuplicateAnimals.xml` |

Notes on the tweaks:

- **Minifiable things need a mass on 1.7.** Anything haulable without an authored mass is a config error at load. The minifiable patches add one where the parent mod has none, compared by name so an upstream mass wins.
- **`ToolsCategory`** adds the defs in code, because weapons usually inherit `thingCategories`, so a patch can't match them on the same node as `equippedStatOffsets`. It also checks the stat's category, which leaves out things like the minigun's move speed penalty. It runs in a long event queued from `ExecuteWhenFinished`, after every static constructor and whatever they queued: Setosa's Power Tools applies its tools' work offsets from a long event queued in its own static constructor. The category tree is already resolved by then, so it updates the cached child lists itself.
- **`HaulUnpoweredMechs`** takes the same mechs as vanilla's `HaulMechsToCharger` (spawned, not downed, in low-energy self-shutdown, not in a self-shutdown control group, below its recharge limit) and only when vanilla's own `JobGiver_GetEnergy_Charger.GetClosestCharger` finds nothing, so the two never compete; once a charger frees up, vanilla carries the mech on from the zone. A mech already in a shutdown zone is left alone. The cell is the zone cell nearest the interaction cell of any compatible, unforbidden charger, taken or unpowered (weight classes come from the def, so `LightMechsUseStandardChargers` counts), else a random one. Neither hauling tweak uses WVC's `TryFindRandomMechShutdownZone`, which shuffles the map's zone list in place, or its `CanSelfShutdown`, which asks whether the *mech* can reach and reserve the cell on its own map; `ShutdownCells` makes the same checks for the hauler. After the drop the hauler releases the cell and starts the mech's `SelfShutdown` job on it, as vanilla starts the charge job; otherwise the energy need picks its own nearby spot, which can be outside the zone. Downed mechs are `HaulDownedMechs`'s, which skips a downed mech vanilla would take to a free charger (below its recharge limit, not in a self-shutdown group, `GetClosestCharger` finds one); a downed mech at its limit isn't vanilla's, so it still goes to the zone. That reading of vanilla comes from `WorkGiver_HaulMechToCharger.HasJobOnThing`: downed mechs skip its self-shutdown check but not the others.
- **`ShutdownZonesAcrossLevels`** (2026-11-21). In MultiFloors each level is its own map, linked by stairs, so on their own both hauling tweaks and WVC's zone search only look at the mech's level. A zone on the mech's own level is always tried first.
  - **Haulers:** with no usable cell on the hauler's level, `MultiFloorsShutdown.TryFindCellOnOtherLevel` goes through the other levels nearest first (`GetOtherMapVerticallyOutward`). A cell must pass `ShutdownCells.Suitable`, be unreserved (`IsReservedByAnyoneOf`: `ReservationManager.CanReserve` refuses a claimant on another map), and be reachable through the stairs: setting the pawn's `ReferencePosition` to the cell makes MultiFloors' `FindPathTo(map)` pick an arrival stair that can reach it, and `CanReachAcrossLevel` keeps that path for the job. Only 8 free cells per level get that check. `HasJobOnThing` clears the kept path, since the work giver may not pick this job. `Ordo_CarryMechToOtherLevel` follows MultiFloors' `JobDriver_TakeToBedOnOtherLevel`: pick the mech up, take the next stair, `SwitchMap`; then another of itself for the next stair, or on the zone's level the same-map job (`HaulMechForRepair` or `Ordo_HaulMechToShutdownZone`), which skips the pickup when it's already carrying the mech. Its JobDef has `carryThingAfterJob` (and `dropThingBeforeJob` false), like `MF_RescueToOtherLevel`: `SwitchMap` ends the job as it moves the hauler, and without it the mech is dropped at the foot of the stairs. The same-map job is started with `keepCarryingThingOverride`. The job is added to MultiFloors' private `ChangingLevelJobDefs` set, which gravship launches and rituals check.
  - **WVC's work modes:** a postfix on `JobGiver_GoToShutdownZone.TryGiveJob`. When it gives no job and the mech isn't already in a zone or shutdown room, the mech takes `MF_ChangeLevelThroughStair` to a level with a free cell it can reach; WVC's giver sends it to a cell once it's there. This follows MultiFloors' own `HarmonyPatch_MechChargeAcrossLevel`, which does the same for chargers.
  - **MultiFloors' level data lives in `[PrepatcherField]` fields, but the Prepatcher mod isn't needed on Waylith:** the game ships `RimWorldWaylith.PrepatcherCompat.dll`, which provides them. Checked 2026-11-21: the `levels` suite and `setup` pass with and without `zetrith.prepatcher` in the profile. Either way each accessor gets a body inside MultiFloors' own assembly, so calling them from Tweaks is fine.
  - **`ChargersAcrossLevels`** (2026-11-21): `WorkGiver_HaulMechToChargerOnOtherLevel` (priority 91, so scanned before vanilla's `HaulMechsToCharger` and the downed tweak at 90 and the unpowered one at 89) takes the mechs vanilla's `HasJobOnThing` would (downed or in low-energy shutdown, not in a self-shutdown group, below the recharge limit, not charging) when vanilla's `GetClosestCharger` finds none on their level, so it never takes one vanilla would. It doesn't skip mechs in a zone, so a parked mech moves on when a charger frees up upstairs. The charger must pass `CanPawnChargeCurrently`, MultiFloors' `IsForbiddenAcrossLevel`, be unreserved, and be reachable with `CanReachAcrossLevel(charger)`. The carry job keeps the charger as its `globalTarget`; on arrival `JobDriver_DeliverMechToCharger` (vanilla's `JobDriver_HaulMechToCharger` that can start with the mech already carried; vanilla's always walks to the mech first) puts it on the charger and starts `MechCharge`. If the charger was taken on the way, another on that level, else a zone cell there.
  - **A hauler on another level** fetches a mech through MultiFloors' own work scan, with no code here: a pawn with no job on its level runs the work givers from a stair on each other level (`HarmonyPatch_ScanJobsOnOtherLevel` and its default `Prioritized` twin, which put the pawn there with `UnsafeTeleportTo`), takes `MF_ChangeLevelThroughStair` there, and resumes the job it found. A carry job planned from that level keeps its stair path in `SecondaryStairPath`; `FindNextStair` passes over it on the hauler's own level (its first stair is on another map) and follows it once there.
  - Not covered: elevators (the driver handles them as MultiFloors' does, untested), and more than one level away. `JobDriver_ChangeLevel.ForcedBodyOffset`, which draws the hauler climbing, is left out: MultiFloors' `DrawOffset` returns Unity's `Vector3` from an assembly this build doesn't reference.
- **`ResourcesAcrossLevels`** (2026-11-22). Each MultiFloors level is its own map with its own `ResourceCounter`, so vanilla's costs counted only the level shown. MultiFloors already lists other levels' materials in the stuff menu (its transpiler on `Designator_Build.ProcessInput`) and delivers them (`WorkGiver_ConstructDeliverResourcesAcrossLevel`), but leaves the counts alone.
  - A transpiler swaps `ResourceCounter.GetCount` for `MultiFloorsResources.GetCount`, which adds every other level's count on the tile (`GetAllLevelMaps(except)`).
  - It's applied to `Designator_Build.DrawPlaceMouseAttachments`, `DrawPanelReadout` and the `StuffDef` getter, and to `Designator_Paint.DrawMouseAttachments` (dye).
  - Not covered: the `StuffDef` fallback's choice of another material, which still reads the current level's amounts. The top-left resource readout is `ReadoutAcrossLevels`'.
  - **I Clearly Have Enough** (`Mlie.IClearlyHaveEnough`, 2023661266, in the author's mod list) transpiles the same two readouts. It drops `ldfld Map.resourceCounter` and calls its own `GetPresentOnMap(Map, ThingDef)`, which counts everything on the map, stored or not. Tweaks' transpiler runs after it (`Priority.Last`, `after` its Harmony ID), finds that call by name, and swaps it for `PresentAcrossLevels`, which adds up that mod's count on every level. With both, the readouts count everything on every level.
  - Before this, the order was whichever patched first. If Tweaks ran first, I Clearly Have Enough still removed the counter while Tweaks' replacement expected it on the stack. If it ran second, it found no `GetCount` call and only logged a warning.
- **`ReadoutAcrossLevels`** (2026-11-22). The readout reads `Find.CurrentMap`'s counter. Transpilers swap its calls for `MultiFloorsReadout`'s, which add every other level's: `AllCountedAmounts` in `ResourceReadout.DoReadoutSimple` (the list it draws; a resource stocked only upstairs reads 0 on the ground and would be skipped), `GetCount` in `DrawResourceSimple` and `Listing_ResourceReadout.DoThingDef`, and `GetCountIn(ThingCategoryDef)` in `Listing_ResourceReadout.DoCategory`. The summed list is one dictionary refilled on each call, not cached per frame: headless, the frame count may not move between scenarios. Mods that replace the readout aren't covered.
- **`BillsAcrossLevels`** (2026-11-22). Vanilla's `RecipeWorkerCounter.CountProducts` counts on `bill.Map` only. MultiFloors fetches ingredients from other levels (`GlobalBillGiverMonitor`) but leaves this count alone.
  - A transpiler swaps every `Bill.Map` read in `CountProducts`, its private `GetCarriedCount`, and the butchering and stone block counters' overrides for `MultiFloorsBills.MapOf`: the bill's map, or the level being counted.
  - A postfix on each `CountProducts` then runs the same counter once per other level with that level set, and adds what it finds, so the bill's own rules (hit points, quality, tainted, equipped, carried) apply there too. Re-entering, the postfix returns at once.
  - A bill counting one stockpile (`GetIncludeSlotGroup`) is left alone, since that stockpile is on one level. Modded counters that override `CountProducts` still count one level.
- **`HemogenTilSet`** runs every tick, so it checks the 15000-tick interval before reading the setting. It skips pawns that aren't spawned (in caravans and transport pods, comps still tick).
- **`ShowArmorIndicators`** uses `MapHeld` and `PositionHeld`, because a killing blow despawns the pawn.
- **`DeteriorationAlertSkipTainted`** (2026-11-20) finds TD's alert by name, so Tweaks has no reference to TD's assembly, and logs a warning instead of patching if the type or method is gone. TD's alert lists every haulable, unforbidden, unfogged thing below half its hit points that deteriorates where it lies; the postfix removes tainted apparel from that list, and turns the alert off when nothing else is left. Block Unwanted Minutiae's "MessageDeterioratedAway - But only tainted apparel" option covers the separate vanilla message, by matching its text.
- **`ReGrowthExtinctNoDuplicateAnimals`** (2026-11-15): Extinct Animals (3820991509) declares `loadBefore` ReGrowth 2 (2260097569), so ReGrowth 2's `RG_Dodo`, `RG_Thrinaxodon` and `RG_Synthetion` defs override its trimmed ones, and their `wildBiomes` already cover every biome Extinct's `Patches/Core_limited.xml` adds them to, at the same values. Each animal then appears twice in those biomes and `BiomeDef.AllWildAnimals` throws "same key" (Giddy-Up logs it for `RG_Dodo` and skips its biome cache). Loading Extinct after ReGrowth 2 also clears the error, but contradicts its `loadBefore`, so RimSort reports a loop. Not yet covered by a scenario; checked only against the parent mods' files.

Style: 4 spaces in the C# files.

## Testing

**Profiles** (in `~/.gabs/config.json`):

- `tweaks` and `tweaks-headless` load Tweaks' hard dependencies only: Harmony, Core, Biotech, XML Extensions, WVC Work Modes, RimBridgeServer and Tweaks. Data folder `~/.gabs/rimworld/tweaks`.
- `tweaks-mods` and `tweaks-mods-headless` add Odyssey and every mod a tweak targets: Mechanoid Upgrades, Complex Jobs, Simple Improve, BiotechPatch, VSE, Medieval Tailor, VFE Power and Production, Vanilla Temperature, Mecha Weapon Changer, ReSplice, Dubs Bad Hygiene, Fantasy Biotech, Setosa's Power Tools and TD Enhancement Pack, plus Vanilla Expanded Framework, which several of them need: 24 mods in all. They're Workshop mods, so the Steam client must be running. Data folder `~/.gabs/rimworld/tweaks-mods`. It takes about a minute to start.
- `tweaks-multifloors` and `tweaks-multifloors-headless` are `tweaks` plus Prepatcher (`zetrith.prepatcher`, 2934420800, straight after Harmony; not needed on Waylith, but it matches the main mod list) and MultiFloors (after WVC), for the `levels` suite. Workshop mods, so Steam must be running. Data folder `~/.gabs/rimworld/tweaks-multifloors`, a copy of `tweaks`' with its save.
- The `tweaks` and `tweaks-mods` saves were made fresh, headless, from the main menu. Nothing is drawn by the tweaks that matters to the tests, so the headless profiles are enough.

Run `ordo/run_scenarios` on both. The scenarios are in `tools/BridgeTools/Scenarios/TweaksScenarios.cs`. A scenario for another mod's tweak requires that mod (`requiresMod`) and shows as skipped on `tweaks`.

| Suite | Scenarios | What they check |
|---|---|---|
| `setup` | `setup_no_load_errors`, `setup_setting_defaults` | The whole game log since startup has no error naming a def Tweaks adds or patches (patches apply and defs are checked at load, before any scenario, and XML Extensions reports "0 failed" for patches whose results then fail the def checks); the settings menu's defaults match the C# ones, and the removed settings are gone |
| `patches` | `patch_*`, one per patch | The patched def has the change: weight classes, recipes at each bench (`AllRecipes`), work types, passion and glove values, generator watts, minifiable with its category and a mass, the bench's facility link, the wells' place workers |
| `armor` | `armor_numbers`, `armor_numbers_killing_blow`, `armor_numbers_off` | A temporary Harmony prefix on `MoteMaker.ThrowText` records the floating text. Over 25 seeded hits each on a colonist in steel plate armor, one in a flak vest and one unarmored: a struck-through number exactly when the hit was deflected or reduced, white for metal armor and cyan otherwise, and a red number equal to the damage dealt. A killing blow still shows its damage on the map. With the setting off, nothing |
| `haul_mechs` | `haul_downed_mech_to_shutdown_zone`, `haul_downed_mech_off`, `haul_downed_mech_charger_free`, `haul_downed_mech_full_charger_free`, `haul_unpowered_mech_no_charger`, `haul_unpowered_mech_near_charger`, `haul_unpowered_mech_charger_free`, `haul_unpowered_mech_off` | A lifter with its moving parts missing is downed; a hauler carries it into a WVC shutdown zone (made by reflection) with the tweak's job. With the setting off, the work giver skips and the mech stays put. At 10% energy with a powered charger, the tweak has no job and vanilla's `HaulMechToCharger` puts it on the charger; at full energy, the tweak still takes it to the zone. A lifter at 0 energy shuts itself down: with no charger, a hauler carries it into the zone with the unpowered job, it shuts down again there, and isn't hauled again. With a charger on no power net, it goes on the zone cell nearest the charger. With a charger powered by a charged battery, the tweak has no job and vanilla takes it to the charger. With the setting off, the work giver skips |
| `levels` (MultiFloors) | `levels_downed_mech_carried_upstairs`, `levels_unpowered_mech_carried_upstairs`, `levels_own_level_first`, `levels_haul_off`, `levels_wvc_mode_walks_upstairs`, `levels_wvc_mode_off_stays` , `levels_build_costs_count_other_levels`, `levels_build_costs_off`, `levels_build_costs_clearly_have_enough`, `levels_readout_counts_other_levels`, `levels_bills_count_other_levels` | An up-stair spawned on the test map makes a level above it (`UpperLevel`, by reflection, in `tools/BridgeTools/Scenarios/UpperLevel.cs`, shared with the other mods' `levels` scenarios), floored around the exit since a new level is void elsewhere; it's removed afterwards, after destroying what's on it, since removing a level passes its pawns to the world. With a zone only upstairs, a hauler carries a downed and an unpowered lifter up with `Ordo_CarryMechToOtherLevel`, and the same-map job puts it in the zone (the unpowered one shuts down again there). With a zone on both levels, the ground one is used. With the setting off, there's no job. A lifter in WVC's Ambush mode, which looks for an ambush zone when there's no enemy, walks upstairs to the zone; with the setting off it stays on the ground. Chargers (`levels_*_to_charger_upstairs`, `levels_charger_upstairs_beats_zone`, `levels_parked_mech_to_charger_upstairs`, `levels_charger_off`): with a powered charger only upstairs, an unpowered lifter and a downed one at 10% are carried up and charge there, also over a zone on the ground and from that zone once parked in it; with the setting off, the giver skips. `levels_downed_mech_carried_downstairs` carries one down. `levels_hauler_fetches_mech_upstairs` and `_down`: the hauler on the ground, the mech upstairs, the zone upstairs or down. On failure, `CarriedTo` prints each change in the hauler's job and who holds the mech, and every job the hauler ended (a temporary Harmony prefix on `EndCurrentJob`) |
| `tools` | `tools_category`, `tools_category_power_tools` | The category holds exactly the weapons with a positive work-stat offset, each lists it, and the tree's caches include them. Vanilla and the other mods have none, so the second, requiring Setosa's Power Tools, fails on an empty category: with it there are 7 tools, and 4 weapons with other offsets left out |
| `hemogen` | `hemogen_below_cap_queues`, `hemogen_at_cap_waits`, `hemogen_off_ignores_cap` | The game clock is moved to the colonist's next 15000-tick interval and the comp ticks once: with no packs an extraction is queued, with 30 it isn't, and with the limit off it is |
| `deterioration` | `deterioration_alert_skips_tainted`, `deterioration_alert_only_tainted_inactive`, `deterioration_alert_off_counts_tainted` | Cloth parkas lie in the open at 40% hit points, one tainted and (in two of them) one clean, and a new TD alert's report is read directly, with TD's own alert switch forced on. With the setting on, the clean parka is listed and the tainted one isn't; with only the tainted one, the alert is inactive. With the setting off, the tainted one is listed |

How the scenarios are built:

- **Other mods' types are reached by reflection** (`AccessTools.TypeByName`): XML Extensions' `SettingsManager`, WVC's `Zone_MechanoidShutdown`, BiotechPatch's comp and settings, VSE's `PassionDef`, MultiFloors' stairs. BridgeTools needs no reference to them and loads in any profile.
- **Settings** are changed through `SettingsManager.SetSetting` and restored in a `finally`. Zones are deleted afterwards, since stage cleanup doesn't remove them.

**Results (2026-11-14):** 26/26 on `tweaks-mods-headless`, twice in a row in one game (about 1 s). On `tweaks-headless`, 11 pass and 15 skip, each naming its missing mod.

**With `HaulUnpoweredMechs` (2026-11-14):** 30/30 on `tweaks-mods-headless`, twice in one game. Then 32/32 with the downed-mech charger guard and its two scenarios, twice in one game. Not yet run on `tweaks-headless`. The first run failed both hauling scenarios on a logged "Could not reserve" error: the hauler still held the cell when it started the mech's shutdown job, now released first.

**With `DeteriorationAlertSkipTainted` (2026-11-20):** 35/35 on `tweaks-mods-headless`. Not yet run on `tweaks-headless`, where the three `deterioration` scenarios skip.

**With `ShutdownZonesAcrossLevels` (2026-11-21):** 23/23 on `tweaks-multifloors-headless` (all six `levels` scenarios), 17/17 on `tweaks-headless` and 35/35 on `tweaks-mods-headless`, where the `levels` scenarios skip; no errors in the launch logs. The first run failed both carry scenarios: the hauler dropped the mech at the foot of the stairs as `SwitchMap` ended its job, then took the stairs alone, and the same-map job couldn't reserve a mech on another map. Fixed with `carryThingAfterJob` and `keepCarryingThingOverride`. The other four `levels` scenarios haven't been seen to fail yet.

**With `ChargersAcrossLevels` and the fetch scenarios (2026-11-21):** `levels` 14/14 three times in one game on `tweaks-multifloors-headless`, then the whole suite 31/31; `setup` and `haul_mechs` 10/10 on `tweaks-headless`, no launch errors. On the way, the fetch and carry-down scenarios failed at random: moving the mechanitor to the level (despawn and respawn) undrafted it, and it hauled the mech itself before the hauler could. `UpperLevel.MoveHere` now drafts it again. The parked-mech scenario first failed setting up, as an unpowered mech shuts down where its energy need picks, which was outside the zone; the zone tweak now hauls it in first. Not yet seen to fail: the charger scenarios and the fetch ones.

**With `ResourcesAcrossLevels` (2026-11-22):** the whole suite 33/33 on `tweaks-multifloors-headless` with no leaks, and `setup` 2/2 on `tweaks-headless`, where the `levels` scenarios skip. Neither log has errors.
- `levels_build_costs_count_other_levels` stocks a wall's default material in a stockpile upstairs and steel on the ground. It checks that the count from either level is the sum, that the four methods are transpiled, and that a new wall designator keeps the default material.
- `levels_build_costs_off` checks that with the setting off only the ground counts, and the designator falls back to the steel as vanilla does. The two scenarios together show the patch changes what the game picks.
- Both also read each method as the game runs it (`PatchProcessor.GetCurrentInstructions`, after every mod's transpilers): every count goes through Tweaks, and none goes straight to vanilla's counter or I Clearly Have Enough's single-map count.
- `levels_build_costs_clearly_have_enough` requires I Clearly Have Enough as well as MultiFloors. It puts 60 loose steel upstairs, outside any stockpile, and checks that vanilla's counter doesn't see it, that the mod's does, and that `PresentAcrossLevels` from the ground adds both levels. It also checks that both readouts end up calling `PresentAcrossLevels`. I Clearly Have Enough isn't in any profile: for this run it was added to `tweaks-multifloors` after MultiFloors, and the whole suite passed 34/34 with no Tweaks warnings. The profile was then restored, so the scenario skips there.
- The drawn readouts weren't looked at in a windowed game.

**With `ReadoutAcrossLevels` and `BillsAcrossLevels` (2026-11-22):** the whole suite 35/35 on `tweaks-multifloors-headless` with no leaks, and `setup` 2/2 on `tweaks-headless`.
- `levels_readout_counts_other_levels` uses the build-cost scenarios' stock. From the ground, the count, the simple list and the material's category add the level's, the ground's own counter is unchanged, and each of the four readout methods, as the game runs it, counts only through Tweaks. With the setting off, only the ground counts.
- `levels_bills_count_other_levels`: a "Do until you have 8" simple meal bill on a fueled stove on the ground, with 2 meals stockpiled on the ground and 7 upstairs. It counts 9 and isn't worked; counting only the ground's stockpile it's 2; with the setting off it's 2 and is worked. With a hit point limit, which counts thing by thing, it's 2 across the levels and 1 with the setting off: vanilla counts each stack there as one. Its first run expected 9 there and failed. Each patched method is checked to read no `Bill.Map` directly.
- The readout wasn't looked at in a windowed game.

Every scenario was seen to fail (bar the `levels` ones noted above):

- **Before the fixes below:** `setup_no_load_errors` (the FSFMerge and mass errors), the four minifiable mass checks, and both tools scenarios.
- **C# faults by hot reload,** in two batches:
  - Metal and other armor colours swapped: `armor_numbers`.
  - Armor numbers ignore their setting: `armor_numbers_off`.
  - `Map` instead of `MapHeld`: `armor_numbers_killing_blow`.
  - The haul work giver ignores its setting: `haul_downed_mech_off`.
  - It makes no job: `haul_downed_mech_to_shutdown_zone`.
  - No pack cap: `hemogen_at_cap_waits`.
  - The unpowered work giver skips vanilla's charger check, and picks a random cell (one batch): `haul_unpowered_mech_charger_free` ("the tweak has no job while a charger is free") and `haul_unpowered_mech_near_charger` (put on (15, 0, 18), nearest (25, 0, 18)).
  - The downed-mech giver without the charger guard: `haul_downed_mech_charger_free` ("the tweak has no job for it while a charger is free"). Vanilla's giver (priority 90, scanned first) still won the actual haul, so only the direct check catches it.
  - The limit setting ignored (on its own): `hemogen_off_ignores_cap`.
  - The deterioration postfix ignores its setting: `deterioration_alert_off_counts_tainted`. It removes nothing: `deterioration_alert_skips_tainted` and `deterioration_alert_only_tainted_inactive` ("culprits: Cloth parka (normal 40% T)").
- **Every XML tweak and the tools category switched off** in the profile's saved settings, with the VSE and glove values set back to the parent mods' own: each `patch_*` scenario failed, saying what it found. For example, "the standard recharger takes light mechs: [Medium, Heavy, UltraHeavy]", and the VFE generators at 500 W and 2400 W.
- **`FSFMerge` unconditional again, and the cap's menu default set to 25:** `setup_no_load_errors` and `setup_setting_defaults`.

## History

- **2026-11-22, the resource list and bills across MultiFloors levels.** `ReadoutAcrossLevels` and `BillsAcrossLevels` (above). The scenarios' `UpperLevel` moved to its own file in BridgeTools for the other mods' MultiFloors scenarios.
- **2026-11-22, building costs across MultiFloors levels.** `ResourcesAcrossLevels` (above).
- **2026-11-21, chargers across MultiFloors levels.** `ChargersAcrossLevels` (above), and scenarios showing MultiFloors' work scan has a hauler fetch a mech from another level.
- **2026-11-21, shutdown zones across MultiFloors levels.** `ShutdownZonesAcrossLevels` (above). Both hauling tweaks now pick cells through `ShutdownCells`, with the hauler's reach and reservation instead of WVC's checks for the mech; the downed-mech tweak still picks a random cell, the unpowered one the cell nearest a charger.
- **2026-11-21, patching that can't take the rest down.** Every patch goes through `TryPatch` (repo `shared/Harmony`), so a missing target logs what stops working and the other patches still apply. Found while testing it: without BiotechPatch, dev mode logged a `TypeLoadException` for `Comp_HemogenFarm`, because `new HarmonyMethod(Prefix_Comp_HemogenFarm_CompTick)` cached the prefix as a delegate in a static field that `ReportProbablyMissingAttributes` reads. The prefix is now passed by name, and `setup_no_load_errors` fails on any type from a referenced mod that can't load. About.xml now lists every optional mod in `loadAfter`. 17/17 on `tweaks-headless` (18 skipped for missing mods) and 35/35 on `tweaks-mods-headless`.
- **2026-11-14, tests and the review's follow-up.** A review checked every tweak against its parent mod's installed version and RimWorld 1.7, then the new suite checked them in game.
  - Removed `UnlockAllTrees`: Progression: Agriculture already leaves trees unlocked (its `sowableCrops` excludes `IsTree`), so only VE Mushrooms' Timbercap was affected. Unlocks it made stay in existing saves.
  - Removed `VfeFactoryComponentResearchFix`: it now only let advanced components be made without Advanced Fabrication.
  - Removed the Alpha Animals uranium recipe at Medieval Overhaul's alchemy bench: Medieval Overhaul doesn't support 1.7, so it never loaded.
  - Fixed: `MechExtraWorkTypes` named Complex Jobs' `FSFMerge`, which is optional and off by default, causing a load error. It's now added only when that work type exists, and Complex Jobs is in `loadAfter` so its patches run first.
  - Fixed: making MU's upgrader, the VFE industrial generators and the VTE wall cooler and vent minifiable left them without a mass, a config error on 1.7.
  - Fixed: the tools category caught only 1 of Setosa's Power Tools' 7 tools, because it was built before Setosa applied their work offsets.
  - Kept as they were, against the review: ReSplice's gene vault and repository do need the minifiable patch (on 1.7, vanilla's `GeneBuildingBase` sets no `minifiedDef`), and the VFE fabrication bench patch works (the bench ends up minifiable, with a mass and no load error).
  - The csproj's references now have their real names.

## Checked and still needed (2026-11-14)

Recorded so the next review doesn't redo the work. The suite checks the results of all of these in game; these notes are why each is still needed upstream.

- **`MechExtraWorkTypes`, Complex Jobs:** Complex Jobs 1.7 moves vanilla's deliver work givers into `FSFDeliver` and never adds it to `mechEnabledWorkTypes`, so without this lifters stop delivering. `FSFMerge` only exists with Complex Jobs' "Merge Worktype" setting on.
- **`MechExtraWorkTypes`, Simple Improve:** constructoids still don't get `WorkType_Improving`.
- **`DubsWellsOnGravships`:** DBH 1.7 still lists `PlaceWorker_InvalidOverSubstructure` on both inlets.
- **`LimitHemogenFarming`:** BiotechPatch's `CompTick` still queues a bill every 15000 ticks, with no cap and no check for an existing bill.
- **`HaulDownedMechs`:** vanilla's `HaulMechsToCharger` only carries mechs that are out of power, and WVC 1.7 has no work giver for it.
- **`VfeBenchLinksFabricationCabinet`:** the bench's `linkableFacilities` is still only `ToolCabinet`.
- **Minifiable:** Mecha Weapon Changer, `MU_MechUpgrader`, ReSplice's four gene buildings and the VTE wall units set no `minifiedDef` upstream. VFE sets an empty `<minifiedDef/>` on the component fabrication bench on purpose; the patch still makes it minifiable.
- **`SmallUpgradesAtAdvancedAssembler`:** MU's craftable upgrades still list only the basic assembler.
- **VFE Power:** portables are still 500 W and industrial 2400 W. Only the portables are minifiable upstream.
- **Values:** VSE Critical's `learnRateFactorOther` is 0.25 and the blacksmith gloves' `WorkSpeedGlobal` is 0.2 upstream.
- **Vanilla 1.7:** `StandardRecharger` is still Medium/Heavy/UltraHeavy, `LargeMechGestator` has no light-mech recipes, and subcores are made only at the subcore encoder.

## Known issues and risks

- `Patches/VFE_Power.xml` has a TODO: align the fuel cost with the new output.
- Several parent mods don't declare 1.7 support: Mecha Weapon Changer, Simple Improve, BiotechPatch, Fantasy Biotech and Setosa's Power Tools. The tweaks on them only work while those mods still load. BiotechPatch matters most, because `HemogenTilSet` compiles against its assembly: if its types change, the prefix fails when it's patched.
- MultiFloors supports up to 1.6 and runs on 1.7 from its 1.6 folder. `MultiFloorsShutdown` uses its internals (`Toils_Stairs`, `CrossLevelMoveJobUtility`, Prepatcher fields such as `ReferencePosition`, and the private `ChangingLevelJobDefs.changingLevelJobDefs`), so a MultiFloors update can break it; the `levels` suite is the check.
- Changing Setosa's Power Tools' presets in game re-applies its offsets, but the tools category is only built at startup, so it catches up on the next restart.
