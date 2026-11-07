using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.LegendaryIsUnique;

[StaticConstructorOnStartup]
public class LegendaryIsUnique {

	private const string UniqueSuffix = "_Unique";

	private static readonly Dictionary<string, ThingDef> _uniqueThingsByBaseDefName = new();

	// Quality pre-rolled for the craft in progress, handed back to the game's own roll so they match
	private static (Pawn Pawn, QualityCategory Quality)? _forcedQuality;

	static LegendaryIsUnique()
	{
		var harmony = new Harmony(typeof(LegendaryIsUnique).Namespace);

		harmony.Patch(
			AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.PostProcessProduct)),
			prefix: new HarmonyMethod(Prefix_GenRecipe_PostProcessProduct),
			finalizer: new HarmonyMethod(Finalizer_GenRecipe_PostProcessProduct)
		);

		harmony.Patch(
			AccessTools.Method(typeof(QualityUtility), nameof(QualityUtility.GenerateQualityCreatedByPawn), [typeof(Pawn), typeof(SkillDef), typeof(bool)]),
			prefix: new HarmonyMethod(Prefix_QualityUtility_GenerateQualityCreatedByPawn)
		);

		if (ModsConfig.IsActive("vanillaexpanded.skills"))
		{
			harmony.Patch(
				AccessTools.Method("VSE.Stats.QualityUtility:GenerateQuality"),
				prefix: new HarmonyMethod(Prefix_VES_QualityUtility_GenerateQuality)
			);
		}

		foreach (var uniqueThingDef in DefDatabase<ThingDef>.AllDefs)
		{
			if (!uniqueThingDef.defName.EndsWith(UniqueSuffix, StringComparison.Ordinal) || !uniqueThingDef.HasComp<CompUniqueWeapon>())
			{
				continue;
			}

			var baseDefName = uniqueThingDef.defName[..^UniqueSuffix.Length];
			if (!_uniqueThingsByBaseDefName.TryAdd(baseDefName, uniqueThingDef))
			{
				Log.Warning($"[LegendaryIsUnique] Multiple unique variants for [{baseDefName}]: using [{_uniqueThingsByBaseDefName[baseDefName]}], ignoring [{uniqueThingDef}]");
			}
		}
	}

	// Swaps in the unique thing before the original runs, so the game applies quality, art, style,
	// minification and the craft notification to the unique thing itself
	public static void Prefix_GenRecipe_PostProcessProduct(
		ref Thing product,
		RecipeDef recipeDef,
		Pawn worker
	)
	{
		if (worker == null || !_uniqueThingsByBaseDefName.TryGetValue(product.def.defName, out var uniqueThingDef))
		{
			return;
		}

		var qualityToCreate = QualityUtility.GenerateQualityCreatedByPawn(worker, recipeDef.workSkill);
		_forcedQuality = (worker, qualityToCreate);

		if (qualityToCreate >= LegendaryIsUniqueMod.Settings.MinimumQuality)
		{
			product = ThingMaker.MakeThing(uniqueThingDef, product.Stuff);
		}
	}

	// Runs even if the original throws, so an unused roll never leaks into a later quality roll
	public static void Finalizer_GenRecipe_PostProcessProduct()
	{
		_forcedQuality = null;
	}

	public static bool Prefix_QualityUtility_GenerateQualityCreatedByPawn(ref QualityCategory __result, Pawn pawn)
	{
		return !TryConsumeForcedQuality(pawn, ref __result);
	}

	public static bool Prefix_VES_QualityUtility_GenerateQuality(ref QualityCategory __result, Pawn worker)
	{
		return !TryConsumeForcedQuality(worker, ref __result);
	}

	private static bool TryConsumeForcedQuality(Pawn pawn, ref QualityCategory quality)
	{
		if (_forcedQuality is not { } forced || forced.Pawn != pawn)
		{
			return false;
		}

		quality = forced.Quality;
		_forcedQuality = null;
		return true;
	}
}
