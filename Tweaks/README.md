# Tweaks

`Ordo.Tweaks`. A grab bag of small, individually switchable changes to the base game and to other mods: mechs, colony management, balance and compatibility fixes. Most are XML patches. A few are C#.

This README is for working on the mod. For building and deploying, load the repo's `rimworld-mod-dev` skill.

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
- Progression: Agriculture (3418017359)
- BiotechPatch (3461060466)

The last two are optional mods. The code that touches their types is kept in a separate `[MethodImpl(NoInlining)]` method, called only when the mod is active, so those types are only resolved when they exist.

## Settings

All settings are declared once in `mod/Defs/TweaksSettings.xml`, an XML Extensions `SettingsMenuDef` (`Ordo_TweaksSettings`).

- **XML patches** read them through `XmlExtensions.OptionalPatch` (on/off) or `UseSetting` (a value).
- **C# reads them** through `src/TweaksSettings.cs`, which wraps `SettingsManager.TryGetSetting`.
- **A default lives in three places:** the menu def, the patch's `defaultValue` and `TweaksSettings.cs`. Keep them in step. The menu def has a comment saying so.
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
| `MechExtraWorkTypes` | Lifters get Complex Jobs' merge and deliver work types; constructoids get Simple Improve's improving work type. The `VR_Construct_Lifter` and `VR_Construct_Constructoid` variants get the same when they exist | Complex Jobs, Simple Improve | `Patches/MechsAreCapable.xml` |
| `LimitHemogenFarming`, `HemogenPackCap` | BiotechPatch's "hemogen farm anyone" only queues extractions while the map has fewer packs than the cap (default 30) | BiotechPatch | `src/HemogenTilSet.cs`, a prefix on `Comp_HemogenFarm.CompTick` |
| `UnlockAllTrees` | Progression: Agriculture starts with every sowable tree unlocked | Progression: Agriculture | `src/UnlockAllTrees.cs`, a postfix on `GameComponent.FinalizeInit` |
| `ToolsCategory` | A "tools" stockpile category under Weapons, holding weapons whose equipped stat offsets help work | Always | `src/ToolsCategory.cs`, `Defs/ToolsCategoryDef.xml` |
| `VseCriticalLearnRateOther` | Vanilla Skills Expanded's Critical passion learns other skills at 0.75 (0.25-1) | VSE | `Patches/VSE_Critical_Passions.xml` |
| `BlacksmithGlovesWorkSpeed` | Medieval Tailor's blacksmith gloves give +25% global work speed (0-50%) | Medieval Tailor | `Patches/BlacksmithGlovesAreUseful.xml` |
| `VfeGeneratorRebalance`, `VfePortableGeneratorWatts`, `VfeIndustrialGeneratorWatts` | VFE Power's portable generators make 200 W and industrial ones 3000 W, instead of VFE's 500 W portables that beat the bigger ones | VFE Power | `Patches/VFE_Power.xml` |
| `Minifiable*` | Can be uninstalled and moved: Mecha Weapon Changer, MU's mech upgrader, ReSplice gene buildings, VFE's component fabrication bench, and Vanilla Temperature's wall units | The matching mod | `Patches/Minifiable*.xml`, `Patches/VFE_Temperature.xml` |
| `VfeBenchLinksFabricationCabinet` | VFE's component fabrication bench links to the fabrication cabinet | VFE Production | `Patches/VFE_AssemblyBenchLinkable.xml` |
| `DubsWellsOnGravships` | Dubs Bad Hygiene's water wells can be placed on gravship substructure | Dubs Bad Hygiene | `Patches/DubsOnGravships.xml` |
| `VfeFactoryComponentResearchFix` | VFE Factory's component assembly processes lose their research requirement | VFE Factory | `Patches/VFEF_ResearchWeirdness.xml` |

Not switchable: `Defs/AlphaAnimals_MedievalUranium.xml` adds a recipe to process Alpha Animals' uranium crystals at Medieval Overhaul's alchemy bench, when both mods are loaded.

Notes on the C# tweaks:

- **`ToolsCategory`** adds the defs in code, because weapons usually inherit `thingCategories`, so a patch can't match them on the same node as `equippedStatOffsets`. It also checks the stat's category, which leaves out things like the minigun's move speed penalty. The category tree is already resolved by then, so it updates the cached child lists itself.
- **`HemogenTilSet`** runs every tick, so it checks the 15000-tick interval before reading the setting. It skips pawns that aren't spawned (in caravans and transport pods, comps still tick).
- **`ShowArmorIndicators`** uses `MapHeld` and `PositionHeld`, because a killing blow despawns the pawn.

Style: 4 spaces in the C# files.

## Testing

There are no in-game scenarios or GABS profiles yet. Most tweaks are patches on other mods, so a suite would need a profile per group of mods. To set one up, follow `new-mod-setup.md` in the skill. A cheap first step would be a scenario per patch that checks the patched def has the change. Only the C# tweaks need behaviour scenarios.

## Known issues

- `Tweaks.csproj` gives three references the same `Include` name, `WVC_MoreMechanoidsWorkModes_Main`: the actual WVC assembly, plus Progression: Agriculture and BiotechPatch. It builds, because the hint paths are what matter, but the names are wrong and confusing.
- `Patches/VFE_Power.xml` has a TODO: align the fuel cost with the new output.
