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

	// Only cast when an ally is within the ability's effect radius, for buffs that are wasted on the caster alone
	public bool needsAllyInRadius;

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
		return ability.CanCast && AnimalAbilityAI.MayAutoCast(pawn, ability.def);
	}

	public static void Postfix_Pawn_PostApplyDamage(
		Pawn __instance
	)
	{
		if (__instance.Dead || __instance.Downed || !__instance.Spawned || __instance.abilities == null)
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

	public static Job GetCombatSelfBuffJob(
		Pawn pawn,
		Thing enemy,
		bool hunting
	)
	{
		foreach (var ability in pawn.abilities.abilities)
		{
			var autoCast = ability.def.GetModExtension<AutoCastExtension>();
			if (autoCast is not { CastsInCombat: true }
			    || (hunting && ability.def.GetModExtension<AbilityAIExtension>() is { useWhileHunting: false })
			    || !autoCast.EnemyDistanceAllowsCast(pawn, enemy)
			    || !CanAutoCast(pawn, ability)
			    || (autoCast.needsAllyInRadius && !AllyInRadius(pawn, ability.def.EffectRadius)))
			{
				continue;
			}

			return ability.GetJob(pawn, pawn);
		}

		return null;
	}

	private static bool AllyInRadius(
		Pawn pawn,
		float radius
	)
	{
		foreach (var other in pawn.Map.mapPawns.AllPawnsSpawned)
		{
			if (!other.Downed && other.Position.InHorDistOf(pawn.Position, radius) && other.IsAllyOf(pawn))
			{
				return true;
			}
		}

		return false;
	}
}
