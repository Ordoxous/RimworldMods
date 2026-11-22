using System.Runtime.CompilerServices;
using HarmonyLib;
using MultiFloors;
using MultiFloors.Jobs;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors, colonists take the stairs to a neural supercharger (Ideology) on another level. Vanilla's
/// JobGiver_GetNeuralSupercharge looks only on the pawn's map, and gives the job a priority of 0 when it finds none there,
/// so the think tree never asks it for a job. MultiFloors does the same for mech chargers
/// (<c>HarmonyPatch_MechChargeAcrossLevel</c>) but leaves this one alone.
/// - A postfix on <c>GetPriority</c> gives vanilla's 9.25 when the pawn is due a charge and a charged supercharger it may
///   auto-use is reachable on another level.
/// - A postfix on <c>TryGiveJob</c>, when vanilla gives none, takes the stairs to that level, with vanilla's
///   GetNeuralSupercharge job queued for arrival (MultiFloors' <c>NextJobThinkResult</c>).
/// A supercharger on the pawn's own level always comes first: both postfixes only act when vanilla found none.
/// Everything here touches MultiFloors' types, so it's only patched when MultiFloors is active.
/// </summary>
public static class MultiFloorsSupercharger
{
    // Vanilla's GetPriority for a pawn due a charge
    private const float Priority = 9.25f;

    // Vanilla's cooldown between charges
    private const int CooldownTicks = 30000;

    // Reach across levels runs MultiFloors' stair path finder, so only this many superchargers are tried per level
    private const int MaxReachChecksPerLevel = 8;

    public static void Patch(
        Harmony harmony
    )
    {
        const string lost = "colonists don't use neural superchargers on other levels";
        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_GetNeuralSupercharge), nameof(JobGiver_GetNeuralSupercharge.GetPriority)),
            "JobGiver_GetNeuralSupercharge.GetPriority",
            lost,
            postfix: new HarmonyMethod(typeof(MultiFloorsSupercharger), nameof(Postfix_GetPriority))
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_GetNeuralSupercharge), "TryGiveJob"),
            "JobGiver_GetNeuralSupercharge.TryGiveJob",
            lost,
            postfix: new HarmonyMethod(typeof(MultiFloorsSupercharger), nameof(Postfix_TryGiveJob))
        );
    }

    public static void Postfix_GetPriority(
        ref float __result,
        Pawn pawn
    )
    {
        if (__result > 0f || !DueCharge(pawn))
        {
            return;
        }

        // The reach check keeps a stair path for a job, and none follows from here
        var found = TryFindOnOtherLevel(pawn, out _, out _);
        pawn.SecondaryStairPath() = null;
        if (found)
        {
            __result = Priority;
        }
    }

    public static void Postfix_TryGiveJob(
        ref Job __result,
        Pawn pawn
    )
    {
        if (__result != null || !DueCharge(pawn) || !TryFindOnOtherLevel(pawn, out var supercharger, out var level))
        {
            return;
        }

        // The stair job follows the path the reach check kept, then starts the queued job on arrival
        pawn.NextJobThinkResult() = new ThinkResult(JobMaker.MakeJob(JobDefOf.GetNeuralSupercharge, supercharger), null, JobTag.SatisfyingNeeds);
        __result = CrossLevelJobFactory.MakeChangeLevelThroughStairJob(supercharger, level);
    }

    // Vanilla's conditions short of finding a supercharger
    private static bool DueCharge(
        Pawn pawn
    )
    {
        if (!ModsConfig.IdeologyActive || !pawn.Spawned || !TweaksSettings.SuperchargersAcrossLevels)
        {
            return false;
        }

        var last = pawn.health.lastReceivedNeuralSuperchargeTick;
        return last == -1 || Find.TickManager.TicksGame - last >= CooldownTicks;
    }

    // A supercharger on another level, nearest levels first, that vanilla's validator would take: charged, not
    // forbidden, set to auto-use for this pawn. Also unreserved and reachable through the stairs.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TryFindOnOtherLevel(
        Pawn pawn,
        out Thing supercharger,
        out Map level
    )
    {
        supercharger = null;
        level = null;
        var map = pawn.Map;
        if (map == null || pawn.StayOnCurrentMap() || !map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return false;
        }

        foreach (var other in map.GetOtherMapVerticallyOutward(controller))
        {
            var reachChecks = 0;
            foreach (var candidate in other.listerThings.ThingsOfDef(ThingDefOf.NeuralSupercharger))
            {
                if (candidate.TryGetComp<CompNeuralSupercharger>() is not { Charged: true } comp
                    || !comp.CanAutoUse(pawn)
                    || candidate.IsForbiddenAcrossLevel(pawn)
                    || other.reservationManager.IsReservedByAnyoneOf(candidate, pawn.Faction))
                {
                    continue;
                }

                if (pawn.CanReachAcrossLevel(candidate, other))
                {
                    supercharger = candidate;
                    level = other;
                    return true;
                }

                if (++reachChecks >= MaxReachChecksPerLevel)
                {
                    break;
                }
            }
        }

        return false;
    }
}
