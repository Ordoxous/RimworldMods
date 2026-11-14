using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using Ordo.LegendaryIsUnique;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Crafts through GenRecipe.MakeRecipeProducts, the game's own path from a finished bill to its products, many times over with
// a fixed random seed, so the outcomes are the same on every run. Weapons are picked from the defs at runtime. The settings are
// changed for each scenario and restored afterwards; a minimum quality of Awful, below what the settings window offers, makes
// every roll qualify.
public static class LegendaryIsUniqueScenarios
{
	private const string HarmonyId = "Ordo.LegendaryIsUnique";

	private const string UniqueSuffix = "_Unique";

	private const int Seed = 1312;

	// Suites: "setup" for the variants and patches, "craft" for what a craft makes, "quality" for the one roll behind both the
	// swap and the item's quality, and "settings" for the threshold and chance
	public static readonly List<Scenario> All = new()
	{
		new("setup_variants_and_patches", "setup", VariantsAndPatches),
		new("craft_threshold_matches_quality", "craft quality settings", ThresholdMatchesQuality),
		new("craft_default_needs_legendary", "craft settings", DefaultNeedsLegendary),
		new("craft_chance", "craft settings", Chance),
		new("craft_no_variant_untouched", "craft", NoVariantUntouched),
		new("quality_roll_cleared_after_throw", "quality", RollClearedAfterThrow),
		new("quality_vse_pre_roll", "quality vse", VsePreRoll, requiresMod: "vanillaexpanded.skills")
	};

	private static readonly Type ModType = typeof(LegendaryIsUnique.LegendaryIsUnique);

	private static Dictionary<string, ThingDef> VariantsByBase =>
		(Dictionary<string, ThingDef>)ModType.GetField("_uniqueThingsByBaseDefName", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

	private static readonly FieldInfo ForcedQuality = ModType.GetField("_forcedQuality", BindingFlags.NonPublic | BindingFlags.Static)!;

	private static bool VseActive => ModsConfig.IsActive("vanillaexpanded.skills");

	private static ThingDef UniqueVariant(
		ThingDef weapon
	)
	{
		var unique = DefDatabase<ThingDef>.GetNamedSilentFail(weapon.defName + UniqueSuffix);
		return unique != null && unique.HasComp<CompUniqueWeapon>() ? unique : null;
	}

	// A recipe that makes one weapon with a quality, not from stuff, crafted with a skill
	private static RecipeDef WeaponRecipe(
		bool withVariant
	)
	{
		return DefDatabase<RecipeDef>.AllDefsListForReading
			.Where(recipe => recipe.workSkill != null && recipe.products?.Count == 1)
			.OrderBy(recipe => recipe.defName)
			.FirstOrDefault(recipe =>
			{
				var product = recipe.products[0].thingDef;
				return product.IsWeapon && !product.MadeFromStuff && product.HasComp<CompQuality>() && (UniqueVariant(product) != null) == withVariant;
			});
	}

	private static Pawn Crafter(
		ScenarioContext context,
		RecipeDef recipe,
		int skill
	)
	{
		// A generated colonist can be incapable of the skill, which then counts as level 0 whatever it's set to
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var pawn = context.Colonist(attempt, 0);
			var record = pawn.skills.GetSkill(recipe.workSkill);
			if (record.TotallyDisabled)
			{
				pawn.Destroy();
				continue;
			}

			record.Level = skill;
			return pawn;
		}

		throw new InvalidOperationException($"No colonist capable of {recipe.workSkill.defName} in 20 tries");
	}

	// What finishing the bill makes, as the game's own toil does it
	private static Thing Craft(
		RecipeDef recipe,
		Pawn worker
	)
	{
		return GenRecipe.MakeRecipeProducts(recipe, worker, new List<Thing>(), null, null, null, null, null).Single();
	}

	private static QualityCategory? Quality(
		Thing thing
	)
	{
		return thing.TryGetComp<CompQuality>()?.Quality;
	}

