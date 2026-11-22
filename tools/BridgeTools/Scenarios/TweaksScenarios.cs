using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Most tweaks are XML patches, so most scenarios check the patched def has the change. A patch for another mod requires that
// mod, and shows as skipped on the tweaks profile, which has only Tweaks' hard dependencies; tweaks-mods loads them all. Other
// mods' types are reached by reflection, so BridgeTools needs no reference to them and loads without them. Settings are XML
// Extensions settings, changed through its SettingsManager and restored afterwards; the C# tweaks read them each time.
public static partial class TweaksScenarios
{
    private const string ModId = "Ordo.Tweaks";

    private const string HarmonyId = "Ordo.BridgeTools.TweaksScenarios";

    private const string MultiFloorsId = "telardo.MultiFloors";

    // Every def a tweak adds or patches, and the mod's own names, for finding the load errors a tweak caused
    private static readonly string[] Touched =
    {
        "Ordo.Tweaks", "Ordo_Tools", "Ordo_HaulMechForRepair", "HaulMechForRepair", "Ordo_HaulMechToShutdownZone", "StandardRecharger", "TableMachining", "SubcoreEncoder",
        "LargeMechGestator", "MU_BasicUpgradesAssembler", "MU_AdvancedUpgradesAssembler", "MU_MechUpgrader", "Mech_Lifter",
        "VR_Construct_Lifter", "Mech_Constructoid", "VR_Construct_Constructoid", "FSFMerge", "FSFDeliver", "WorkType_Improving",
        "VSE_Critical", "AMT_Handwear_BlacksmithGloves", "VFE_PortableWoodFiredGenerator", "VFE_PortableChemfuelPoweredGenerator",
        "VFE_IndustrialWoodFiredGenerator", "VFE_IndustrialChemfuelPoweredGenerator", "MechaWeaponChanger", "RS_GeneCentrifuge",
        "RS_XenoGermDuplicator", "VFE_ComponentFabricationBench", "VFE_FabricationCabinet", "VTE_WallMountedHeater",
        "VTE_WallMountedCooler", "VTE_WallMountedVent", "WaterWellInlet", "DeepWaterWellInlet", "Ordo_ToolWork", "ToolWorkDef", "JobGiver_FetchTool", "MainColonistBehaviorCore"
    };

    // Suites: "setup" for loading, "patches" for the XML patches, one per C# tweak ("armor", "haul_mechs", "tools", "hemogen", "deterioration"),
    // "levels" for shutdown zones, chargers and building costs across MultiFloors levels ("resources" for the costs alone), and "settings" for the scenarios that turn a tweak off
    public static readonly List<Scenario> All = new()
    {
        new("setup_no_load_errors", "setup", NoLoadErrors),
        new("setup_setting_defaults", "setup settings", SettingDefaults),

        new("patch_light_mechs_standard_charger", "patches", LightMechsStandardCharger),
        new("patch_mech_items_at_machining_table", "patches", MechItemsAtMachiningTable),
        new("patch_small_mechs_in_large_gestator", "patches", SmallMechsInLargeGestator),
        new("patch_mu_upgrades_and_upgrader", "patches", MechanoidUpgrades, requiresMod: "GoGaTio.MechanoidUpgrades"),
        new("patch_complex_jobs_lifter", "patches", ComplexJobsLifter, requiresMod: "FrozenSnowFox.ComplexJobs"),
        new("patch_simple_improve_constructoid", "patches", SimpleImproveConstructoid, requiresMod: "Zei33.SimpleImprove"),
        new("patch_vse_critical_passion", "patches", VseCriticalPassion, requiresMod: "vanillaexpanded.skills"),
        new("patch_blacksmith_gloves", "patches", BlacksmithGloves, requiresMod: "Tinda.Medieval.Tailor.Continued"),
        new("patch_vfe_power_generators", "patches", VfePowerGenerators, requiresMod: "VanillaExpanded.VFEPower"),
        new("patch_mecha_weapon_changer_minifiable", "patches", context => Minifiable(context, "BuildingsProduction", "MechaWeaponChanger"), requiresMod: "drilledhead.MechaWeaponChanger"),
        new("patch_resplice_minifiable", "patches", context => Minifiable(context, "BuildingsProduction", "RS_GeneCentrifuge", "RS_XenoGermDuplicator", "RS_GeneVault", "RS_GeneRepository"), requiresMod: "ReSplice.XOTR.Core"),
        new("patch_vfe_fabrication_bench", "patches", VfeFabricationBench, requiresMod: "VanillaExpanded.VFEProduction"),
        new("patch_vte_wall_units_minifiable", "patches", context => Minifiable(context, "BuildingsTemperature", "VTE_WallMountedHeater", "VTE_WallMountedCooler", "VTE_WallMountedVent"), requiresMod: "VanillaExpanded.Temperature"),
        new("patch_dubs_wells_on_gravships", "patches", DubsWellsOnGravships, requiresMod: "dubwise.dubsbadhygiene"),

        new("armor_numbers", "armor", ArmorNumbers),
        new("armor_numbers_killing_blow", "armor", ArmorNumbersKillingBlow),
        new("armor_numbers_off", "armor settings", ArmorNumbersOff),
        new("haul_downed_mech_to_shutdown_zone", "haul_mechs", HaulDownedMech),
        new("haul_downed_mech_off", "haul_mechs settings", HaulDownedMechOff),
        new("haul_downed_mech_charger_free", "haul_mechs", HaulDownedMechChargerFree),
        new("haul_downed_mech_full_charger_free", "haul_mechs", HaulDownedMechFullChargerFree),
        new("haul_unpowered_mech_no_charger", "haul_mechs", HaulUnpoweredMechNoCharger),
        new("haul_unpowered_mech_near_charger", "haul_mechs", HaulUnpoweredMechNearCharger),
        new("haul_unpowered_mech_charger_free", "haul_mechs", HaulUnpoweredMechChargerFree),
        new("haul_unpowered_mech_off", "haul_mechs settings", HaulUnpoweredMechOff),
        new("levels_downed_mech_carried_upstairs", "haul_mechs levels", LevelsDownedMechCarriedUpstairs, requiresMod: MultiFloorsId),
        new("levels_unpowered_mech_carried_upstairs", "haul_mechs levels", LevelsUnpoweredMechCarriedUpstairs, requiresMod: MultiFloorsId),
        new("levels_own_level_first", "haul_mechs levels", LevelsOwnLevelFirst, requiresMod: MultiFloorsId),
        new("levels_haul_off", "haul_mechs levels settings", LevelsHaulOff, requiresMod: MultiFloorsId),
        new("levels_wvc_mode_walks_upstairs", "levels", context => LevelsWorkModeWalks(context, on: true), requiresMod: MultiFloorsId),
        new("levels_wvc_mode_off_stays", "levels settings", context => LevelsWorkModeWalks(context, on: false), requiresMod: MultiFloorsId),
        new("levels_unpowered_mech_to_charger_upstairs", "haul_mechs levels", LevelsUnpoweredMechToCharger, requiresMod: MultiFloorsId),
        new("levels_downed_mech_to_charger_upstairs", "haul_mechs levels", LevelsDownedMechToCharger, requiresMod: MultiFloorsId),
        new("levels_charger_upstairs_beats_zone", "haul_mechs levels", LevelsChargerBeatsZone, requiresMod: MultiFloorsId),
        new("levels_parked_mech_to_charger_upstairs", "haul_mechs levels", LevelsParkedMechToCharger, requiresMod: MultiFloorsId),
        new("levels_charger_off", "haul_mechs levels settings", LevelsChargerOff, requiresMod: MultiFloorsId),
        new("levels_downed_mech_carried_downstairs", "haul_mechs levels", LevelsDownedMechCarriedDownstairs, requiresMod: MultiFloorsId),
        new("levels_hauler_fetches_mech_upstairs", "haul_mechs levels", context => LevelsHaulerFetchesMech(context, zoneUpstairs: true), requiresMod: MultiFloorsId),
        new("levels_hauler_fetches_mech_down", "haul_mechs levels", context => LevelsHaulerFetchesMech(context, zoneUpstairs: false), requiresMod: MultiFloorsId),
        new("levels_build_costs_count_other_levels", "levels resources", LevelsBuildCostsCountOtherLevels, requiresMod: MultiFloorsId),
        new("levels_build_costs_off", "levels resources settings", LevelsBuildCostsOff, requiresMod: MultiFloorsId),
        new("levels_build_costs_clearly_have_enough", "levels resources", LevelsBuildCostsClearlyHaveEnough, requiresMod: MultiFloorsId + ",Mlie.IClearlyHaveEnough"),
        new("levels_readout_counts_other_levels", "levels resources settings", LevelsReadoutCountsOtherLevels, requiresMod: MultiFloorsId),
        new("levels_bills_count_other_levels", "levels bills settings", LevelsBillsCountOtherLevels, requiresMod: MultiFloorsId),
        new("levels_supercharger_upstairs", "levels supercharger", context => LevelsSupercharger(context, on: true), requiresMod: MultiFloorsId + ",Ludeon.RimWorld.Ideology"),
        new("levels_supercharger_off", "levels supercharger settings", context => LevelsSupercharger(context, on: false), requiresMod: MultiFloorsId + ",Ludeon.RimWorld.Ideology"),
        new("tools_category", "tools", context => ToolsCategory(context, requireTools: false)),
        new("tools_category_power_tools", "tools", context => ToolsCategory(context, requireTools: true), requiresMod: "setosa.power.tools"),
        new("tools_work_jackhammer_away_for_walks", "tools tools_work", context => ToolsWalkWithoutJackhammer(context, on: true), requiresMod: PowerTools),
        new("tools_work_sidearms_alone_walks_with_jackhammer", "tools tools_work settings", context => ToolsWalkWithoutJackhammer(context, on: false), requiresMod: PowerTools),
        new("tools_work_plant_cutter_keeps_scythe", "tools tools_work", ToolsPlantCutterKeepsScythe, requiresMod: DarkAgesTools),
        new("tools_work_bonesaw_not_for_surgery", "tools tools_work", ToolsBonesawNotForSurgery, requiresMod: DarkAgesTools),
        new("tools_work_cleaner_uses_broom", "tools tools_work", context => ToolsCleanerUsesBroom(context, on: true), requiresMod: DarkAgesTools),
        new("tools_work_switch_off", "tools tools_work settings", context => ToolsCleanerUsesBroom(context, on: false), requiresMod: DarkAgesTools),
        new("tools_fetch_miner_fetches", "tools tools_work", context => ToolsMinerFetches(context, on: true), requiresMod: DarkAgesTools),
        new("tools_fetch_off", "tools tools_work settings", context => ToolsMinerFetches(context, on: false), requiresMod: DarkAgesTools),
        new("tools_fetch_skilled_miner_first", "tools tools_work", ToolsSkilledMinerFirst, requiresMod: DarkAgesTools),
        new("tools_fetch_outfit_bans", "tools tools_work", ToolsOutfitBans, requiresMod: DarkAgesTools + ",Snues.AutoArm"),
        new("tools_fetch_auto_arm_forced", "tools tools_work", ToolsAutoArmForced, requiresMod: DarkAgesTools + ",Snues.AutoArm"),
        new("tools_fetch_hand_back", "tools tools_work", ToolsHandBack, requiresMod: DarkAgesTools),
        new("tools_fetch_player_tool_kept", "tools tools_work", ToolsPlayerToolKept, requiresMod: DarkAgesTools),
        new("tools_work_jackhammer_kept_between_adjacent_rocks", "tools tools_work", context => ToolsWalkWithoutJackhammer(context, on: true, adjacent: true), requiresMod: PowerTools),
        new("tools_work_no_jackhammer_as_everyday_weapon", "tools tools_work settings", ToolsNoJackhammerAsEverydayWeapon, requiresMod: PowerTools),
        new("tools_work_side_jobs_no_switch", "tools tools_work", ToolsSideJobsNoSwitch, requiresMod: SidearmsId),
        new("tools_fetch_area_restricted", "tools tools_work", ToolsAreaRestricted, requiresMod: DarkAgesTools),
        new("tools_fetch_needs_autoswitch", "tools tools_work settings", ToolsFetchNeedsAutoswitch, requiresMod: DarkAgesTools),
        new("tools_fetch_copy_stays_fetched", "tools tools_work", ToolsCopyStaysFetched, requiresMod: DarkAgesTools),
        new("tools_fetch_taken_fetched_is_players", "tools tools_work", ToolsTakenFetchedIsPlayers, requiresMod: DarkAgesTools),
        new("tools_fetch_hand_back_needs_storage", "tools tools_work", ToolsHandBackNeedsStorage, requiresMod: DarkAgesTools),
        new("tools_fetch_skilled_work_first", "tools tools_work", ToolsSkilledWorkFirst, requiresMod: DarkAgesTools),
        new("tools_fetch_upgrade_at_cap", "tools tools_work settings", ToolsUpgradeAtCap, requiresMod: PowerTools + ",van.datools"),
        new("hemogen_below_cap_queues", "hemogen", context => Hemogen(context, packs: 0, limit: true, expectBill: true), requiresMod: "biotechpatch.1trickPwnyta"),
        new("hemogen_at_cap_waits", "hemogen", context => Hemogen(context, packs: 30, limit: true, expectBill: false), requiresMod: "biotechpatch.1trickPwnyta"),
        new("hemogen_off_ignores_cap", "hemogen settings", context => Hemogen(context, packs: 30, limit: false, expectBill: true), requiresMod: "biotechpatch.1trickPwnyta"),
        new("deterioration_alert_skips_tainted", "deterioration", context => DeteriorationAlert(context, skip: true, withClean: true), requiresMod: "MemeGoddess.TDPack"),
        new("deterioration_alert_only_tainted_inactive", "deterioration", context => DeteriorationAlert(context, skip: true, withClean: false), requiresMod: "MemeGoddess.TDPack"),
        new("deterioration_alert_off_counts_tainted", "deterioration settings", context => DeteriorationAlert(context, skip: false, withClean: true), requiresMod: "MemeGoddess.TDPack")
    };

