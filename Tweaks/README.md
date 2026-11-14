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

The csproj compiles against three Workshop assemblies, all with `Private` false and falling back to the 1.6 folder where there's no 1.7 one:

- XML Extensions
- WVC Work Modes
- BiotechPatch (3461060466), an optional mod. The code that touches its types is kept in a separate `[MethodImpl(NoInlining)]` method, called only when the mod is active, so those types are only resolved when they exist.

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
| Fantasy Biotech (`VR_Construct_*`) | 3632363090 | 1.6 |
| Setosa's Power Tools | 3526160222 | 1.6 |

No decompiler is installed. To read a mod's IL, load `UnityStubs.dll` and `Assembly-CSharp.dll` from the game root with `Assembly.LoadFrom` in PowerShell, then the mod's DLL. `GetTypes()` throws a `ReflectionTypeLoadException`, but `Exception.Types` still holds most types, and `GetMethodBody().GetILAsByteArray()` with `Module.ResolveMember` is enough to see what a method calls.

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
| `HaulDownedMechs` | Haulers (and mechs that can haul) carry downed colony mechs to a WVC shutdown zone, where they can repair | Always | `src/HaulMechForRepair.cs`, `Defs/HaulMechsForRepair.xml` |
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

Notes on the tweaks:

- **Minifiable things need a mass on 1.7.** Anything haulable without an authored mass is a config error at load. The minifiable patches add one where the parent mod has none, compared by name so an upstream mass wins.
- **`ToolsCategory`** adds the defs in code, because weapons usually inherit `thingCategories`, so a patch can't match them on the same node as `equippedStatOffsets`. It also checks the stat's category, which leaves out things like the minigun's move speed penalty. It runs in a long event queued from `ExecuteWhenFinished`, after every static constructor and whatever they queued: Setosa's Power Tools applies its tools' work offsets from a long event queued in its own static constructor. The category tree is already resolved by then, so it updates the cached child lists itself.
- **`HemogenTilSet`** runs every tick, so it checks the 15000-tick interval before reading the setting. It skips pawns that aren't spawned (in caravans and transport pods, comps still tick).
- **`ShowArmorIndicators`** uses `MapHeld` and `PositionHeld`, because a killing blow despawns the pawn.

Style: 4 spaces in the C# files.

## Testing

**Profiles** (in `~/.gabs/config.json`):

- `tweaks` and `tweaks-headless` load Tweaks' hard dependencies only: Harmony, Core, Biotech, XML Extensions, WVC Work Modes, RimBridgeServer and Tweaks. Data folder `~/.gabs/rimworld/tweaks`.
- `tweaks-mods` and `tweaks-mods-headless` add Odyssey and every mod a tweak targets: Mechanoid Upgrades, Complex Jobs, Simple Improve, BiotechPatch, VSE, Medieval Tailor, VFE Power and Production, Vanilla Temperature, Mecha Weapon Changer, ReSplice, Dubs Bad Hygiene, Fantasy Biotech and Setosa's Power Tools, plus their dependencies (Vanilla Expanded Framework, ReplaceLib, SYR Processor Framework). They're Workshop mods, so the Steam client must be running. Data folder `~/.gabs/rimworld/tweaks-mods`. It takes about a minute to start.
- Both saves were made fresh, headless, from the main menu. Nothing is drawn by the tweaks that matters to the tests, so the headless profiles are enough.

Run `ordo/run_scenarios` on both. The scenarios are in `tools/BridgeTools/Scenarios/TweaksScenarios.cs`. A scenario for another mod's tweak requires that mod (`requiresMod`) and shows as skipped on `tweaks`.

| Suite | Scenarios | What they check |
|---|---|---|
| `setup` | `setup_no_load_errors`, `setup_setting_defaults` | The whole game log since startup has no error naming a def Tweaks adds or patches (patches apply and defs are checked at load, before any scenario, and XML Extensions reports "0 failed" for patches whose results then fail the def checks); the settings menu's defaults match the C# ones, and the removed settings are gone |
| `patches` | `patch_*`, one per patch | The patched def has the change: weight classes, recipes at each bench (`AllRecipes`), work types, passion and glove values, generator watts, minifiable with its category and a mass, the bench's facility link, the wells' place workers |
| `armor` | `armor_numbers`, `armor_numbers_killing_blow`, `armor_numbers_off` | A temporary Harmony prefix on `MoteMaker.ThrowText` records the floating text. Over 25 seeded hits each on a colonist in steel plate armor, one in a flak vest and one unarmored: a struck-through number exactly when the hit was deflected or reduced, white for metal armor and cyan otherwise, and a red number equal to the damage dealt. A killing blow still shows its damage on the map. With the setting off, nothing |
| `haul_mechs` | `haul_downed_mech_to_shutdown_zone`, `haul_downed_mech_off` | A lifter with its moving parts missing is downed; a hauler carries it into a WVC shutdown zone (made by reflection) with the tweak's job. With the setting off, the work giver skips and the mech stays put |
| `tools` | `tools_category`, `tools_category_power_tools` | The category holds exactly the weapons with a positive work-stat offset, each lists it, and the tree's caches include them. Vanilla and the other mods have none, so the second, requiring Setosa's Power Tools, fails on an empty category: with it there are 7 tools, and 4 weapons with other offsets left out |
| `hemogen` | `hemogen_below_cap_queues`, `hemogen_at_cap_waits`, `hemogen_off_ignores_cap` | The game clock is moved to the colonist's next 15000-tick interval and the comp ticks once: with no packs an extraction is queued, with 30 it isn't, and with the limit off it is |

How the scenarios are built:

- **Other mods' types are reached by reflection** (`AccessTools.TypeByName`): XML Extensions' `SettingsManager`, WVC's `Zone_MechanoidShutdown`, BiotechPatch's comp and settings, VSE's `PassionDef`. BridgeTools needs no reference to them and loads in any profile.
- **Settings** are changed through `SettingsManager.SetSetting` and restored in a `finally`. Zones are deleted afterwards, since stage cleanup doesn't remove them.

**Results (2026-11-14):** 26/26 on `tweaks-mods-headless`, twice in a row in one game (about 1 s). On `tweaks-headless`, 11 pass and 15 skip, each naming its missing mod.

Every scenario was seen to fail:

- **Before the fixes below:** `setup_no_load_errors` (the FSFMerge and mass errors), the four minifiable mass checks, and both tools scenarios.
- **C# faults by hot reload,** in two batches:
  - Metal and other armor colours swapped: `armor_numbers`.
  - Armor numbers ignore their setting: `armor_numbers_off`.
  - `Map` instead of `MapHeld`: `armor_numbers_killing_blow`.
  - The haul work giver ignores its setting: `haul_downed_mech_off`.
  - It makes no job: `haul_downed_mech_to_shutdown_zone`.
  - No pack cap: `hemogen_at_cap_waits`.
  - The limit setting ignored (on its own): `hemogen_off_ignores_cap`.
- **Every XML tweak and the tools category switched off** in the profile's saved settings, with the VSE and glove values set back to the parent mods' own: each `patch_*` scenario failed, saying what it found. For example, "the standard recharger takes light mechs: [Medium, Heavy, UltraHeavy]", and the VFE generators at 500 W and 2400 W.
- **`FSFMerge` unconditional again, and the cap's menu default set to 25:** `setup_no_load_errors` and `setup_setting_defaults`.

## History

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
- Changing Setosa's Power Tools' presets in game re-applies its offsets, but the tools category is only built at startup, so it catches up on the next restart.
