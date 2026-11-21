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

        harmony.TryPatch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.SpawnSetup)),
            "Pawn.SpawnSetup",
            "animals that existed before the mod was added don't get its abilities",
            postfix: new HarmonyMethod(Postfix_Pawn_SpawnSetup)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_GetFood), nameof(JobGiver_GetFood.TryGiveJob)),
            "JobGiver_GetFood.TryGiveJob",
            "hungry animals don't gnaw wooden buildings",
            prefix: new HarmonyMethod(HungryGnawing.Prefix_JobGiver_GetFood_TryGiveJob)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.PostApplyDamage)),
            "Pawn.PostApplyDamage",
            "animals don't use their self-buffs when hurt",
            postfix: new HarmonyMethod(SelfBuffAutoCast.Postfix_Pawn_PostApplyDamage)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_AIFightEnemy), nameof(JobGiver_AIFightEnemy.GetAbilityJob)),
            "JobGiver_AIFightEnemy.GetAbilityJob",
            "fighting animals choose their abilities with vanilla's AI instead of this mod's",
            prefix: new HarmonyMethod(AnimalCombatPatches.Prefix_JobGiver_AIFightEnemy_GetAbilityJob)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(CompAbilityEffect_RequiresTrainable), nameof(CompAbilityEffect_RequiresTrainable.AICanTargetNow)),
            "CompAbilityEffect_RequiresTrainable.AICanTargetNow",
            "the player's animals use abilities they haven't been trained in, and not the ones they have",
            postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_CompAbilityEffect_RequiresTrainable_AICanTargetNow)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob"),
            "JobGiver_ReactToCloseMeleeThreat.TryGiveJob",
            "animals fighting back in melee don't use abilities",
            postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_ReactToCloseMeleeThreat_TryGiveJob)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_Manhunter), "TryGiveJob"),
            "JobGiver_Manhunter.TryGiveJob",
            "manhunters chase for longer before checking for an ability",
            postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_Manhunter_TryGiveJob)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_AIFightEnemy), "MeleeAttackJob"),
            "JobGiver_AIFightEnemy.MeleeAttackJob",
            "fighting animals chase for longer before checking for an ability",
            postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobGiver_AIFightEnemy_MeleeAttackJob)
        );

        harmony.TryPatch(
            AccessTools.Method(typeof(JobDriver_PredatorHunt), "CheckWarnPlayerInterval"),
            "JobDriver_PredatorHunt.CheckWarnPlayerInterval",
            "hunting predators don't use abilities on their prey",
            postfix: new HarmonyMethod(AnimalCombatPatches.Postfix_JobDriver_PredatorHunt_CheckWarnPlayerInterval)
        );

        // Dev mode's "Hot reload Defs" rebuilds every def from XML, which drops the abilities and trainables the tags add in code
        harmony.TryPatch(
            AccessTools.Method(typeof(PlayDataLoader), "HotReloadDefs"),
            "PlayDataLoader.HotReloadDefs",
            "hot reloading defs will lose the animals' abilities",
            postfix: new HarmonyMethod(Postfix_PlayDataLoader_HotReloadDefs)
        );
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
