using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.SillyAnimalThings;

// Conditions under which a self-targeted ability casts itself. Any unset condition is ignored.
public class AutoCastExtension : DefModExtension
{
	// Cast straight away when damage leaves the pawn below this fraction of its health
	public float castBelowHealth = -1f;

	// Cast at the start of a fight, depending on how far away the enemy is
	public float castWhenEnemyFartherThan = -1f;

	public float castWhenEnemyWithin = -1f;

	public bool CastsInCombat => castWhenEnemyFartherThan > 0f || castWhenEnemyWithin > 0f;

	public bool EnemyDistanceAllowsCast(
		Pawn pawn,
		Thing enemy
	)
	{
		return (castWhenEnemyFartherThan <= 0f || !enemy.Position.InHorDistOf(pawn.Position, castWhenEnemyFartherThan))
		       && (castWhenEnemyWithin <= 0f || enemy.Position.InHorDistOf(pawn.Position, castWhenEnemyWithin));
	}
}

// Self-targeted abilities that vanilla AI can't use properly: the combat AI would aim them at the enemy instead
public static class SelfBuffAutoCast
{
	private static bool CanAutoCast(
		Pawn pawn,
		Ability ability
	)
	{
		if (!ability.CanCast)
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

		foreach (var ability in __instance.abilities.abilities)
		{
			var autoCast = ability.def.GetModExtension<AutoCastExtension>();
			if (autoCast != null
			    && __instance.health.summaryHealth.SummaryHealthPercent < autoCast.castBelowHealth
			    && CanAutoCast(__instance, ability))
			{
				ability.Activate(__instance, __instance);
			}
		}
	}

	public static bool Prefix_JobGiver_AIFightEnemy_GetAbilityJob(
		Pawn pawn,
		Thing enemyTarget,
		ref Job __result
	)
	{
		if (pawn.abilities == null)
		{
			return true;
		}

		foreach (var ability in pawn.abilities.abilities)
		{
			var autoCast = ability.def.GetModExtension<AutoCastExtension>();
			if (autoCast is { CastsInCombat: true }
			    && autoCast.EnemyDistanceAllowsCast(pawn, enemyTarget)
			    && CanAutoCast(pawn, ability))
			{
				__result = ability.GetJob(pawn, pawn);
				return false;
			}
		}

		return true;
	}
}
