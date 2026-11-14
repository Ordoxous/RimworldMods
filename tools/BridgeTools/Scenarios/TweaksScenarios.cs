using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Scenarios;

// Most tweaks are XML patches, so most scenarios check the patched def has the change. A patch for another mod requires that
// mod, and shows as skipped on the tweaks profile, which has only Tweaks' hard dependencies; tweaks-mods loads them all. Other
// mods' types are reached by reflection, so BridgeTools needs no reference to them and loads without them. Settings are XML
// Extensions settings, changed through its SettingsManager and restored afterwards; the C# tweaks read them each time.
public static class TweaksScenarios
{
	private const string ModId = "Ordo.Tweaks";

	private const string HarmonyId = "Ordo.BridgeTools.TweaksScenarios";

	// Every def a tweak adds or patches, and the mod's own names, for finding the load errors a tweak caused
	private static readonly string[] Touched =
	{
		"Ordo.Tweaks", "Ordo_Tools", "Ordo_HaulMechForRepair", "HaulMechForRepair", "StandardRecharger", "TableMachining", "SubcoreEncoder",
		"LargeMechGestator", "MU_BasicUpgradesAssembler", "MU_AdvancedUpgradesAssembler", "MU_MechUpgrader", "Mech_Lifter",
		"VR_Construct_Lifter", "Mech_Constructoid", "VR_Construct_Constructoid", "FSFMerge", "FSFDeliver", "WorkType_Improving",
		"VSE_Critical", "AMT_Handwear_BlacksmithGloves", "VFE_PortableWoodFiredGenerator", "VFE_PortableChemfuelPoweredGenerator",
		"VFE_IndustrialWoodFiredGenerator", "VFE_IndustrialChemfuelPoweredGenerator", "MechaWeaponChanger", "RS_GeneCentrifuge",
		"RS_XenoGermDuplicator", "VFE_ComponentFabricationBench", "VFE_FabricationCabinet", "VTE_WallMountedHeater",
		"VTE_WallMountedCooler", "VTE_WallMountedVent", "WaterWellInlet", "DeepWaterWellInlet"
	};

	// Suites: "setup" for loading, "patches" for the XML patches, one per C# tweak ("armor", "haul_mechs", "tools", "hemogen"),
	// and "settings" for the scenarios that turn a tweak off
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
		new("tools_category", "tools", context => ToolsCategory(context, requireTools: false)),
		new("tools_category_power_tools", "tools", context => ToolsCategory(context, requireTools: true), requiresMod: "setosa.power.tools"),
		new("hemogen_below_cap_queues", "hemogen", context => Hemogen(context, packs: 0, limit: true, expectBill: true), requiresMod: "biotechpatch.1trickPwnyta"),
		new("hemogen_at_cap_waits", "hemogen", context => Hemogen(context, packs: 30, limit: true, expectBill: false), requiresMod: "biotechpatch.1trickPwnyta"),
		new("hemogen_off_ignores_cap", "hemogen settings", context => Hemogen(context, packs: 30, limit: false, expectBill: true), requiresMod: "biotechpatch.1trickPwnyta")
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
			["ArmorIndicators"] = "true", ["HaulDownedMechs"] = "true", ["ToolsCategory"] = "true",
			["LimitHemogenFarming"] = "true", ["HemogenPackCap"] = "30"
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
						var result = pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 0f, hitPart: torso));
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
			pawn.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 500f, 0f, hitPart: pawn.RaceProps.body.corePart));
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
				pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f, 0f, hitPart: pawn.RaceProps.body.corePart));
			}

			context.Check($"with the setting off, no numbers: [{string.Join(", ", Texts.Select(text => text.Text))}]", Texts.Count == 0);
			return Task.CompletedTask;
		}));
	}

	// A WVC shutdown zone, made by reflection since BridgeTools has no reference to WVC. Delete it afterwards: stage cleanup
	// doesn't remove zones.
	private static Zone ShutdownZone(
		ScenarioContext context,
		IEnumerable<IntVec3> cells
	)
	{
		var type = AccessTools.TypeByName("WVC_WorkModes.Zone_MechanoidShutdown");
		var zone = (Zone)Activator.CreateInstance(type, context.Map.zoneManager);
		context.Map.zoneManager.RegisterZone(zone);
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
}
