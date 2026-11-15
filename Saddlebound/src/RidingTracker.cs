using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ordo.Saddlebound;

// Who rode when, and who rides each mount. Stores ticks and thing IDs, not live state, so it doesn't matter whether it
// loads before or after Giddy-Up's own storage.
public class RidingTracker : GameComponent
{
    // Rider thing ID -> the last tick they were seen riding (on mounting and on dismounting)
    private Dictionary<int, int> lastRideTick = new();

    // Mount thing ID -> rider thing ID -> times ridden
    private Dictionary<int, MountRecord> mounts = new();

    // How long the mountable-animal answer is kept per rider
    private const int RideableCacheTicks = GenDate.TicksPerHour;
    private readonly Dictionary<int, (int tick, bool any)> rideableCache = new();

    public RidingTracker(
        Game game
    )
    {
    }

    public static RidingTracker Instance => Current.Game?.GetComponent<RidingTracker>();

    public void Notify_Mounted(
        Pawn rider,
        Pawn mount
    )
    {
        lastRideTick[rider.thingIDNumber] = Find.TickManager.TicksGame;
        if (mount == null)
        {
            return;
        }

        if (!mounts.TryGetValue(mount.thingIDNumber, out var record))
        {
            record = new MountRecord();
            mounts[mount.thingIDNumber] = record;
        }

        record.Notify_RiddenBy(rider);
    }

    public void Notify_Dismounted(
        Pawn rider
    ) => lastRideTick[rider.thingIDNumber] = Find.TickManager.TicksGame;

    public void Notify_Died(
        Pawn pawn
    )
    {
        lastRideTick.Remove(pawn.thingIDNumber);
        mounts.Remove(pawn.thingIDNumber);
        rideableCache.Remove(pawn.thingIDNumber);
    }

    public bool HasBeenRidden(
        Pawn mount
    ) => mounts.ContainsKey(mount.thingIDNumber);

    public MountRecord RecordFor(
        Pawn mount
    ) => mounts.TryGetValue(mount.thingIDNumber, out var record) ? record : null;

    public static bool RidingNow(
        Pawn pawn
    ) => GiddyUpAdapter.IsMounted(pawn) || GiddyUpAdapter.RidesInCaravan(pawn);

    // Ticks since the pawn last rode: 0 while riding. A pawn the tracker has never seen starts its count now, so a new
    // colonist or a mod added mid-game doesn't start with the worst penalty.
    public int TicksSinceRide(
        Pawn pawn
    )
    {
        var now = Find.TickManager.TicksGame;
        if (RidingNow(pawn))
        {
            lastRideTick[pawn.thingIDNumber] = now;
            return 0;
        }

        if (!lastRideTick.TryGetValue(pawn.thingIDNumber, out var tick))
        {
            lastRideTick[pawn.thingIDNumber] = now;
            return 0;
        }

        return now - tick;
    }

    // Is there an animal the pawn could ride where they are: on their map, or in their caravan. Penalties for not
    // riding are suppressed without one, and while Giddy-Up can't let them ride at all.
    public bool HasRideableMount(
        Pawn rider
    )
    {
        var now = Find.TickManager.TicksGame;
        if (rideableCache.TryGetValue(rider.thingIDNumber, out var cached) && now - cached.tick < RideableCacheTicks)
        {
            return cached.any;
        }

        var any = FindRideableMount(rider);
        rideableCache[rider.thingIDNumber] = (now, any);
        return any;
    }

    public void ClearRideableCache() => rideableCache.Clear();

    // For tests: pretend the pawn last rode at this tick
    public void SetLastRideTick(
        Pawn pawn,
        int tick
    ) => lastRideTick[pawn.thingIDNumber] = tick;

    public bool TryGetLastRideTick(
        Pawn pawn,
        out int tick
    ) => lastRideTick.TryGetValue(pawn.thingIDNumber, out tick);

    private static bool FindRideableMount(
        Pawn rider
    )
    {
        if (!GiddyUpAdapter.CanEverRide(rider))
        {
            return false;
        }

        IEnumerable<Pawn> candidates;
        if (rider.Spawned)
        {
            candidates = rider.Map.mapPawns.SpawnedPawnsInFaction(rider.Faction);
        }
        else if (rider.GetCaravan() is { } caravan)
        {
            candidates = caravan.PawnsListForReading;
        }
        else
        {
            return false;
        }

        return candidates.Any(animal => animal.RaceProps.Animal && !animal.Dead && GiddyUpAdapter.CouldRide(rider, animal));
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref lastRideTick, "lastRideTick", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref mounts, "mounts", LookMode.Value, LookMode.Deep);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            lastRideTick ??= new Dictionary<int, int>();
            mounts ??= new Dictionary<int, MountRecord>();
        }
    }
}

// Who has ridden one mount, and how often
public class MountRecord : IExposable
{
    private Dictionary<int, int> rideCounts = new();
    public int lastRiderId = -1;

    public void Notify_RiddenBy(
        Pawn rider
    )
    {
        rideCounts.TryGetValue(rider.thingIDNumber, out var count);
        rideCounts[rider.thingIDNumber] = count + 1;
        lastRiderId = rider.thingIDNumber;
    }

    public int RideCount(
        Pawn rider
    ) => rideCounts.TryGetValue(rider.thingIDNumber, out var count) ? count : 0;

    // The living player pawn who rode it most; ties go to whoever rode it last
    public Pawn UsualRider()
    {
        var riders = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction;
        Pawn best = null;
        var bestCount = 0;
        foreach (var pawn in riders)
        {
            if (!rideCounts.TryGetValue(pawn.thingIDNumber, out var count))
            {
                continue;
            }

            if (count > bestCount || (count == bestCount && pawn.thingIDNumber == lastRiderId))
            {
                best = pawn;
                bestCount = count;
            }
        }

        return best;
    }

    public void ExposeData()
    {
        Scribe_Collections.Look(ref rideCounts, "rideCounts", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref lastRiderId, "lastRiderId", -1);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            rideCounts ??= new Dictionary<int, int>();
        }
    }
}
