using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.SillyAnimalThings;

// Self-targeted abilities that vanilla AI can't use properly: the combat AI would aim them at the enemy instead
public static class SelfBuffAutoCast
{
	private const float RegenerateHealthThreshold = 0.6f;

	private const float HiddenDragonMinDistance = 6.9f;

	private static bool CanAutoCast(
		Pawn pawn,
		Ability ability
	)
	{
		if (ability == null || !ability.CanCast)
		{
			return false;
		}

		// Player animals only use it once trained, the same as the other trainable abilities
		var trainable = ability.def.comps?.Find(comp => comp is CompProperties_AbilityRequiresTrainable) as CompProperties_AbilityRequiresTrainable;
		return pawn.Faction != Faction.OfPlayer || trainable == null || pawn.training?.HasLearned(trainable.trainableDef) == true;
	}

	public static void Postfix_Pawn_PostApplyDamage(
		Pawn __instance
	)
	{
		if (__instance.Dead || !__instance.Spawned || __instance.abilities == null)
		{
			return;
		}

		var regenerate = __instance.abilities.GetAbility(SillyAnimalThingsDefOf.Ordo_Regenerate);
		if (__instance.health.summaryHealth.SummaryHealthPercent < RegenerateHealthThreshold && CanAutoCast(__instance, regenerate))
		{
			regenerate.Activate(__instance, __instance);
		}
	}

	public static bool Prefix_JobGiver_AIFightEnemy_GetAbilityJob(
		Pawn pawn,
		Thing enemyTarget,
		ref Job __result
	)
	{
		var hiddenDragon = pawn.abilities?.GetAbility(SillyAnimalThingsDefOf.Ordo_HiddenDragon);
		if (!CanAutoCast(pawn, hiddenDragon)
		    || pawn.IsPsychologicallyInvisible()
		    || enemyTarget.Position.InHorDistOf(pawn.Position, HiddenDragonMinDistance))
		{
			return true;
		}

		__result = hiddenDragon.GetJob(pawn, pawn);
		return false;
	}
}