    private static Type SettingsManager => AccessTools.TypeByName("XmlExtensions.SettingsManager");

    private static string GetSetting(
        string key
    )
    {
        var args = new object[] { ModId, key, null };
        return (bool)SettingsManager.GetMethod("TryGetSetting")!.Invoke(null, args)! ? (string)args[2] : null;
    }

    private static void SetSetting(
        string key,
        string value
    )
    {
        if (value == null)
        {
            SettingsManager.GetMethod("DeleteSetting")!.Invoke(null, new object[] { ModId, key });
            return;
        }

        SettingsManager.GetMethod("SetSetting")!.Invoke(null, new object[] { ModId, key, value });
    }

    // Runs with a setting changed, restoring it afterwards
    private static async Task WithSetting(
        string key,
        string value,
        Func<Task> run
    )
    {
        var was = GetSetting(key);
        SetSetting(key, value);
        try
        {
            await run();
        }
        finally
        {
            SetSetting(key, was);
        }
    }

    private static ThingDef Thing(
        ScenarioContext context,
        string defName
    )
    {
        var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
        context.Check($"{defName} exists", def != null);
        return def;
    }

    // The whole log since startup: the patches apply and their defs are checked at load, before any scenario runs
    private static Task NoLoadErrors(
        ScenarioContext context
    )
    {
        var errors = Log.Messages
            .Where(message => message.type == LogMessageType.Error)
            .Select(message => message.text)
            .Where(text => Touched.Any(name => text.Contains(name)))
            .Distinct()
            .ToList();
        context.Check($"no load errors about a def Tweaks touches: [{string.Join(" | ", errors.Take(6))}]", errors.Count == 0);

        // An optional mod's type reached while the mod is missing, such as a cached delegate's static field that dev mode's
        // ReportProbablyMissingAttributes reads. The error names only the other mod's assembly, not Tweaks.
        var referenced = typeof(Ordo.Tweaks.ShowArmorIndicators).Assembly.GetReferencedAssemblies().Select(name => $"from assembly '{name.Name},").ToList();
        var typeLoads = Log.Messages
            .Where(message => message.type == LogMessageType.Error)
            .Select(message => message.text)
            .Where(text => text.Contains("Could not load type") && referenced.Any(text.Contains))
            .Distinct()
            .ToList();
        context.Check($"no type from a mod Tweaks references failed to load: [{string.Join(" | ", typeLoads.Take(3))}]", typeLoads.Count == 0);
        context.Check("the log still holds its first messages, so nothing from startup was dropped", !Log.reachedMaxMessagesLimit);
        return Task.CompletedTask;
    }

