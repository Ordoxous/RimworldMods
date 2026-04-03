using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
			typeof(GenRecipe).GetMethod("PostProcessProduct", BindingFlags.NonPublic | BindingFlags.Static),
			prefix: new HarmonyMethod(Prefix_ThingWithComps_PostProcessProduct),
			postfix: new HarmonyMethod(Postfix_ThingWithComps_PostProcessProduct)
		);

		harmony.Patch(
			typeof(QualityUtility).GetMethod(nameof(QualityUtility.GenerateQualityCreatedByPawn), [typeof(Pawn), typeof(SkillDef), typeof(bool)]),
			prefix: new HarmonyMethod(Prefix_QualityUtility_GenerateQualityCreatedByPawn)
		);

		if (ModLister.HasActiveModWithName("Vanilla Skills Expanded"))
		{
			var withType = Traverse.CreateWithType("VSE.Stats.QualityUtility").GetValue<Type>();
			var traverse = withType.GetMethod("GenerateQuality");
			harmony.Patch(
				traverse,
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

		__result = ThingMaker.MakeThing(__state.UniqueThingDef, __result.Stuff);
		
		if (__result.def.Minifiable)
			__result = __result.MakeMinified();
		
		var compQuality = product.TryGetComp<CompQuality>();
		compQuality?.SetQuality(__state.QualityToCreate, ArtGenerationContext.Colony);

		var compArt = product.TryGetComp<CompArt>();
		compArt?.JustCreatedBy(worker);
		
		if (worker.Ideo != null)
			__result.StyleDef = worker.Ideo.GetStyleFor(__result.def);

		if (precept != null)
			__result.StyleSourcePrecept = precept;
		else if (style != null)
			__result.StyleDef = style;
		else if (!__state.UniqueThingDef.randomStyle.NullOrEmpty() && Rand.Chance(__state.UniqueThingDef.randomStyleChance))
			__result.SetStyleDef(__state.UniqueThingDef.randomStyle.RandomElementByWeight(x => x.Chance).StyleDef);
		__result.overrideGraphicIndex = overrideGraphicIndex;
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