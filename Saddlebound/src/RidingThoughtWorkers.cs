using RimWorld;
using Verse;

namespace Ordo.Saddlebound;

// Riding: Respected. "Rode recently" while riding and for a day after.
public class ThoughtWorker_Precept_RodeRecently : ThoughtWorker_Precept
{
    public const int RecentTicks = GenDate.TicksPerDay;

    public override ThoughtState ShouldHaveThought(
        Pawn p
    )
    {
        var tracker = RidingTracker.Instance;
        if (tracker == null || !GiddyUpAdapter.CanEverRide(p))
        {
            return ThoughtState.Inactive;
        }

        return tracker.TicksSinceRide(p) <= RecentTicks ? ThoughtState.ActiveDefault : ThoughtState.Inactive;
    }
}

// Riding: Essential. Stage 0 while riding and for a day after, nothing on the second day, then stage 1 from day 2 and
// stage 2 from day 4. The penalties only apply while there's an animal the pawn could ride.
public class ThoughtWorker_Precept_RidingEssential : ThoughtWorker_Precept
{
    public const int RecentTicks = GenDate.TicksPerDay;
    public const int MissingTicks = 2 * GenDate.TicksPerDay;
    public const int LongMissingTicks = 4 * GenDate.TicksPerDay;

    public const int StageRodeRecently = 0;
    public const int StageMissing = 1;
    public const int StageLongMissing = 2;

    public override ThoughtState ShouldHaveThought(
        Pawn p
    )
    {
        var tracker = RidingTracker.Instance;
        if (tracker == null || !GiddyUpAdapter.CanEverRide(p))
        {
            return ThoughtState.Inactive;
        }

        var since = tracker.TicksSinceRide(p);
        if (since <= RecentTicks)
        {
            return ThoughtState.ActiveAtStage(StageRodeRecently);
        }

        if (since < MissingTicks || !tracker.HasRideableMount(p))
        {
            return ThoughtState.Inactive;
        }

        return ThoughtState.ActiveAtStage(since < LongMissingTicks ? StageMissing : StageLongMissing);
    }
}
