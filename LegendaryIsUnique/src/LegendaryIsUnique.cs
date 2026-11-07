using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using Verse;

namespace Ordo.LegendaryIsUnique;

[StaticConstructorOnStartup]
public class LegendaryIsUnique {

	private static readonly Dictionary<string, ThingDef> _uniqueThingsByBaseDefName;
	private static readonly Dictionary<Pawn, QualityCategory> _nextCraft = new();

	[MethodImpl(MethodImplOptions.NoInlining)]
	static LegendaryIsUnique()
	{
		var harmony = new Harmony(typeof(LegendaryIsUnique).Namespace);

		harmony.Patch(
			AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.PostProcessProduct)),
			prefix: new HarmonyMethod(Prefix_ThingWithComps_PostProcessProduct),
			postfix: new HarmonyMethod(Postfix_ThingWithComps_PostProcessProduct)
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
		
		_uniqueThingsByBaseDefName = DefDatabase<ThingDef>.AllDefs
			.Where(x => x.HasComp<CompUniqueWeapon>())
			.ToDictionary(x => x.defName.Replace("_Unique", ""), x => x);
	}

	public class PatchState(QualityCategory qualityToCreate, ThingDef uniqueThingDef)
	{
		public QualityCategory QualityToCreate { get; } = qualityToCreate;
		public ThingDef UniqueThingDef { get; } = uniqueThingDef;
	}

	public static void Prefix_ThingWithComps_PostProcessProduct(
		Thing product,
		RecipeDef recipeDef,
		Pawn worker,
		[CanBeNull] out PatchState __state,
		Precept_ThingStyle precept = null,
		ThingStyleDef style = null,
		int? overrideGraphicIndex = null
	)
	{
		__state = null;

		if (!_uniqueThingsByBaseDefName.TryGetValue(product.def.defName, out var uniqueThingDef))
		{
			return;
		}

		var qualityToCreate = QualityUtility.GenerateQualityCreatedByPawn(worker, recipeDef.workSkill);
		_nextCraft[worker] = qualityToCreate;

		if (qualityToCreate is QualityCategory.Legendary or QualityCategory.Masterwork)
		{
			__state = new PatchState(qualityToCreate, uniqueThingDef);
		}
	}

	public static void Postfix_ThingWithComps_PostProcessProduct(
		Thing product,
		RecipeDef recipeDef,
		Pawn worker,
		[CanBeNull] PatchState __state,
		ref Thing __result,
		Precept_ThingStyle precept = null,
		ThingStyleDef style = null,
		int? overrideGraphicIndex = null
	)
	{
		if (__state == null)
		{
			return;
		}

		var unique = ThingMaker.MakeThing(__state.UniqueThingDef, product.Stuff);

		var compQuality = unique.TryGetComp<CompQuality>();
		compQuality?.SetQuality(__state.QualityToCreate, ArtGenerationContext.Colony);

		var compArt = unique.TryGetComp<CompArt>();
		compArt?.JustCreatedBy(worker);

		if (worker.Ideo != null)
			unique.StyleDef = worker.Ideo.GetStyleFor(unique.def);

		if (precept != null)
			unique.StyleSourcePrecept = precept;
		else if (style != null)
			unique.StyleDef = style;
		else if (!__state.UniqueThingDef.randomStyle.NullOrEmpty() && Rand.Chance(__state.UniqueThingDef.randomStyleChance))
			unique.SetStyleDef(__state.UniqueThingDef.randomStyle.RandomElementByWeight(x => x.Chance).StyleDef);
		unique.overrideGraphicIndex = overrideGraphicIndex;

		// Minify last so the comps and style above land on the inner thing
		__result = unique.def.Minifiable ? unique.MakeMinified() : unique;
	}

	public static bool Prefix_QualityUtility_GenerateQualityCreatedByPawn(
		ref QualityCategory __result,
		Pawn pawn,
		SkillDef relevantSkill,
		bool consumeInspiration = true
	) {
		if (_nextCraft.TryGetValue(pawn, out var qualityToCreate))
		{
			__result = qualityToCreate;
			_nextCraft.Remove(pawn);
			return false;
		}

		return true;
	}

	public static bool Prefix_VES_QualityUtility_GenerateQuality(
		ref QualityCategory __result,
		Pawn worker,
		SkillDef workSkill,
		bool consumeInspiration = true,
		Thing thing = null
	) {
		if (_nextCraft.TryGetValue(worker, out var qualityToCreate))
		{
			__result = qualityToCreate;
			_nextCraft.Remove(worker);
			return false;
		}

		return true;
	}

	// private static void Prefix_ThingWithComps_PostProcessProduct(
	// 	Thing product,
	// 	RecipeDef recipeDef,
	// 	Pawn worker,
	// 	Precept_ThingStyle precept = null,
	// 	ThingStyleDef style = null,
	// 	int? overrideGraphicIndex = null
	// ) {
	// 	if (product is not ThingWithComps thingWithComps)
	// 	{
	// 		return;
	// 	}
	// 	
	// 	if (thingWithComps.def.HasComp<CompUniqueWeapon>())
	// 	{
	// 		Log.Message($"Skipping already unique thing: [{product.def}]");
	// 		return;
	// 	}
	// 	
	// 	if (thingWithComps.TryGetComp<CompQuality>(out var quality) && quality.Quality == QualityCategory.Legendary)
	// 	{
	// 		Log.Message($"Making legendary thing unique: [{product}]");
	// 		thingWithComps.AllComps.Add();
	// 	}
	// }
    
 // FIXME: Add CompProperties_UniqueWeapon if the weapon is legendary
 // <li Class="CompProperties_UniqueWeapon">
	// 			<weaponCategories>
	// 				<li>Ranged</li>
	// 				<li>BulletFiring</li>
	// 				<li>Gun</li>
	// 				<li>Pistol</li>
	// 				<li>Sighted</li>
	// 				<li MayRequire="VanillaExpanded.VWE">VWE_Akimbo</li>
	// 			</weaponCategories>
	// 			<namerLabels>
	// 				<li>lightweight pistol</li>
	// 			</namerLabels>
	// 		</li>
}