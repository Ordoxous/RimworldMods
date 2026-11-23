using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MultiFloors;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors, construction and repairs on another level no longer get stuck. MultiFloors claims a frame, blueprint
/// or broken-down building for one pawn's cross-level job with a flag on the thing (<c>HandlingByOthers</c>), and every
/// pawn skips a claimed thing. The flag is saved with the game, and several paths leave it set with no job behind it:
/// - <c>WorkGiver_ConstructDeliverResourcesAcrossLevel.NonScanJob</c> makes the delivery job, which sets the flag, before
///   checking the pawn can reach the frame through the stairs, and drops the job when it can't.
/// - <c>JobDriver_DeliverResourcesAcrossLevel</c> clears it only when the job fails: on arrival, the stair toil hands the
///   claim to a new ordered job, which may never start.
/// - <c>JobDriver_DeliverToContainer</c> clears only its current target when it ends, not the nearby frames still queued.
/// - <c>WorkGiver_FixBrokenDownBuildingAcrossLevel</c> hands the repair over as the pawn's next think result after the
///   climb, which the pawn may never take.
/// Every so often, and on load, the flag is cleared from things that no pawn's current, queued or next job targets.
/// Everything here touches MultiFloors' types, so it's only reached when MultiFloors is active.
/// </summary>
public static class MultiFloorsStuckClaims
{
    // An in-game hour: a claim a job dropped is put back within it, and the check stays rare
    public const int IntervalTicks = 2500;

    // The things whose claim was cleared, for tests
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<Thing> Release()
    {
        var targeted = new HashSet<Thing>();
        foreach (var map in Find.Maps)
        {
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.jobs == null)
                {
                    continue;
                }

                AddTargets(targeted, pawn.CurJob);
                foreach (var queued in pawn.jobs.jobQueue)
                {
                    AddTargets(targeted, queued.job);
                }

                AddTargets(targeted, pawn.NextJobThinkResult().Job);
            }
        }

        var released = new List<Thing>();
        foreach (var map in Find.Maps)
        {
            Release(map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame), targeted, released);
            Release(map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint), targeted, released);
            if (map.GetComponent<BreakdownManager>() is { } breakdowns)
            {
                Release(breakdowns.brokenDownThings, targeted, released);
            }
        }

        return released;
    }

    // Whether MultiFloors counts the thing as claimed, for tests
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool IsClaimed(
        Thing thing
    ) => thing.HandlingByOthers();

    // Claims the thing as MultiFloors' jobs do, for tests
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Claim(
        Thing thing
    ) => thing.HandlingByOthers() = true;

    private static void Release(
        IEnumerable<Thing> things,
        HashSet<Thing> targeted,
        List<Thing> released
    )
    {
        foreach (var thing in things)
        {
            if (thing.HandlingByOthers() && !targeted.Contains(thing))
            {
                thing.HandlingByOthers() = false;
                released.Add(thing);
            }
        }
    }

    private static void AddTargets(
        HashSet<Thing> targeted,
        Job job
    )
    {
        if (job == null)
        {
            return;
        }

        AddTarget(targeted, job.targetA);
        AddTarget(targeted, job.targetB);
        AddTarget(targeted, job.targetC);
        AddQueue(targeted, job.targetQueueA);
        AddQueue(targeted, job.targetQueueB);
    }

    private static void AddQueue(
        HashSet<Thing> targeted,
        List<LocalTargetInfo> queue
    )
    {
        if (queue == null)
        {
            return;
        }

        foreach (var target in queue)
        {
            AddTarget(targeted, target);
        }
    }

    private static void AddTarget(
        HashSet<Thing> targeted,
        LocalTargetInfo target
    )
    {
        if (target.HasThing)
        {
            targeted.Add(target.Thing);
        }
    }
}

/// <summary>Runs <see cref="MultiFloorsStuckClaims"/> on load and every in-game hour. Holds no MultiFloors types, since the
/// game makes one for every game, with or without MultiFloors.</summary>
public class GameComponent_MultiFloorsStuckClaims : GameComponent
{
    // Off the hour, away from other hourly work
    private const int TickOffset = 1213;

    public GameComponent_MultiFloorsStuckClaims(
        Game game
    )
    {
    }

    public override void LoadedGame() => Check();

    public override void GameComponentTick()
    {
        if (Find.TickManager.TicksGame % MultiFloorsStuckClaims.IntervalTicks == TickOffset)
        {
            Check();
        }
    }

    private static void Check()
    {
        if (!TweaksSettings.ClaimsAcrossLevels)
        {
            return;
        }

        var released = MultiFloorsStuckClaims.Release();
        if (released.Count > 0 && Prefs.DevMode)
        {
            Log.Message($"[Tweaks] Released MultiFloors' stuck claim on {released.Count} thing(s): {string.Join(", ", released)}");
        }
    }
}
