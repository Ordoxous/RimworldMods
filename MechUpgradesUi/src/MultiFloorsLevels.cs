using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MultiFloors;
using MultiFloors.Jobs;
using Verse;
using Verse.AI;

namespace Ordo.MechUpgradesUi;

/// <summary>
/// MultiFloors (optional) makes each level of a base its own map, linked by stairs. With it, a mech can use an auto upgrader
/// on another level: it takes MultiFloors' stair job there, with the trip into the upgrader queued for when it arrives, as
/// MultiFloors' own <c>HarmonyPatch_MechChargeAcrossLevel</c> does for chargers. Its types are only touched inside the
/// NoInlining methods, which run only when it's active, so they're never resolved without it.
/// </summary>
public static class MultiFloorsLevels
{
    public static readonly bool Active = ModsConfig.IsActive("telardo.MultiFloors");

    /// <summary>The other levels of the map's stack, or none without MultiFloors.</summary>
    public static IEnumerable<Map> OtherLevels(Map map) =>
        Active && map != null ? OtherLevelsInt(map) : Enumerable.Empty<Map>();

    /// <summary>The other levels a pawn may go to by itself: none when MultiFloors' "stay on this level" is set.</summary>
    public static IEnumerable<Map> OtherLevelsFor(Pawn pawn) =>
        Active && pawn.MapHeld != null && !StaysOnLevel(pawn) ? OtherLevelsInt(pawn.MapHeld) : Enumerable.Empty<Map>();

    /// <summary>Whether a pawn can walk to a thing on another level, through the stairs.</summary>
    public static bool CanReach(Pawn pawn, Thing thing) => Active && CanReachInt(pawn, thing);

    /// <summary>Whether a thing on another level is forbidden to the pawn there.</summary>
    public static bool IsForbidden(Thing thing, Pawn pawn) => Active && IsForbiddenInt(thing, pawn);

    /// <summary>The stair trip to a thing on another level, with <paramref name="then"/> started on arrival; null if it can't be reached.</summary>
    public static Job TripTo(Pawn pawn, Thing thing, Job then) => Active ? TripToInt(pawn, thing, then) : null;

    /// <summary>Whether a pawn is on the stairs on its way to a thing on another level (the stair job's targetC).</summary>
    public static bool IsTakingStairsTo(Pawn pawn, Thing thing) =>
        Active && pawn.CurJobDef?.defName == "MF_ChangeLevelThroughStair" && pawn.CurJob.targetC.Thing == thing;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<Map> OtherLevelsInt(Map map) =>
        map.TryGetLevelControllerOnCurrentTile(out var controller) ? controller.GetAllLevelMaps(except: map).ToList() : [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool StaysOnLevel(Pawn pawn) => pawn.StayOnCurrentMap();

    // The reach check keeps the stair path for the next stair job; a check that isn't followed by a trip clears it
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CanReachInt(Pawn pawn, Thing thing)
    {
        var reachable = pawn.CanReachAcrossLevel(thing, thing.Map);
        pawn.SecondaryStairPath() = null;
        return reachable;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsForbiddenInt(Thing thing, Pawn pawn) => thing.IsForbiddenAcrossLevel(pawn);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Job TripToInt(Pawn pawn, Thing thing, Job then)
    {
        // Kept by the reach check, for the stair job to follow
        if (!pawn.CanReachAcrossLevel(thing, thing.Map))
            return null;

        pawn.NextJobThinkResult() = new ThinkResult(then, null, JobTag.Misc);
        return CrossLevelJobFactory.MakeChangeLevelThroughStairJob(thing, thing.Map);
    }
}
