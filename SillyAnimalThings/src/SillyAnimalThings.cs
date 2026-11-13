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
			prefix: new HarmonyMethod(AnimalCombatPatches.Prefix_JobGiver_AIFightEnemy_GetAbilityJob)
		);

		harmony.Patch(
			AccessTools.Method(typeof(CompAbilityEffect_RequiresTrainable), nameof(CompAbilityEffect_RequiresTrainable.AICanTargetNow)),
			postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_CompAbilityEffect_RequiresTrainable_AICanTargetNow)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob"),
			postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_ReactToCloseMeleeThreat_TryGiveJob)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobGiver_Manhunter), "TryGiveJob"),
			postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_Manhunter_TryGiveJob)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobGiver_AIFightEnemy), "MeleeAttackJob"),
			postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_AIFightEnemy_MeleeAttackJob)
		);

		harmony.Patch(
			AccessTools.Method(typeof(JobDriver_PredatorHunt), "CheckWarnPlayerInterval"),
			postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobDriver_PredatorHunt_CheckWarnPlayerInterval)
		);

		// Dev mode's "Hot reload Defs" rebuilds every def from XML, which drops the abilities and trainables the tags add in code
		var hotReloadDefs = AccessTools.Method(typeof(PlayDataLoader), "HotReloadDefs");
		if (hotReloadDefs != null)
		{
			harmony.Patch(hotReloadDefs, postfix: new HarmonyMethod(Postfix_PlayDataLoader_HotReloadDefs));
		}
		else
		{
			Log.Warning("[SillyAnimalThings] Couldn't find PlayDataLoader.HotReloadDefs, so hot reloading defs will lose the animals' abilities");
		}
	}

	// HotReloadDefs only queues the reload, so this queues the tags after it. ExecuteWhenFinished would run them straight away,
	// because the reload hasn't started yet.
	private static void Postfix_PlayDataLoader_HotReloadDefs()
	{
		LongEventHandler.QueueLongEvent(
			() =>
			{
				AnimalTagUtility.ApplyAllTags();
				Log.Message($"[SillyAnimalThings] Defs were hot reloaded, so the {DefDatabase<AnimalTagDef>.DefCount} animal tags were applied again");
			},
			"LoadingLongEvent",
			false,
			null
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
