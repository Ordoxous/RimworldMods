using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.SillyAnimalThings;

// Hooks that let animals use their abilities wherever they fight, not only while manhunting
public static class AnimalCombatPatches
{
	// How often a hunting predator looks for an ability to use on its prey
	private const int HuntCheckInterval = 30;

	// Manhunters, raiders and animals defending their master all ask this for an ability first
	public static bool Prefix_JobGiver_AIFightEnemy_GetAbilityJob(
		Pawn pawn,
		Thing enemyTarget,
		ref Job __result
	)
	{
		if (!AnimalAbilityAI.HasOurAbilities(pawn))
		{
			return true;
		}

		__result = AnimalAbilityAI.TryGetAbilityJob(pawn, enemyTarget);
		return false;
	}

	// Vanilla stops a trained animal from using the ability by itself, and lets an untrained one. For a player's animal
	// this mod wants the reverse: it uses what it has been trained in.
	public static void Postfix_CompAbilityEffect_RequiresTrainable_AICanTargetNow(
		CompAbilityEffect_RequiresTrainable __instance,
		ref bool __result
	)
	{
		var pawn = __instance.parent.pawn;
		var def = __instance.parent.def;
		if (pawn == null || !AnimalAbilityAI.IsOurs(def))
		{
			return;
		}

		__result = pawn.Faction == Faction.OfPlayer
			? AnimalAbilityAI.MayAutoCast(pawn, def)
			: __instance.Props.aiCanCastWithoutTrainable || pawn.training?.HasLearned(__instance.Props.trainableDef) == true;
	}

	// Fighting back against whoever is hitting it in melee. Vanilla only ever makes a plain melee attack here.
	public static void Postfix_JobGiver_ReactToCloseMeleeThreat_TryGiveJob(
		Pawn pawn,
		ref Job __result
	)
	{
		if (__result?.def != JobDefOf.AttackMelee || !AnimalAbilityAI.HasOurAbilities(pawn))
		{
			return;
		}

		var abilityJob = AnimalAbilityAI.TryGetAbilityJob(pawn, pawn.mindState.meleeThreat);
		if (abilityJob != null)
		{
			__result = abilityJob;
		}
	}

	public static void Postfix_JobGiver_Manhunter_TryGiveJob(
		Pawn pawn,
		Job __result
	)
	{
		AnimalAbilityAI.ShortenChase(pawn, __result);
	}

	public static void Postfix_JobGiver_AIFightEnemy_MeleeAttackJob(
		Pawn pawn,
		Job __result
	)
	{
		AnimalAbilityAI.ShortenChase(pawn, __result);
	}

	// Runs every few ticks while a predator chases its prey (the hook is vanilla's check for warning the player). Vanilla's
	// hunt never uses abilities, so this pauses the hunt for one and resumes it afterwards.
	public static void Postfix_JobDriver_PredatorHunt_CheckWarnPlayerInterval(
		JobDriver_PredatorHunt __instance,
		int delta
	)
	{
		var pawn = __instance.pawn;
		if (pawn.abilities == null || !pawn.IsHashIntervalTick(HuntCheckInterval, delta) || !AnimalAbilityAI.HasOurAbilities(pawn))
		{
			return;
		}

		if (__instance.job.GetTarget(TargetIndex.A).Thing is not Pawn { Spawned: true, Downed: false } prey)
		{
			return;
		}

		var abilityJob = AnimalAbilityAI.TryGetAbilityJob(pawn, prey, hunting: true);
		if (abilityJob != null)
		{
			pawn.jobs.StartJob(abilityJob, JobCondition.InterruptForced, resumeCurJobAfterwards: true);
		}
	}
}
