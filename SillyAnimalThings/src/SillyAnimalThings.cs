using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

[StaticConstructorOnStartup]
public static class SillyAnimalThings
{
	public const string DefPrefix = "Ordo_";

	static SillyAnimalThings()
	{
		AnimalTagUtility.ApplyAllTags();

		var harmony = new Harmony(typeof(SillyAnimalThings).Namespace);

		harmony.Patch(
			AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup)),
			postfix: new HarmonyMethod(Postfix_Pawn_SpawnSetup)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobGiver_GetFood), nameof(JobGiver_GetFood.TryGiveJob)),
			prefix: new HarmonyMethod(HungryGnawing.Prefix_JobGiver_GetFood_TryGiveJob)
		);

		harmony.Patch(
			AccessTools.Method(typeof(Pawn), nameof(Pawn.PostApplyDamage)),
			postfix: new HarmonyMethod(SelfBuffAutoCast.Postfix_Pawn_PostApplyDamage)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobGiver_AIFightEnemy), nameof(JobGiver_AIFightEnemy.GetAbilityJob)),
			prefix: new HarmonyMethod(SelfBuffAutoCast.Prefix_JobGiver_AIFightEnemy_GetAbilityJob)
		);
	}

	// Pawn kind abilities are only granted at generation, so animals that existed before the mod was added never get ours
	private static void Postfix_Pawn_SpawnSetup(
		Pawn __instance
	)
	{
		var kindAbilities = __instance.kindDef?.abilities;
		if (kindAbilities.NullOrEmpty() || !__instance.RaceProps.Animal)
		{
			return;
		}

		foreach (var abilityDef in kindAbilities)
		{
			if (!abilityDef.defName.StartsWith(DefPrefix))
			{
				continue;
			}

			__instance.abilities ??= new Pawn_AbilityTracker(__instance);
			if (__instance.abilities.GetAbility(abilityDef) == null)
			{
				__instance.abilities.GainAbility(abilityDef);
			}
		}
	}
}