	// Runs with the given settings and a fixed seed, restoring both afterwards
	private static void With(
		QualityCategory minimumQuality,
		float uniqueChance,
		Action run
	)
	{
		var settings = LegendaryIsUniqueMod.Settings;
		var (wasQuality, wasChance) = (settings.MinimumQuality, settings.UniqueChance);
		settings.MinimumQuality = minimumQuality;
		settings.UniqueChance = uniqueChance;
		Rand.PushState(Seed);
		try
		{
			run();
		}
		finally
		{
			Rand.PopState();
			settings.MinimumQuality = wasQuality;
			settings.UniqueChance = wasChance;
		}
	}

	private static Task VariantsAndPatches(
		ScenarioContext context
	)
	{
		var expected = DefDatabase<ThingDef>.AllDefsListForReading
			.Where(def => def.defName.EndsWith(UniqueSuffix, StringComparison.Ordinal) && def.HasComp<CompUniqueWeapon>())
			.ToList();
		var found = VariantsByBase;
		context.Check($"Odyssey's unique weapons were found: {expected.Count}", expected.Count > 0);
		foreach (var unique in expected)
		{
			var baseName = unique.defName[..^UniqueSuffix.Length];
			context.Check($"{baseName} maps to {unique.defName}: {(found.TryGetValue(baseName, out var mapped) ? mapped.defName : "nothing")}", found.TryGetValue(baseName, out var target) && target == unique);
		}

		context.Check($"nothing but unique weapons is mapped: {found.Count} mapped, {expected.Count} expected", found.Count == expected.Count);

		CheckPatched(context, AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.PostProcessProduct)), prefix: true, finalizer: true);
		CheckPatched(context, AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) }), prefix: true, finalizer: false);
		if (VseActive)
		{
			var vse = AccessTools.Method("VSE.Stats.QualityUtility:GenerateQuality");
			context.Check("VSE is active and has VSE.Stats.QualityUtility.GenerateQuality", vse != null);
			if (vse != null)
			{
				CheckPatched(context, vse, prefix: true, finalizer: false);
			}
		}

		return Task.CompletedTask;
	}

	private static void CheckPatched(
		ScenarioContext context,
		MethodBase method,
		bool prefix,
		bool finalizer
	)
	{
		var patches = Harmony.GetPatchInfo(method);
		var name = $"{method.DeclaringType?.Name}.{method.Name}";
		context.Check($"{name} has the mod's prefix", !prefix || patches?.Prefixes.Any(patch => patch.owner == HarmonyId) == true);
		context.Check($"{name} has the mod's finalizer", !finalizer || patches?.Finalizers.Any(patch => patch.owner == HarmonyId) == true);
	}

	// The core of the mod: the roll that decides the swap is the roll the item gets. With Excellent as the threshold and a
	// crafter who sometimes makes it, every craft at or above Excellent is unique and every craft below it isn't.
	private static Task ThresholdMatchesQuality(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: true);
		if (recipe == null)
		{
			context.Fail("no weapon recipe with a unique variant");
			return Task.CompletedTask;
		}

		var weapon = recipe.products[0].thingDef;
		var unique = UniqueVariant(weapon);
		var crafter = Crafter(context, recipe, 14);
		var mismatches = new List<string>();
		int uniques = 0, normals = 0;
		With(QualityCategory.Excellent, 1f, () =>
		{
			for (var i = 0; i < 200; i++)
			{
				var product = Craft(recipe, crafter);
				var quality = Quality(product);
				var shouldBeUnique = quality >= QualityCategory.Excellent;
				if (product.def == unique)
				{
					uniques++;
				}
				else if (product.def == weapon)
				{
					normals++;
				}

				if ((product.def == unique) != shouldBeUnique || quality == null)
				{
					mismatches.Add($"{product.def.defName} at {quality}");
				}
			}
		});

		context.Check($"{weapon.defName}: unique exactly when the quality is Excellent or better; mismatches: [{string.Join(", ", mismatches.Take(5))}]", mismatches.Count == 0);
		context.Check($"both outcomes happened: {uniques} unique, {normals} normal", uniques >= 5 && normals >= 5);
		return Task.CompletedTask;
	}

	// The default setting needs Legendary: a crafter who can make Masterwork but can't reach Legendary without inspiration
	// never makes the unique weapon
	private static Task DefaultNeedsLegendary(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: true);
		if (recipe == null)
		{
			context.Fail("no weapon recipe with a unique variant");
			return Task.CompletedTask;
		}

		var weapon = recipe.products[0].thingDef;
		var crafter = Crafter(context, recipe, 20);
		var qualities = new List<QualityCategory?>();
		var made = new List<ThingDef>();
		With(QualityCategory.Legendary, 1f, () =>
		{
			for (var i = 0; i < 50; i++)
			{
				var product = Craft(recipe, crafter);
				qualities.Add(Quality(product));
				made.Add(product.def);
			}
		});

		var legendary = qualities.Count(quality => quality == QualityCategory.Legendary);
		context.Check($"with no Legendary rolls ({legendary} of 50), every craft is the normal {weapon.defName}", made.Zip(qualities).All(pair => pair.First == weapon || pair.Second == QualityCategory.Legendary));
		context.Check($"the crafter reached Masterwork: best was {qualities.Max()}", qualities.Max() >= QualityCategory.Masterwork);
		return Task.CompletedTask;
	}

	// With every roll qualifying and a 25% chance, about a quarter of crafts are unique
	private static Task Chance(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: true);
		if (recipe == null)
		{
			context.Fail("no weapon recipe with a unique variant");
			return Task.CompletedTask;
		}

		var unique = UniqueVariant(recipe.products[0].thingDef);
		var crafter = Crafter(context, recipe, 0);
		const int crafts = 400;
		var uniques = 0;
		With(QualityCategory.Awful, 0.25f, () =>
		{
			for (var i = 0; i < crafts; i++)
			{
				if (Craft(recipe, crafter).def == unique)
				{
					uniques++;
				}
			}
		});

		// Three standard deviations either side of the expected 100. The seed is fixed, so this gives the same count every run.
		var share = uniques / (float)crafts;
		context.Check($"about 25% of qualifying crafts are unique: {uniques} of {crafts} ({share:P0})", share is > 0.18f and < 0.32f);
		return Task.CompletedTask;
	}

	private static Task NoVariantUntouched(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: false);
		if (recipe == null)
		{
			context.Fail("no weapon recipe without a unique variant");
			return Task.CompletedTask;
		}

		var weapon = recipe.products[0].thingDef;
		var crafter = Crafter(context, recipe, 10);
		var made = new List<Thing>();
		With(QualityCategory.Awful, 1f, () =>
		{
			for (var i = 0; i < 20; i++)
			{
				made.Add(Craft(recipe, crafter));
			}
		});

		context.Check($"{weapon.defName}, with no unique variant, is always itself: [{string.Join(", ", made.Select(thing => thing.def.defName).Distinct())}]", made.All(thing => thing.def == weapon));
		context.Check("it still gets a quality", made.All(thing => Quality(thing) != null));
		return Task.CompletedTask;
	}

	// The pre-rolled quality waits for the game's own roll. If something throws before that roll takes it, the finalizer must
	// still clear it, or the crafter's next quality roll, for anything, would get it.
	private static Task RollClearedAfterThrow(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: true);
		if (recipe == null)
		{
			context.Fail("no weapon recipe with a unique variant");
			return Task.CompletedTask;
		}

		var crafter = Crafter(context, recipe, 10);
		With(QualityCategory.Awful, 1f, () => Craft(recipe, crafter));
		context.Check("no roll is left waiting after a normal craft", ForcedQuality.GetValue(null) == null);

		// Throws from the game's roll while the pre-roll is waiting, ahead of the mod's prefix, which would take it
		var harmony = new Harmony("Ordo.BridgeTools.LegendaryIsUniqueScenarios");
		var thrower = new HarmonyMethod(typeof(LegendaryIsUniqueScenarios), nameof(ThrowWhileWaiting)) { priority = Priority.First };
		var rolls = new List<MethodBase> { AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), new[] { typeof(Pawn), typeof(SkillDef), typeof(bool) }) };
		if (VseActive && AccessTools.Method("VSE.Stats.QualityUtility:GenerateQuality") is { } vse)
		{
			rolls.Add(vse);
		}

		Exception thrown = null;
		try
		{
			foreach (var roll in rolls)
			{
				harmony.Patch(roll, prefix: thrower);
			}

			With(QualityCategory.Awful, 1f, () =>
			{
				try
				{
					GenRecipe.PostProcessProduct(ThingMaker.MakeThing(recipe.products[0].thingDef), recipe, crafter, null, null, null);
				}
				catch (Exception exception)
				{
					thrown = exception;
				}
			});
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
		}

		context.Check($"the planted fault threw: {thrown?.GetType().Name}", thrown != null);
		context.Check("no roll is left waiting after the throw", ForcedQuality.GetValue(null) == null);
		ForcedQuality.SetValue(null, null);
		return Task.CompletedTask;
	}

	// With VSE, the roll that decides the swap must be VSE's own, so its quality bonuses count, and the item must get that roll.
	// A VSE roll made while no roll is waiting is the pre-roll; the game's later roll finds one waiting and takes it.
	private static Task VsePreRoll(
		ScenarioContext context
	)
	{
		var recipe = WeaponRecipe(withVariant: true);
		var vse = AccessTools.Method("VSE.Stats.QualityUtility:GenerateQuality");
		if (recipe == null || vse == null)
		{
			context.Fail($"no weapon recipe with a unique variant ({recipe?.defName}) or no VSE roll ({vse})");
			return Task.CompletedTask;
		}

		var crafter = Crafter(context, recipe, 14);
		var harmony = new Harmony("Ordo.BridgeTools.LegendaryIsUniqueScenarios");
		var mismatches = new List<string>();
		var preRolls = 0;
		try
		{
			harmony.Patch(
				vse,
				prefix: new HarmonyMethod(typeof(LegendaryIsUniqueScenarios), nameof(NoteWaiting)) { priority = Priority.First },
				postfix: new HarmonyMethod(typeof(LegendaryIsUniqueScenarios), nameof(RecordVseRoll))
			);
			With(QualityCategory.Excellent, 1f, () =>
			{
				for (var i = 0; i < 50; i++)
				{
					VseRolls.Clear();
					var product = Craft(recipe, crafter);
					var preRoll = VseRolls.FirstOrDefault(roll => !roll.Waiting);
					if (preRoll == default)
					{
						mismatches.Add("no VSE pre-roll");
						continue;
					}

					preRolls++;
					if (Quality(product) != preRoll.Quality)
					{
						mismatches.Add($"pre-rolled {preRoll.Quality}, item is {Quality(product)}");
					}
				}
			});
		}
		finally
		{
			harmony.UnpatchAll(harmony.Id);
			VseRolls.Clear();
		}

		context.Check($"every craft's deciding roll was VSE's, and the item got it: {preRolls} of 50; problems: [{string.Join(", ", mismatches.Distinct().Take(5))}]", mismatches.Count == 0 && preRolls == 50);
		return Task.CompletedTask;
	}

	private static readonly List<(bool Waiting, QualityCategory Quality)> VseRolls = new();

	// Ahead of the mod's prefix, which takes a waiting roll, so this sees whether one was waiting when the call began
	private static void NoteWaiting(
		out bool __state
	)
	{
		__state = ForcedQuality.GetValue(null) != null;
	}

	// Harmony runs postfixes even when a prefix skipped the original, so the game's later, consumed roll is recorded too
	private static void RecordVseRoll(
		QualityCategory __result,
		bool __state
	)
	{
		VseRolls.Add((__state, __result));
	}

	private static void ThrowWhileWaiting()
	{
		if (ForcedQuality.GetValue(null) != null)
		{
			throw new InvalidOperationException("Planted by the scenario: the game's quality roll failed");
		}
	}
}
