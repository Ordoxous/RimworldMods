using System.Runtime.CompilerServices;
using MultiFloors;
using Verse;
using Verse.AI;

namespace Ordo.ColonistBarJobs;

// MultiFloors (optional) makes each level of a base its own map, linked by stairs. A pawn that found work on another level
// first takes MF_ChangeLevelThroughStair there, whose target is the stair, and MultiFloors keeps the job it will start on
// arrival in the pawn's NextJobThinkResult. Its types are only touched inside the NoInlining method, which runs only when
// it's active, so they're never resolved without it.
public static class MultiFloorsLevels
{
    private static readonly bool Active = ModsConfig.IsActive("telardo.MultiFloors");

    private static JobDef _stairJob;
    private static bool _looked;

    // The job a pawn going up or down the stairs will start on arrival, or null if it isn't doing that or has none queued
    public static Job JobAfterStairs(
        Pawn pawn,
        Job job
    )
    {
        if (!Active || job == null)
        {
            return null;
        }

        if (!_looked)
        {
            _stairJob = DefDatabase<JobDef>.GetNamedSilentFail("MF_ChangeLevelThroughStair");
            _looked = true;
        }

        return job.def == _stairJob && _stairJob != null ? NextJob(pawn) : null;
    }

    // Defs are new objects after a def hot reload, so look the stair job up again
    public static void Reset() => _looked = false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Job NextJob(
        Pawn pawn
    ) => pawn.NextJobThinkResult().Job;
}