    // A default lives in the settings menu, in TweaksSettings.cs and in each patch. The menu's must match the C# ones, which
    // are used when no setting is saved.
    private static Task SettingDefaults(
        ScenarioContext context
    )
    {
        var expected = new Dictionary<string, string>
        {
            ["ArmorIndicators"] = "true",
            ["DeteriorationAlertSkipTainted"] = "true",
            ["HaulDownedMechs"] = "true",
            ["HaulUnpoweredMechs"] = "true",
            ["ShutdownZonesAcrossLevels"] = "true",
            ["ChargersAcrossLevels"] = "true",
            ["ResourcesAcrossLevels"] = "true",
            ["ReadoutAcrossLevels"] = "true",
            ["BillsAcrossLevels"] = "true",
            ["ToolsCategory"] = "true",
            ["LimitHemogenFarming"] = "true",
            ["HemogenPackCap"] = "30",
            ["ToolsSwitchForWork"] = "true",
            ["ToolsFetch"] = "true",
            ["ToolsPerPawn"] = "2",
            ["ToolsMainWorkPriority"] = "2"
        };
        foreach (var (key, value) in expected)
        {
            var menu = DefaultSetting(key);

            // Settings for absent mods are left out of the menu, so they have no default
            if (key.StartsWith("Hemogen") || key == "LimitHemogenFarming")
            {
                if (!ModsConfig.IsActive("biotechpatch.1trickPwnyta"))
                {
                    continue;
                }
            }

            if (key.StartsWith("Tools") && key != "ToolsCategory" && !ModsConfig.IsActive(SidearmsId))
            {
                continue;
            }

            if (key == "DeteriorationAlertSkipTainted" && !ModsConfig.IsActive("MemeGoddess.TDPack"))
            {
                continue;
            }

            if (key is "ShutdownZonesAcrossLevels" or "ChargersAcrossLevels" or "ResourcesAcrossLevels" or "ReadoutAcrossLevels" or "BillsAcrossLevels" && !ModsConfig.IsActive(MultiFloorsId))
            {
                continue;
            }

            context.Check($"{key}: the menu's default ({menu ?? "none"}) matches the C# default ({value})", menu != null && string.Equals(menu, value, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var removed in new[] { "UnlockAllTrees", "VfeFactoryComponentResearchFix" })
        {
            context.Check($"{removed} was removed with its tweak", DefaultSetting(removed) == null);
        }

        return Task.CompletedTask;
    }

    // The settings menu's default for a key, or null when the menu has no such setting (XML Extensions throws for those)
    private static string DefaultSetting(
        string key
    )
    {
        try
        {
            return (string)SettingsManager.GetMethod("GetDefaultValue")!.Invoke(null, new object[] { ModId, key });
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static Task LightMechsStandardCharger(
        ScenarioContext context
    )
    {
        var charger = Thing(context, "StandardRecharger");
        var classes = charger?.building.requiredMechWeightClasses?.Select(weight => weight.defName).ToList() ?? new List<string>();
        context.Check($"the standard recharger takes light mechs: [{string.Join(", ", classes)}]", classes.Contains("Light"));
        return Task.CompletedTask;
    }

    private static Task MechItemsAtMachiningTable(
        ScenarioContext context
    )
    {
        var encoder = Thing(context, "SubcoreEncoder");
        var table = Thing(context, "TableMachining");
        if (encoder == null || table == null)
        {
            return Task.CompletedTask;
        }

        var missing = encoder.AllRecipes.Where(recipe => !table.AllRecipes.Contains(recipe)).Select(recipe => recipe.defName).ToList();
        context.Check($"the subcore encoder has recipes: {encoder.AllRecipes.Count}", encoder.AllRecipes.Count > 0);
        context.Check($"every subcore encoder recipe is also at the machining table; missing: [{string.Join(", ", missing)}]", missing.Count == 0);
        return Task.CompletedTask;
    }

    private static Task SmallMechsInLargeGestator(
        ScenarioContext context
    )
    {
        var small = Thing(context, "MechGestator");
        var large = Thing(context, "LargeMechGestator");
        if (small == null || large == null)
        {
            return Task.CompletedTask;
        }

        var missing = small.AllRecipes.Where(recipe => !large.AllRecipes.Contains(recipe)).Select(recipe => recipe.defName).ToList();
        context.Check($"the mech gestator has recipes: {small.AllRecipes.Count}", small.AllRecipes.Count > 0);
        context.Check($"every mech gestator recipe is also at the large one; missing: [{string.Join(", ", missing)}]", missing.Count == 0);
        return Task.CompletedTask;
    }

    private static Task MechanoidUpgrades(
        ScenarioContext context
    )
    {
        var basic = Thing(context, "MU_BasicUpgradesAssembler");
        var advanced = Thing(context, "MU_AdvancedUpgradesAssembler");
        if (basic != null && advanced != null)
        {
            var missing = basic.AllRecipes.Where(recipe => !advanced.AllRecipes.Contains(recipe)).Select(recipe => recipe.defName).ToList();
            context.Check($"the basic assembler has recipes: {basic.AllRecipes.Count}", basic.AllRecipes.Count > 0);
            context.Check($"every basic assembler recipe is also at the advanced one; missing: [{string.Join(", ", missing)}]", missing.Count == 0);
        }

        return Minifiable(context, "BuildingsProduction", "MU_MechUpgrader");
    }

    private static List<string> WorkTypes(
        ThingDef mech
    )
    {
        return mech?.race.mechEnabledWorkTypes?.Select(work => work.defName).ToList() ?? new List<string>();
    }

    // Complex Jobs always has its deliver work type. Its merge work type is optional and off by default, so lifters get it only
    // when it exists. Fantasy Biotech's lifter gets the same.
    private static Task ComplexJobsLifter(
        ScenarioContext context
    )
    {
        var merge = DefDatabase<WorkTypeDef>.GetNamedSilentFail("FSFMerge") != null;
        foreach (var defName in new[] { "Mech_Lifter", "VR_Construct_Lifter" })
        {
            var lifter = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (lifter == null)
            {
                context.Check("only Fantasy Biotech's lifter can be missing", defName == "VR_Construct_Lifter" && !ModsConfig.IsActive("vr.fantasy.biotech"));
                continue;
            }

            var work = WorkTypes(lifter);
            context.Check($"{defName} can deliver: [{string.Join(", ", work)}]", work.Contains("FSFDeliver"));
            context.Check($"{defName} can merge exactly when Complex Jobs' merge work type exists ({merge}): [{string.Join(", ", work)}]", work.Contains("FSFMerge") == merge);
        }

        return Task.CompletedTask;
    }

    private static Task SimpleImproveConstructoid(
        ScenarioContext context
    )
    {
        foreach (var defName in new[] { "Mech_Constructoid", "VR_Construct_Constructoid" })
        {
            var constructoid = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (constructoid == null)
            {
                context.Check("only Fantasy Biotech's constructoid can be missing", defName == "VR_Construct_Constructoid" && !ModsConfig.IsActive("vr.fantasy.biotech"));
                continue;
            }

            var work = WorkTypes(constructoid);
            context.Check($"{defName} can improve: [{string.Join(", ", work)}]", work.Contains("WorkType_Improving"));
        }

        return Task.CompletedTask;
    }

    private static Task VseCriticalPassion(
        ScenarioContext context
    )
    {
        var type = AccessTools.TypeByName("VSE.Passions.PassionDef");
        var critical = type == null ? null : GenDefDatabase.GetDefSilentFail(type, "VSE_Critical");
        var rate = critical == null ? null : AccessTools.Field(type, "learnRateFactorOther")?.GetValue(critical);
        context.Check($"VSE's Critical passion learns other skills at 0.75: {rate ?? "not found"}", rate is float value && Mathf.Approximately(value, 0.75f));
        return Task.CompletedTask;
    }

    private static Task BlacksmithGloves(
        ScenarioContext context
    )
    {
        var gloves = Thing(context, "AMT_Handwear_BlacksmithGloves");
        var speed = gloves?.equippedStatOffsets?.Where(offset => offset.stat == StatDefOf.WorkSpeedGlobal).Select(offset => offset.value).ToList() ?? new List<float>();
        context.Check($"the blacksmith gloves give exactly one +25% global work speed: [{string.Join(", ", speed)}]", speed.Count == 1 && Mathf.Approximately(speed[0], 0.25f));
        return Task.CompletedTask;
    }

    private static Task VfePowerGenerators(
        ScenarioContext context
    )
    {
        foreach (var (defName, watts) in new[]
                 {
                     ("VFE_PortableWoodFiredGenerator", 200f), ("VFE_PortableChemfuelPoweredGenerator", 200f),
                     ("VFE_IndustrialWoodFiredGenerator", 3000f), ("VFE_IndustrialChemfuelPoweredGenerator", 3000f)
                 })
        {
            var power = Thing(context, defName)?.GetCompProperties<CompProperties_Power>();
            context.Check($"{defName} makes {watts} W: {(power == null ? "no power comp" : -power.PowerConsumption + " W")}", power != null && Mathf.Approximately(-power.PowerConsumption, watts));
        }

        return Minifiable(context, "BuildingsPower", "VFE_IndustrialWoodFiredGenerator", "VFE_IndustrialChemfuelPoweredGenerator");
    }

    // Can be uninstalled, sits in the given stockpile category, and has a mass, which 1.7 needs for anything haulable
    private static Task Minifiable(
        ScenarioContext context,
        string category,
        params string[] defNames
    )
    {
        foreach (var defName in defNames)
        {
            var def = Thing(context, defName);
            if (def == null)
            {
                continue;
            }

            var categories = def.thingCategories?.Select(thingCategory => thingCategory.defName).ToList() ?? new List<string>();
            context.Check($"{defName} can be uninstalled: minifiedDef={def.minifiedDef?.defName ?? "none"}", def.Minifiable);
            context.Check($"{defName} is in {category}: [{string.Join(", ", categories)}]", categories.Contains(category));
            context.Check($"{defName} has a mass: {def.GetStatValueAbstract(StatDefOf.Mass)}", def.statBases?.Any(stat => stat.stat == StatDefOf.Mass && stat.value > 0f) == true);
        }

        return Task.CompletedTask;
    }

    private static Task VfeFabricationBench(
        ScenarioContext context
    )
    {
        var bench = Thing(context, "VFE_ComponentFabricationBench");
        var links = bench?.GetCompProperties<CompProperties_AffectedByFacilities>()?.linkableFacilities?.Select(facility => facility.defName).ToList() ?? new List<string>();
        context.Check($"the component fabrication bench links to the fabrication cabinet: [{string.Join(", ", links)}]", links.Contains("VFE_FabricationCabinet"));
        return Minifiable(context, "BuildingsProduction", "VFE_ComponentFabricationBench");
    }

    private static Task DubsWellsOnGravships(
        ScenarioContext context
    )
    {
        foreach (var defName in new[] { "WaterWellInlet", "DeepWaterWellInlet" })
        {
            var workers = Thing(context, defName)?.placeWorkers?.Select(worker => worker.Name).ToList() ?? new List<string>();
            context.Check($"{defName} can go on substructure: [{string.Join(", ", workers)}]", !workers.Contains("PlaceWorker_InvalidOverSubstructure"));
        }

        return Task.CompletedTask;
    }

    // Floating text the game threw, recorded by a temporary patch on MoteMaker.ThrowText. Headless, motes aren't drawn, but
    // the call is still made.
    private static readonly List<(string Text, Color Color, Map Map)> Texts = new();

    private static void RecordText(
        string text,
        Color color,
        Map map
    )
    {
        Texts.Add((text, color, map));
    }

    private static async Task RecordingTexts(
        Func<Task> run
    )
    {
        var harmony = new Harmony(HarmonyId);
        var throwText = AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.ThrowText), new[] { typeof(Vector3), typeof(Map), typeof(string), typeof(Color), typeof(float) });
        Texts.Clear();
        try
        {
            harmony.Patch(throwText, prefix: new HarmonyMethod(typeof(TweaksScenarios), nameof(RecordText)));
            await run();
        }
        finally
        {
            harmony.UnpatchAll(HarmonyId);
        }
    }

    private static bool Struck(
        string text
    )
    {
        return text.Contains('̶');
    }

    private static Pawn Armored(
        ScenarioContext context,
        int x,
        string apparel,
        ThingDef stuff
    )
    {
        var pawn = context.Colonist(x, 0);
        pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(ThingDef.Named(apparel), stuff), false);
        return pawn;
    }

    // For every hit: armor that deflected or reduced it shows the blocked amount struck through, white for metal armor and
    // cyan otherwise, and damage that got through shows in red
    private static Task ArmorNumbers(
        ScenarioContext context
    )
    {
        var problems = new List<string>();
        var seen = new HashSet<string>();
        return RecordingTexts(() =>
        {
            Rand.PushState(4242);
            try
            {
                foreach (var pawn in new[]
                         {
                             Armored(context, -2, "Apparel_PlateArmor", ThingDefOf.Steel),
                             Armored(context, 2, "Apparel_FlakVest", null),
                             context.Colonist(6, 0)
                         })
                {
                    var torso = pawn.RaceProps.body.corePart;
                    for (var hit = 0; hit < 25 && !pawn.Dead; hit++)
                    {
                        Texts.Clear();
                        var result = pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, hitPart: torso));
                        var blocked = result.deflected || result.diminished;
                        var struck = Texts.Where(text => Struck(text.Text)).ToList();
                        var red = Texts.Where(text => !Struck(text.Text)).ToList();
                        var metalBlock = result.deflectedByMetalArmor || result.diminishedByMetalArmor;

                        if (blocked != (struck.Count == 1))
                        {
                            problems.Add($"blocked={blocked} but {struck.Count} struck numbers");
                        }
                        else if (blocked)
                        {
                            seen.Add(metalBlock ? "white" : "cyan");
                            if (struck[0].Color != (metalBlock ? Color.white : Color.cyan))
                            {
                                problems.Add($"blocked by {(metalBlock ? "metal" : "other")} armor shown in {struck[0].Color}");
                            }
                        }

                        var dealt = result.totalDamageDealt >= 0.01f;
                        if (dealt != (red.Count == 1) || dealt && (red[0].Color != Color.red || red[0].Text != result.totalDamageDealt.ToString("F0")))
                        {
                            problems.Add($"dealt {result.totalDamageDealt} but shown [{string.Join(", ", red.Select(text => text.Text))}]");
                        }
                        else if (dealt)
                        {
                            seen.Add("red");
                        }
                    }

                }
            }
            finally
            {
                Rand.PopState();
            }

            context.Check($"every hit's numbers matched its result: [{string.Join("; ", problems.Distinct().Take(5))}]", problems.Count == 0);
            context.Check($"metal (white), other armor (cyan) and damage (red) numbers were all seen: [{string.Join(", ", seen)}]", seen.SetEquals(new[] { "white", "cyan", "red" }));
            return Task.CompletedTask;
        });
    }

    // A killing blow despawns the pawn, so the numbers go where its corpse is
    private static Task ArmorNumbersKillingBlow(
        ScenarioContext context
    )
    {
        var pawn = context.Colonist(0, 0);
        return RecordingTexts(() =>
        {
            pawn.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 500f, hitPart: pawn.RaceProps.body.corePart));
            context.Check("the pawn died", pawn.Dead);
            context.Check($"the killing blow's damage was shown on the map: [{string.Join(", ", Texts.Select(text => text.Text))}]", Texts.Any(text => !Struck(text.Text) && text.Map == context.Map));
            return Task.CompletedTask;
        });
    }

    private static Task ArmorNumbersOff(
        ScenarioContext context
    )
    {
        var pawn = Armored(context, 0, "Apparel_PlateArmor", ThingDefOf.Steel);
        return WithSetting("ArmorIndicators", "false", () => RecordingTexts(() =>
        {
            for (var hit = 0; hit < 5; hit++)
            {
                pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, hitPart: pawn.RaceProps.body.corePart));
            }

            context.Check($"with the setting off, no numbers: [{string.Join(", ", Texts.Select(text => text.Text))}]", Texts.Count == 0);
            return Task.CompletedTask;
        }));
    }

    // A WVC shutdown zone, made by reflection since BridgeTools has no reference to WVC. Delete it afterwards: stage cleanup
    // doesn't remove zones.
    private static Zone ShutdownZone(
        ScenarioContext context,
        IEnumerable<IntVec3> cells,
        Map map = null
    )
    {
        map ??= context.Map;
        var type = AccessTools.TypeByName("WVC_WorkModes.Zone_MechanoidShutdown");
        var zone = (Zone)Activator.CreateInstance(type, map.zoneManager);
        map.zoneManager.RegisterZone(zone);
        foreach (var cell in cells)
        {
            zone.AddCell(cell);
        }

        return zone;
    }

    // Downed by losing what it moves with, as a fight would leave it
    private static Pawn DownedMech(
        ScenarioContext context,
        int x,
        int z
    )
    {
        var mechanitor = context.Mechanitor(-12, 6);
        var mech = context.ControlledMech("Mech_Lifter", mechanitor, x, z);
        foreach (var part in mech.health.hediffSet.GetNotMissingParts().Where(part => part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)).ToList())
        {
            mech.health.AddHediff(HediffDefOf.MissingBodyPart, part);
        }

        context.Check($"the mech is downed: {Stage.Describe(mech)}", mech.Downed && !mech.Dead);
        return mech;
    }

    private static async Task HaulDownedMech(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        var cells = CellRect.CenteredOn(context.Cell(10, 0), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            var hauled = false;
            await context.Until($"the hauler takes the mech to the zone: hauler {Stage.Describe(hauler)}; mech at {mech.PositionHeld}", () =>
            {
                hauled |= hauler.CurJobDef?.defName == "HaulMechForRepair";
                return mech.Spawned && cells.Contains(mech.Position);
            }, 2500);
            context.Check("it was the tweak's job that moved it", hauled);
        }
        finally
        {
            zone.Delete();
        }
    }

    private static async Task HaulDownedMechOff(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        var start = mech.Position;
        var zone = ShutdownZone(context, CellRect.CenteredOn(context.Cell(10, 0), 1).Cells);
        try
        {
            await WithSetting("HaulDownedMechs", "false", async () =>
            {
                var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
                var giver = DefDatabase<WorkGiverDef>.GetNamed("Ordo_HaulMechForRepair").Worker;
                context.Check("with the setting off, the work giver skips", giver.ShouldSkip(hauler));
                await context.Step(600);
                context.Check($"the mech stayed where it was: {mech.PositionHeld}, hauler {Stage.Describe(hauler)}", mech.Spawned && mech.Position == start);
            });
        }
        finally
        {
            zone.Delete();
        }
    }

    // A light-mech charger on a power net: a charged battery nearby, which it connects to by itself
    private static async Task<Building_MechCharger> FreeCharger(
        ScenarioContext context,
        Pawn mech,
        int x,
        int z
    )
    {
        var charger = (Building_MechCharger)context.Build(ThingDef.Named("BasicRecharger"), x, z);
        var battery = context.Build(ThingDef.Named("Battery"), x + 2, z);
        battery.GetComp<CompPowerBattery>().SetStoredEnergyPct(1f);
        await context.Until($"the charger is powered: net {charger.PowerComp.PowerNet != null}", () => charger.CanPawnChargeCurrently(mech), 300, 30);
        return charger;
    }

    private static WorkGiver_Scanner DownedGiver => (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("Ordo_HaulMechForRepair").Worker;

    // Vanilla's HaulMechsToCharger takes a downed mech below its recharge limit to a free charger, so the downed-mech tweak leaves
    // it alone instead of carrying it away to the zone
    private static async Task HaulDownedMechChargerFree(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        mech.needs.energy.CurLevelPercentage = 0.1f;
        var charger = await FreeCharger(context, mech, 6, 6);
        var cells = CellRect.CenteredOn(context.Cell(10, 0), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            context.Check("the tweak has no job for it while a charger is free", !DownedGiver.HasJobOnThing(hauler, mech));
            var tweak = false;
            var vanilla = false;
            await context.Until($"the hauler takes the mech to the charger: hauler {Stage.Describe(hauler)}; mech {Stage.Describe(mech)} at {mech.PositionHeld}", () =>
            {
                tweak |= hauler.CurJobDef?.defName == "HaulMechForRepair";
                vanilla |= hauler.CurJobDef == JobDefOf.HaulMechToCharger;
                return mech.Spawned && hauler.carryTracker.CarriedThing == null && (mech.Position == charger.InteractionCell || cells.Contains(mech.Position));
            }, 2500);
            context.Check("the tweak's job never ran", !tweak);
            context.Check("vanilla's HaulMechToCharger carried it", vanilla);
            context.Check($"the mech is at the charger, not in the zone: {mech.Position}, charger at {charger.InteractionCell}", mech.Position == charger.InteractionCell);
        }
        finally
        {
            zone.Delete();
        }
    }

    // At full energy a downed mech isn't vanilla's to take to a charger, so the tweak still carries it to the zone
    private static async Task HaulDownedMechFullChargerFree(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        await FreeCharger(context, mech, 6, 6);
        var cells = CellRect.CenteredOn(context.Cell(10, 0), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            var hauled = false;
            await context.Until($"the hauler takes the mech to the zone: hauler {Stage.Describe(hauler)}; mech at {mech.PositionHeld}", () =>
            {
                hauled |= hauler.CurJobDef?.defName == "HaulMechForRepair";
                return mech.Spawned && cells.Contains(mech.Position);
            }, 2500);
            context.Check("it was the tweak's job that moved it", hauled);
        }
        finally
        {
            zone.Delete();
        }
    }

    private static WorkGiver_Scanner UnpoweredGiver => (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("Ordo_HaulMechToShutdownZone").Worker;

    // A light mech (a lifter) out of power: its energy need puts it into self-shutdown on its next interval
    private static async Task<Pawn> UnpoweredMech(
        ScenarioContext context,
        int x,
        int z
    )
    {
        var mechanitor = context.Mechanitor(-12, 6);
        var mech = context.ControlledMech("Mech_Lifter", mechanitor, x, z);
        mech.needs.energy.CurLevel = 0f;
        await context.Until($"the mech shuts down for lack of power: {Stage.Describe(mech)}", () => mech.needs.energy.IsLowEnergySelfShutdown, 900, 30);
        context.Check($"it's not downed: {Stage.Describe(mech)}", !mech.Downed);
        return mech;
    }

    // The hauler carries it to the zone with the tweak's job, and it shuts down there. Once in the zone, it isn't hauled again.
    private static async Task<bool> HauledToZone(
        ScenarioContext context,
        Pawn mech,
        Pawn hauler,
        ICollection<IntVec3> cells
    )
    {
        var hauled = false;
        var arrived = await context.Until($"the hauler takes the mech to the zone: hauler {Stage.Describe(hauler)}; mech at {mech.PositionHeld}", () =>
        {
            hauled |= hauler.CurJobDef?.defName == "Ordo_HaulMechToShutdownZone";
            return mech.Spawned && cells.Contains(mech.Position) && hauler.carryTracker.CarriedThing == null;
        }, 2500);
        context.Check("it was the tweak's job that moved it", hauled);
        await context.Until($"it shuts down again where it was put: {Stage.Describe(mech)}", () => mech.needs.energy.IsLowEnergySelfShutdown, 300, 30);
        context.Check($"it's still in the zone: {mech.Position}", cells.Contains(mech.Position));
        context.Check("in the zone, it isn't hauled again", !UnpoweredGiver.HasJobOnThing(hauler, mech));
        return arrived;
    }

    private static async Task HaulUnpoweredMechNoCharger(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        var cells = CellRect.CenteredOn(context.Cell(10, 0), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            await HauledToZone(context, mech, hauler, cells);
        }
        finally
        {
            zone.Delete();
        }
    }

    // A charger with no power net can't charge anything, so vanilla has nowhere to take the mech. The tweak picks the zone cell
    // nearest that charger.
    private static async Task HaulUnpoweredMechNearCharger(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, -6);
        var charger = (Building_MechCharger)context.Build(ThingDef.Named("BasicRecharger"), 10, 6);
        context.Check("the charger can't charge the mech", !charger.CanPawnChargeCurrently(mech));
        var cells = Enumerable.Range(-8, 18).Select(x => context.Cell(x, 2)).ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var nearest = cells.OrderBy(cell => cell.DistanceToSquared(charger.InteractionCell)).First();
            var hauler = context.Worker(-6, -6, WorkTypeDefOf.Hauling);
            if (await HauledToZone(context, mech, hauler, cells))
            {
                context.Check($"it was put on the zone cell nearest the charger: {mech.Position}, nearest {nearest}, charger at {charger.InteractionCell}", mech.Position == nearest);
            }
        }
        finally
        {
            zone.Delete();
        }
    }

    // A charged battery puts the charger on a power net, so vanilla's HaulMechsToCharger takes the mech to it, and the tweak stays
    // out of the way, zone or not.
    private static async Task HaulUnpoweredMechChargerFree(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        await FreeCharger(context, mech, 6, 6);
        var cells = CellRect.CenteredOn(context.Cell(10, 0), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            context.Check("the tweak has no job while a charger is free", !UnpoweredGiver.HasJobOnThing(hauler, mech));
            var tweak = false;
            await context.Until($"the hauler takes the mech to the charger: hauler {Stage.Describe(hauler)}; mech {Stage.Describe(mech)}", () =>
            {
                tweak |= hauler.CurJobDef?.defName == "Ordo_HaulMechToShutdownZone";
                return mech.CurJobDef == JobDefOf.MechCharge;
            }, 2500);
            context.Check("the tweak's job never ran", !tweak);
        }
        finally
        {
            zone.Delete();
        }
    }

    private static async Task HaulUnpoweredMechOff(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        var start = mech.Position;
        var zone = ShutdownZone(context, CellRect.CenteredOn(context.Cell(10, 0), 1).Cells);
        try
        {
            await WithSetting("HaulUnpoweredMechs", "false", async () =>
            {
                var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
                context.Check("with the setting off, the work giver skips", UnpoweredGiver.ShouldSkip(hauler));
                await context.Step(600);
                context.Check($"the mech stayed where it was: {mech.PositionHeld}, hauler {Stage.Describe(hauler)}", mech.Spawned && mech.Position == start);
            });
        }
        finally
        {
            zone.Delete();
        }
    }

    // A hauler on the ground carries the mech upstairs with the tweak's cross-level job, then the same-map one puts it in the
    // zone there. jobName is the same-map job.
    private static Task<bool> CarriedUpstairs(
        ScenarioContext context,
        Pawn mech,
        Pawn hauler,
        UpperLevel level,
        ICollection<IntVec3> cells,
        string jobName
    ) => CarriedTo(context, mech, hauler, level.Map, "the zone upstairs", jobName, crossLevel: true,
        () => mech.Spawned && mech.Map == level.Map && cells.Contains(mech.Position));

    // A hauler takes the mech to destMap, where jobName (a same-map job) finishes the trip and done becomes true. With
    // crossLevel, the tweak's cross-level job must have carried it there; without, it mustn't have.
    private static async Task<bool> CarriedTo(
        ScenarioContext context,
        Pawn mech,
        Pawn hauler,
        Map destMap,
        string destination,
        string jobName,
        bool crossLevel,
        Func<bool> done
    )
    {
        var carried = false;
        var hauled = false;
        // Each change in what the hauler is doing, and who holds the mech, for when it goes wrong
        var trace = new List<string>();
        _tracedHauler = hauler;
        TracedJobEnds.Clear();
        var harmony = new Harmony(HarmonyId);
        harmony.Patch(AccessTools.Method(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.EndCurrentJob)),
            prefix: new HarmonyMethod(typeof(TweaksScenarios), nameof(RecordJobEnd)));
        bool arrived;
        try
        {
            arrived = await context.Until(
                $"the hauler takes the mech to {destination}: hauler {Stage.Describe(hauler)} on {hauler.MapHeld}; mech {Stage.Describe(mech)} on {mech.MapHeld}",
                () =>
                {
                    var holder = (mech.ParentHolder as Pawn_CarryTracker)?.pawn;
                    var state = $"{hauler.CurJobDef?.defName}/{hauler.jobs?.curDriver?.CurToilIndex} at {hauler.PositionHeld} on {hauler.MapHeld}, "
                                + $"mech held by {(holder == null ? "nobody" : $"{holder.LabelShort} ({holder.CurJobDef?.defName})")}";
                    if (trace.Count == 0 || trace[^1] != state)
                    {
                        trace.Add(state);
                    }

                    carried |= hauler.CurJobDef?.defName == "Ordo_CarryMechToOtherLevel";
                    hauled |= hauler.CurJobDef?.defName == jobName && hauler.Map == destMap;
                    return done() && hauler.carryTracker.CarriedThing == null;
                }, 4000);
        }
        finally
        {
            harmony.UnpatchAll(HarmonyId);
            _tracedHauler = null;
        }

        var traced = $"hauler's trace: {string.Join(" | ", trace.Take(30))}; jobs ended: {string.Join(" | ", TracedJobEnds.Take(20))}";
        context.Check(traced, arrived && carried == crossLevel && hauled);
        context.Check(crossLevel ? "the cross-level job carried it" : "the cross-level job wasn't needed", carried == crossLevel);
        context.Check($"{jobName} took it to {destination}", hauled);
        return arrived;
    }

    // With chargers on the ground taken or missing, a hauler carries the mech up to the free one upstairs, where it charges
    private static Task<bool> ChargedUpstairs(
        ScenarioContext context,
        Pawn mech,
        Pawn hauler,
        UpperLevel level
    ) => CarriedTo(context, mech, hauler, level.Map, "the charger upstairs", "Ordo_DeliverMechToCharger", crossLevel: true,
        () => mech.Spawned && mech.Map == level.Map && mech.CurJobDef == JobDefOf.MechCharge);

    private static WorkGiver_Scanner ChargerGiver => (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.GetNamed("Ordo_HaulMechToChargerOnOtherLevel").Worker;

    private static async Task LevelsUnpoweredMechToCharger(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        using var level = new UpperLevel(context);
        await level.FreeCharger(context, mech);
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        await ChargedUpstairs(context, mech, hauler, level);
    }

    // Vanilla takes a downed mech below its recharge limit to a free charger, so the tweak does the same across levels
    private static async Task LevelsDownedMechToCharger(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        mech.needs.energy.CurLevelPercentage = 0.1f;
        using var level = new UpperLevel(context);
        await level.FreeCharger(context, mech);
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        await ChargedUpstairs(context, mech, hauler, level);
    }

    // A free charger upstairs wins over a zone on the mech's own level: charging beats waiting
    private static async Task LevelsChargerBeatsZone(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        using var level = new UpperLevel(context);
        await level.FreeCharger(context, mech);
        var zone = ShutdownZone(context, CellRect.CenteredOn(context.Cell(-6, -8), 1).Cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            await ChargedUpstairs(context, mech, hauler, level);
        }
        finally
        {
            zone.Delete();
        }
    }

    // A mech already waiting in a zone on the ground, which the zone tweaks leave alone, still goes to a charger that's
    // free upstairs
    private static async Task LevelsParkedMechToCharger(
        ScenarioContext context
    )
    {
        var cells = CellRect.CenteredOn(context.Cell(-6, -8), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            // Hauled into the zone by the zone tweak, as in play: left to itself it shuts down wherever its energy need
            // picks, which can be just outside
            var mech = await UnpoweredMech(context, 0, 0);
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            if (!await HauledToZone(context, mech, hauler, cells))
            {
                return;
            }

            using var level = new UpperLevel(context);
            await level.FreeCharger(context, mech);
            await ChargedUpstairs(context, mech, hauler, level);
        }
        finally
        {
            zone.Delete();
        }
    }

    private static async Task LevelsChargerOff(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        var start = mech.Position;
        using var level = new UpperLevel(context);
        await level.FreeCharger(context, mech);
        await WithSetting("ChargersAcrossLevels", "false", async () =>
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            context.Check("with the setting off, the work giver skips", ChargerGiver.ShouldSkip(hauler));
            await context.Step(600);
            context.Check($"the mech stayed where it was: {mech.PositionHeld} on {mech.MapHeld}", mech.Spawned && mech.Map == context.Map && mech.Position == start);
        });
    }

    // A wall's default material, stocked only upstairs, and another material stocked on the ground: what the Architect
    // shows on the ground, and the material a new wall designator picks. Stockpiles are counted, so both are stockpiled.
    private sealed class LevelStock : IDisposable
    {
        public readonly UpperLevel Level;
        public readonly ThingDef Wall = ThingDefOf.Wall;
        public readonly ThingDef Upstairs, Ground;

        private readonly ScenarioContext _context;
        private readonly Zone_Stockpile _groundZone;

        public LevelStock(
            ScenarioContext context
        )
        {
            _context = context;
            Level = new UpperLevel(context);
            Upstairs = GenStuff.DefaultStuffFor(Wall);
            Ground = Upstairs == ThingDefOf.Steel ? ThingDefOf.WoodLog : ThingDefOf.Steel;
            if (Level.Map == null)
            {
                return;
            }

            Stockpile(Level.Map, context.Cell(10, 10));
            GenSpawn.Spawn(Stack(Upstairs), context.Cell(10, 10), Level.Map);
            _groundZone = Stockpile(context.Map, context.Cell(-10, -10));
            context.Place(Ground, -10, -10, stackCount: 75);
            context.Map.resourceCounter.UpdateResourceCounts();
            Level.Map.resourceCounter.UpdateResourceCounts();
        }

        private static Thing Stack(
            ThingDef def
        )
        {
            var thing = ThingMaker.MakeThing(def);
            thing.stackCount = 75;
            return thing;
        }

        public static Zone_Stockpile Stockpile(
            Map map,
            IntVec3 center
        )
        {
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (var cell in CellRect.CenteredOn(center, 1))
            {
                zone.AddCell(cell);
            }

            return zone;
        }

        public int Count(
            Map map
        ) => Ordo.Tweaks.MultiFloorsResources.GetCount(map.resourceCounter, Upstairs);

        public ThingDef DefaultStuff => new Designator_Build(Wall).StuffDef;

        public void Dispose()
        {
            _groundZone?.Delete();
            Level.Dispose();
            _context.Map.resourceCounter.UpdateResourceCounts();
        }
    }

    private static async Task LevelsBuildCostsCountOtherLevels(
        ScenarioContext context
    )
    {
        using var stock = new LevelStock(context);
        if (stock.Level.Map == null)
        {
            return;
        }

        var ground = context.Map.resourceCounter.GetCount(stock.Upstairs);
        var upstairs = stock.Level.Map.resourceCounter.GetCount(stock.Upstairs);
        context.Check($"the level's counter has the {stock.Upstairs.label}: {upstairs}", upstairs >= 75);
        context.Check($"counted from the ground, both levels count: {stock.Count(context.Map)} = {ground} + {upstairs}", stock.Count(context.Map) == ground + upstairs);
        context.Check($"counted from upstairs, both levels count: {stock.Count(stock.Level.Map)}", stock.Count(stock.Level.Map) == ground + upstairs);

        foreach (var (method, name) in new (MethodBase, string)[]
                 {
                     (AccessTools.Method(typeof(Designator_Build), "DrawPlaceMouseAttachments"), "Designator_Build.DrawPlaceMouseAttachments"),
                     (AccessTools.Method(typeof(Designator_Build), nameof(Designator_Build.DrawPanelReadout)), "Designator_Build.DrawPanelReadout"),
                     (AccessTools.PropertyGetter(typeof(Designator_Build), nameof(Designator_Build.StuffDef)), "Designator_Build.StuffDef"),
                     (AccessTools.Method(typeof(Designator_Paint), nameof(Designator_Paint.DrawMouseAttachments)), "Designator_Paint.DrawMouseAttachments")
                 })
        {
            context.Check($"{name} is transpiled by Tweaks", Harmony.GetPatchInfo(method)?.Transpilers.Any(patch => patch.owner == ModId) == true);
            CountsAcrossLevels(context, method, name);
        }

        // Vanilla swaps the default material for one in stock on the ground when it's short there
        context.Check($"the current map is the test map: {Find.CurrentMap}", Find.CurrentMap == context.Map);
        context.Check($"the ground has too little {stock.Upstairs.label} for a wall: {ground}", ground < stock.Wall.CostStuffCount);
        context.Check($"a new wall defaults to the {stock.Upstairs.label} upstairs: {stock.DefaultStuff?.label}", stock.DefaultStuff == stock.Upstairs);

        await Task.CompletedTask;
    }

    // The method as the game runs it, after every mod's transpilers: each count goes through Tweaks, none straight to
    // vanilla's counter or I Clearly Have Enough's single-map count
    private static void CountsAcrossLevels(
        ScenarioContext context,
        MethodBase method,
        string name
    )
    {
        var called = PatchProcessor.GetCurrentInstructions(method)
            .Select(instruction => instruction.operand as MethodInfo)
            .Where(m => m != null)
            .ToList();
        var ours = called.Count(m => m.DeclaringType?.FullName == "Ordo.Tweaks.MultiFloorsResources");
        var direct = called.Where(m => m == AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCount))
            || m.Name == "GetPresentOnMap").Select(m => $"{m.DeclaringType?.Name}.{m.Name}").ToList();
        context.Check($"{name} counts through Tweaks: {ours} calls, uncounted levels: [{string.Join(", ", direct)}]", ours > 0 && direct.Count == 0);
    }

    // I Clearly Have Enough counts everything on a map, stored or not, in the two cost readouts. With it, Tweaks adds up its
    // count over the levels: loose steel upstairs, outside any stockpile, counts from the ground.
    private static Task LevelsBuildCostsClearlyHaveEnough(
        ScenarioContext context
    )
    {
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return Task.CompletedTask;
        }

        var steel = ThingDefOf.Steel;
        var loose = ThingMaker.MakeThing(steel);
        loose.stackCount = 60;
        GenSpawn.Spawn(loose, context.Cell(10, 10), level.Map);
        var component = AccessTools.TypeByName("IClearlyHaveEnough.AllResourcesCounter_MapComponent");
        context.Check("found I Clearly Have Enough's map component", component != null);
        if (component == null)
        {
            return Task.CompletedTask;
        }

        int Present(Map map)
        {
            var comp = map.components.FirstOrDefault(c => component.IsInstanceOfType(c));
            AccessTools.Method(component, "UpdateResourceCounts").Invoke(comp, null);
            return (int)AccessTools.Method(component, "GetCount").Invoke(comp, new object[] { steel });
        }

        var ground = Present(context.Map);
        var upstairs = Present(level.Map);
        level.Map.resourceCounter.UpdateResourceCounts();
        context.Check($"the steel upstairs isn't stored, so vanilla's counter doesn't see it: {level.Map.resourceCounter.GetCount(steel)}", level.Map.resourceCounter.GetCount(steel) < 60);
        context.Check($"I Clearly Have Enough sees it upstairs: {upstairs}", upstairs >= 60);
        var across = Ordo.Tweaks.MultiFloorsResources.PresentAcrossLevels(context.Map, steel);
        context.Check($"from the ground, both levels count: {across} = {ground} + {upstairs}", across == ground + upstairs);

        foreach (var (method, name) in new (MethodBase, string)[]
                 {
                     (AccessTools.Method(typeof(Designator_Build), "DrawPlaceMouseAttachments"), "Designator_Build.DrawPlaceMouseAttachments"),
                     (AccessTools.Method(typeof(Designator_Build), nameof(Designator_Build.DrawPanelReadout)), "Designator_Build.DrawPanelReadout")
                 })
        {
            context.Check($"{name} is transpiled by I Clearly Have Enough", Harmony.GetPatchInfo(method)?.Transpilers.Any(patch => patch.owner == "com.chippedchap.iclearlyhaveenough") == true);
            CountsAcrossLevels(context, method, name);
            context.Check($"{name} calls the whole-map count across levels",
                PatchProcessor.GetCurrentInstructions(method).Any(instruction => (instruction.operand as MethodInfo)?.Name == nameof(Ordo.Tweaks.MultiFloorsResources.PresentAcrossLevels)));
        }

        return Task.CompletedTask;
    }

    private static async Task LevelsBuildCostsOff(
        ScenarioContext context
    )
    {
        using var stock = new LevelStock(context);
        if (stock.Level.Map == null)
        {
            return;
        }

        var ground = context.Map.resourceCounter.GetCount(stock.Upstairs);
        await WithSetting("ResourcesAcrossLevels", "false", () =>
        {
            context.Check($"with the setting off, only the ground counts: {stock.Count(context.Map)} = {ground}", stock.Count(context.Map) == ground);
            context.Check($"the ground has too little {stock.Upstairs.label} for a wall: {ground}", ground < stock.Wall.CostStuffCount);
            context.Check($"{stock.Ground.label} can stand in for the default", stock.Ground.stuffProps.canSuggestUseDefaultStuff);
            context.Check($"a new wall falls back to the {stock.Ground.label} on the ground, as vanilla: {stock.DefaultStuff?.label}", stock.DefaultStuff == stock.Ground);

            return Task.CompletedTask;
        });
    }

    // Down rather than up: the hauler and the mech upstairs, the zone on the ground
    private static async Task LevelsDownedMechCarriedDownstairs(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        var mechanitor = mech.GetOverseer();
        using var level = new UpperLevel(context);
        level.MoveHere(mechanitor, context.Cell(3, 3));
        level.MoveHere(mech, context.Cell(4, 11));
        var cells = CellRect.CenteredOn(context.Cell(-6, -8), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            level.MoveHere(hauler, context.Cell(12, 12));
            context.Check("the tweak has a job for the zone downstairs", DownedGiver.HasJobOnThing(hauler, mech));
            await CarriedTo(context, mech, hauler, context.Map, "the zone on the ground", "HaulMechForRepair", crossLevel: true,
                () => mech.Spawned && mech.Map == context.Map && cells.Contains(mech.Position));
        }
        finally
        {
            zone.Delete();
        }
    }

    // While CarriedTo watches a hauler: each job it ends, with how and at which toil, since a job that fails as it starts
    // never shows in the per-tick trace
    private static Pawn _tracedHauler;
    private static readonly List<string> TracedJobEnds = new();

    public static void RecordJobEnd(
        Verse.AI.Pawn_JobTracker __instance,
        Verse.AI.JobCondition condition
    )
    {
        if (__instance.curJob != null && AccessTools.Field(typeof(Verse.AI.Pawn_JobTracker), "pawn").GetValue(__instance) == _tracedHauler)
        {
            TracedJobEnds.Add($"{__instance.curJob.def.defName} {condition} at toil {__instance.curDriver?.CurToilIndex} on {_tracedHauler.MapHeld}");
        }
    }

    // MultiFloors' own work scan: a hauler with nothing to do on its level tries the work givers from a stair on each other
    // level, then takes the stairs to do the job it found. A downed mech upstairs, with a zone upstairs (the same-map job)
    // or on the ground (the cross-level one, planned from upstairs)
    private static async Task LevelsHaulerFetchesMech(
        ScenarioContext context,
        bool zoneUpstairs
    )
    {
        var mech = DownedMech(context, 0, 0);
        var mechanitor = mech.GetOverseer();
        using var level = new UpperLevel(context);
        level.MoveHere(mechanitor, context.Cell(3, 3));
        level.MoveHere(mech, context.Cell(4, 11));
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        if (zoneUpstairs)
        {
            var cells = level.ZoneCells(context);
            ShutdownZone(context, cells, level.Map);
            await CarriedTo(context, mech, hauler, level.Map, "the zone upstairs", "HaulMechForRepair", crossLevel: false,
                () => mech.Spawned && mech.Map == level.Map && cells.Contains(mech.Position));
            return;
        }

        var groundCells = CellRect.CenteredOn(context.Cell(-6, -8), 1).Cells.ToList();
        var zone = ShutdownZone(context, groundCells);
        try
        {
            await CarriedTo(context, mech, hauler, context.Map, "the zone on the ground", "HaulMechForRepair", crossLevel: true,
                () => mech.Spawned && mech.Map == context.Map && groundCells.Contains(mech.Position));
        }
        finally
        {
            zone.Delete();
        }
    }

    private static async Task LevelsDownedMechCarriedUpstairs(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        using var level = new UpperLevel(context);
        var cells = level.ZoneCells(context);
        ShutdownZone(context, cells, level.Map);
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        await CarriedUpstairs(context, mech, hauler, level, cells, "HaulMechForRepair");
    }

    private static async Task LevelsUnpoweredMechCarriedUpstairs(
        ScenarioContext context
    )
    {
        var mech = await UnpoweredMech(context, 0, 0);
        using var level = new UpperLevel(context);
        var cells = level.ZoneCells(context);
        ShutdownZone(context, cells, level.Map);
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        if (await CarriedUpstairs(context, mech, hauler, level, cells, "Ordo_HaulMechToShutdownZone"))
        {
            await context.Until($"it shuts down again where it was put: {Stage.Describe(mech)}", () => mech.needs.energy.IsLowEnergySelfShutdown, 300, 30);
            context.Check($"it's still in the zone upstairs: {mech.Position} on {mech.Map}", mech.Map == level.Map && cells.Contains(mech.Position));
        }
    }

    // A zone on the mech's own level wins over one on another level
    private static async Task LevelsOwnLevelFirst(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        using var level = new UpperLevel(context);
        ShutdownZone(context, level.ZoneCells(context), level.Map);
        var cells = CellRect.CenteredOn(context.Cell(-6, -8), 1).Cells.ToList();
        var zone = ShutdownZone(context, cells);
        try
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            var carried = false;
            await context.Until($"the hauler takes the mech to the zone on the ground: hauler {Stage.Describe(hauler)}; mech at {mech.PositionHeld} on {mech.MapHeld}", () =>
            {
                carried |= hauler.CurJobDef?.defName == "Ordo_CarryMechToOtherLevel";
                return mech.Spawned && mech.Map == context.Map && cells.Contains(mech.Position);
            }, 2500);
            context.Check("it wasn't carried upstairs", !carried);
        }
        finally
        {
            zone.Delete();
        }
    }

    private static async Task LevelsHaulOff(
        ScenarioContext context
    )
    {
        var mech = DownedMech(context, 0, 0);
        var start = mech.Position;
        using var level = new UpperLevel(context);
        ShutdownZone(context, level.ZoneCells(context), level.Map);
        await WithSetting("ShutdownZonesAcrossLevels", "false", async () =>
        {
            var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
            context.Check("with the setting off, there's no job for a zone upstairs", !DownedGiver.HasJobOnThing(hauler, mech));
            await context.Step(600);
            context.Check($"the mech stayed where it was: {mech.PositionHeld} on {mech.MapHeld}", mech.Spawned && mech.Map == context.Map && mech.Position == start);
        });
    }

    // WVC's Ambush mode sends a mech to an ambush shutdown zone when no enemy is about. With none on its level, it takes the
    // stairs to the one upstairs; with the setting off, it shuts down where it is.
    private static async Task LevelsWorkModeWalks(
        ScenarioContext context,
        bool on
    )
    {
        var mechanitor = context.Mechanitor(-12, 6);
        var mech = context.ControlledMech("Mech_Lifter", mechanitor, 0, 0);
        using var level = new UpperLevel(context);
        var cells = level.ZoneCells(context);
        ShutdownZone(context, cells, level.Map);
        await WithSetting("ShutdownZonesAcrossLevels", on ? "true" : "false", async () =>
        {
            mechanitor.mechanitor.GetControlGroup(mech).SetWorkMode(DefDatabase<MechWorkModeDef>.GetNamed("WVC_Ambush"));
            if (on)
            {
                await context.Until($"the mech walks upstairs to the zone: {Stage.Describe(mech)} at {mech.PositionHeld} on {mech.MapHeld}",
                    () => mech.Spawned && mech.Map == level.Map && cells.Contains(mech.Position), 4000);
            }
            else
            {
                await context.Step(1500);
                context.Check($"it stayed on the ground: {Stage.Describe(mech)} on {mech.MapHeld}", mech.Map == context.Map);
            }
        });
    }

    // Weapons that raise a work stat when equipped, and nothing else, are in the tools category, wherever they came from.
    // Vanilla and the other tested mods have no such weapon, so the check is only meaningful with a mod that adds tools:
    // requireTools makes an empty category fail.
    private static Task ToolsCategory(
        ScenarioContext context,
        bool requireTools
    )
    {
        var tools = DefDatabase<ThingCategoryDef>.GetNamedSilentFail("Ordo_Tools");
        context.Check("the tools category exists, under Weapons", tools?.parent == ThingCategoryDefOf.Weapons);
        if (tools == null)
        {
            return Task.CompletedTask;
        }

        bool IsTool(
            ThingDef def
        )
        {
            return def.IsWeapon && def.equippedStatOffsets?.Any(offset => offset.value > 0f && offset.stat.category == StatCategoryDefOf.PawnWork) == true;
        }

        var expected = DefDatabase<ThingDef>.AllDefsListForReading.Where(def => IsTool(def) && def.thingCategories != null).ToList();
        var listed = tools.childThingDefs;
        var otherOffsets = DefDatabase<ThingDef>.AllDefsListForReading.Count(def => def.IsWeapon && def.equippedStatOffsets?.Count > 0 && !IsTool(def));
        context.Check($"the category holds exactly the work tools: {listed.Count} listed, {expected.Count} expected ({otherOffsets} weapons with other offsets left out)", listed.Count == expected.Count && expected.All(listed.Contains));
        context.Check("each tool lists the category itself", expected.All(def => def.thingCategories.Contains(tools)));
        context.Check("the tree's caches include them, so stockpiles see them", expected.All(def => tools.DescendantThingDefs.Contains(def) && ThingCategoryDefOf.Weapons.DescendantThingDefs.Contains(def)));
        context.Check("the sorted list matches", tools.SortedChildThingDefs.Count == listed.Count);
        context.Check($"there are tools to check: {expected.Count}", !requireTools || expected.Count > 0);
        return Task.CompletedTask;
    }

    // BiotechPatch queues a hemogen extraction on a farmed colonist every 15000 ticks. The tweak stops at the pack cap (30 by
    // default). The game clock is moved to the colonist's next 15000-tick interval, then the comp ticks once.
    private static Task Hemogen(
        ScenarioContext context,
        int packs,
        bool limit,
        bool expectBill
    )
    {
        var settingsType = AccessTools.TypeByName("BiotechPatch.BiotechPatchSettings");
        var anyone = (MemberInfo)AccessTools.Field(settingsType, "HemogenFarmAnyone") ?? AccessTools.Property(settingsType, "HemogenFarmAnyone");
        if (anyone == null)
        {
            context.Fail("BiotechPatchSettings.HemogenFarmAnyone wasn't found");
            return Task.CompletedTask;
        }

        object GetAnyone()
        {
            return anyone is FieldInfo field ? field.GetValue(null) : ((PropertyInfo)anyone).GetValue(null);
        }

        void SetAnyone(
            object value
        )
        {
            if (anyone is FieldInfo field)
            {
                field.SetValue(null, value);
            }
            else
            {
                ((PropertyInfo)anyone).SetValue(null, value);
            }
        }

        var pawn = context.Colonist(0, 0);
        pawn.drafter.Drafted = false;
        var comp = pawn.AllComps.FirstOrDefault(candidate => candidate.GetType().Name == "Comp_HemogenFarm");
        if (comp == null)
        {
            context.Fail("the colonist has no Comp_HemogenFarm");
            return Task.CompletedTask;
        }

        AccessTools.Field(comp.GetType(), "hemogenFarmEnabled").SetValue(comp, true);
        var placed = 0;
        var packX = 3;
        while (placed < packs)
        {
            var stack = Math.Min(packs - placed, ThingDefOf.HemogenPack.stackLimit);
            context.Place(ThingDefOf.HemogenPack, packX++, 0, stackCount: stack);
            placed += stack;
        }

        var wasAnyone = GetAnyone();
        SetAnyone(true);
        return WithSetting("LimitHemogenFarming", limit ? "true" : "false", () =>
        {
            try
            {
                var ticks = Find.TickManager.TicksGame + 1;
                while ((ticks + pawn.HashOffset()) % 15000 != 0)
                {
                    ticks++;
                }

                Find.TickManager.ticksGameInt = ticks;
                comp.CompTick();
                var billed = pawn.BillStack.Bills.Any(bill => bill.recipe == RecipeDefOf.ExtractHemogenPack);
                var onMap = context.Map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack).Sum(thing => thing.stackCount);
                context.Check($"with {onMap} packs on the map and the limit {(limit ? "on" : "off")}, an extraction was {(billed ? "" : "not ")}queued", billed == expectBill);
            }
            finally
            {
                SetAnyone(wasAnyone);
                pawn.BillStack.Clear();
            }

            return Task.CompletedTask;
        });
    }

    // TD Enhancement Pack's alert lists unforbidden things outside, below half their hit points. Two parkas lie in the open at 40%:
    // one tainted, and optionally one clean. A fresh alert's report is read directly, since alerts only update on their own cycle.
    private static Task DeteriorationAlert(
        ScenarioContext context,
        bool skip,
        bool withClean
    )
    {
        var alertType = AccessTools.TypeByName("TD_Enhancement_Pack.Alert_Deterioration");
        if (alertType == null)
        {
            context.Fail("TD_Enhancement_Pack.Alert_Deterioration wasn't found");
            return Task.CompletedTask;
        }

        Apparel Parka(
            int x,
            bool tainted
        )
        {
            var parka = (Apparel)context.Place(ThingDef.Named("Apparel_Parka"), x, 0, ThingDefOf.Cloth);
            parka.HitPoints = (int)(parka.MaxHitPoints * 0.4f);
            parka.WornByCorpse = tainted;
            return parka;
        }

        var taintedParka = Parka(3, tainted: true);
        var cleanParka = withClean ? Parka(5, tainted: false) : null;
        context.Check(
            $"the tainted parka deteriorates where it lies (rate {SteadyEnvironmentEffects.FinalDeteriorationRate(taintedParka):0.##})",
            SteadyEnvironmentEffects.FinalDeteriorationRate(taintedParka) > 0
        );

        // TD's own switch for the alert, on by default
        var tdSettings = AccessTools.Field(AccessTools.TypeByName("TD_Enhancement_Pack.Mod"), "settings")?.GetValue(null);
        var tdAlertOn = tdSettings == null ? null : AccessTools.Field(tdSettings.GetType(), "alertDeteriorating");
        if (tdAlertOn == null)
        {
            context.Fail("TD Enhancement Pack's alertDeteriorating setting wasn't found");
            return Task.CompletedTask;
        }

        var wasTdAlertOn = tdAlertOn.GetValue(tdSettings);
        tdAlertOn.SetValue(tdSettings, true);
        return WithSetting("DeteriorationAlertSkipTainted", skip ? "true" : "false", () =>
        {
            try
            {
                CheckDeteriorationReport(context, alertType, taintedParka, cleanParka, skip);
            }
            finally
            {
                tdAlertOn.SetValue(tdSettings, wasTdAlertOn);
            }

            return Task.CompletedTask;
        });
    }

    private static void CheckDeteriorationReport(
        ScenarioContext context,
        Type alertType,
        Apparel taintedParka,
        Apparel cleanParka,
        bool skip
    )
    {
        var report = ((Alert)Activator.CreateInstance(alertType)).GetReport();
        var culprits = report.culpritsThings ?? new List<Thing>();
        var listsTainted = culprits.Contains(taintedParka);
        context.Check($"with the setting {(skip ? "on" : "off")}, the alert {(listsTainted ? "lists" : "leaves out")} the tainted parka", listsTainted == !skip);
        if (cleanParka != null)
        {
            context.Check("the alert lists the clean parka", report.active && culprits.Contains(cleanParka));
        }
        else
        {
            // The blank test map has nothing else deteriorating, so with only the tainted parka the alert goes quiet
            context.Check($"with only tainted apparel the alert is inactive (culprits: {string.Join(", ", culprits.Select(thing => thing.LabelCap))})", !report.active);
        }
    }
}
