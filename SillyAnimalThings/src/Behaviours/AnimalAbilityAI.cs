using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.SillyAnimalThings;

// How the AI treats an ability. Abilities without it use the defaults.
public class AbilityAIExtension : DefModExtension
{
	// When several abilities could be used, the highest goes first
	public float priority;

	// Whether a predator uses it on its prey
	public bool useWhileHunting = true;

	// Whether a player's animal uses it by itself once trained. Off for abilities that would hurt the colony.
	public bool playerAutoCast = true;
}

// Picks which ability an animal uses against a foe. Every way an animal can get into a fight comes through here: manhunting,
// raids, defending its master, fighting back when attacked, and hunting.
public static class AnimalAbilityAI
{
	// How soon an animal chasing its target looks again for an ability that's ready, or about to be
	private const int ChaseRecheckTicks = 90;

	private static readonly List<Ability> TmpCandidates = new();

	public static bool IsOurs(
		AbilityDef def
	)
	{
		return def.defName.StartsWith(SillyAnimalThings.DefPrefix, StringComparison.Ordinal);
	}

	public static bool HasOurAbilities(
		Pawn pawn
	)
	{
		var abilities = pawn.abilities?.abilities;
		if (abilities == null || !pawn.RaceProps.Animal)
		{
			return false;
		}

		foreach (var ability in abilities)
		{
			if (IsOurs(ability.def))
			{
				return true;
			}
		}

		return false;
	}

	// Player animals use an ability by themselves only once trained in it, and never the ones that would hurt the colony.
	// Anyone else's animals always may; whether they need the trainable is up to the ability's RequiresTrainable comp.
	public static bool MayAutoCast(
		Pawn pawn,
		AbilityDef def
	)
	{
		if (pawn.Faction != Faction.OfPlayer)
		{
			return true;
		}

		if (def.GetModExtension<AbilityAIExtension>() is { playerAutoCast: false })
		{
			return false;
		}

		var trainable = def.comps?.Find(comp => comp is CompProperties_AbilityRequiresTrainable) as CompProperties_AbilityRequiresTrainable;
		return trainable == null || pawn.training?.HasLearned(trainable.trainableDef) == true;
	}

	public static Job TryGetAbilityJob(
		Pawn pawn,
		Thing foe,
		bool hunting = false
	)
	{
		if (foe == null || !pawn.Spawned || pawn.Downed || pawn.abilities == null)
		{
			return null;
		}

		// Remember the foe first, so the abilities' own checks count it as an enemy even when the animal isn't hostile to it
		HostilityUtility.RememberFoe(pawn, foe);
		return SelfBuffAutoCast.GetCombatSelfBuffJob(pawn, foe, hunting) ?? GetOffensiveJob(pawn, foe, hunting);
	}

	// Vanilla's JobGiver_AIFightEnemy.GetAbilityJob, but choosing by priority rather than list order
	private static Job GetOffensiveJob(
		Pawn pawn,
		Thing foe,
		bool hunting
	)
	{
		TmpCandidates.Clear();
		foreach (var ability in pawn.abilities.AICastableAbilities(foe, offensive: true))
		{
			var extension = ability.def.GetModExtension<AbilityAIExtension>();
			if (hunting && extension is { useWhileHunting: false })
			{
				continue;
			}

			TmpCandidates.Add(ability);
		}

		if (TmpCandidates.Count == 0)
		{
			return null;
		}

		if (!pawn.Position.WalkableBy(pawn.Map, pawn) || !pawn.Map.pawnDestinationReservationManager.CanReserve(pawn.Position, pawn, pawn.Drafted))
		{
			TmpCandidates.Clear();
			return null;
		}

		var candidates = TmpCandidates.OrderByDescending(ability => ability.def.GetModExtension<AbilityAIExtension>()?.priority ?? 0f).ToList();
		TmpCandidates.Clear();

		foreach (var ability in candidates)
		{
			if (ability.verb.CanHitTarget(foe))
			{
				return ability.GetJob(foe, foe);
			}
		}

		foreach (var ability in candidates)
		{
			var aoeTarget = ability.AIGetAOETarget();
			if (aoeTarget.IsValid)
			{
				return ability.GetJob(aoeTarget, aoeTarget);
			}
		}

		foreach (var ability in candidates)
		{
			if (ability.verb.targetParams.canTargetSelf)
			{
				return ability.GetJob(pawn, pawn);
			}
		}

		return null;
	}

	// A melee chase lasts several hundred ticks, and abilities are only considered when a job is picked, so an animal that
	// starts out of range would rarely use one. This ends the chase early when a ranged or combat ability is ready, or will be.
	public static void ShortenChase(
		Pawn pawn,
		Job job
	)
	{
		if (job == null || job.def != JobDefOf.AttackMelee || !HasOurAbilities(pawn))
		{
			return;
		}

		var soonest = int.MaxValue;
		foreach (var ability in pawn.abilities.abilities)
		{
			if (!IsOurs(ability.def) || !MayAutoCast(pawn, ability.def) || !UsedDuringChase(ability.def))
			{
				continue;
			}

			soonest = Math.Min(soonest, ability.CooldownTicksRemaining);
		}

		if (soonest == int.MaxValue)
		{
			return;
		}

		var expiry = Math.Max(soonest, 0) + ChaseRecheckTicks;
		if (job.expiryInterval <= 0 || expiry < job.expiryInterval)
		{
			job.expiryInterval = expiry;
		}
	}

	// Touch abilities are picked between melee hits anyway, since each melee job ends after a hit
	private static bool UsedDuringChase(
		AbilityDef def
	)
	{
		if (def.GetModExtension<AutoCastExtension>() is { CastsInCombat: true })
		{
			return true;
		}

		return def.aiCanUse && def.verbProperties.range > 1.5f;
	}
}
