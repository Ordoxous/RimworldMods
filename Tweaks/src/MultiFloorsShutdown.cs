using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MultiFloors;
using MultiFloors.Jobs;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using WVC_WorkModes;

namespace Ordo.Tweaks;

/// <summary>
/// Mech chargers and WVC shutdown zones across MultiFloors levels, each of which is its own map. When a mech's level has
/// no free charger, haulers carry it to one on another level; when it has no usable zone cell, to a zone on another level;
/// and a mech walking to a zone in a shutdown work mode takes the stairs. Everything here touches MultiFloors' types, so
/// it's only reached through <see cref="TweaksSettings.ShutdownZonesAcrossLevels"/> and
/// <see cref="TweaksSettings.ChargersAcrossLevels"/>, which are false without MultiFloors, and the entry points aren't
/// inlined into callers that run without it.
/// </summary>
public static class MultiFloorsShutdown
{
    // Reach across levels runs MultiFloors' stair path finder, so only this many free cells or chargers are tried per level
    private const int MaxReachChecksPerLevel = 8;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HasChargerOnOtherLevel(
        Pawn hauler,
        Pawn mech
    )
    {
        var found = TryFindChargerOnOtherLevel(hauler, mech, out _);
        hauler.SecondaryStairPath() = null;
        return found;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Job TryMakeChargerCarryJob(
        Pawn hauler,
        Pawn mech
    ) => TryFindChargerOnOtherLevel(hauler, mech, out var charger)
        ? MakeCarryJob(mech, new GlobalTargetInfo(charger))
        : null;

    // A charger on another level, nearest levels first, as vanilla's GetClosestCharger picks one on the mech's level: it
    // can charge the mech now, and the hauler can reach it through the stairs. Forbiddance and reservations are checked
    // the cross-map way.
    private static bool TryFindChargerOnOtherLevel(
        Pawn hauler,
        Pawn mech,
        out Building_MechCharger charger
    )
    {
        charger = null;
        var map = hauler.Map;
        if (map == null || hauler.StayOnCurrentMap() || !map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return false;
        }

        foreach (var level in map.GetOtherMapVerticallyOutward(controller))
        {
            var reachChecks = 0;
            foreach (var candidate in level.listerThings.ThingsInGroup(ThingRequestGroup.MechCharger))
            {
                if (candidate is not Building_MechCharger free
                    || !free.CanPawnChargeCurrently(mech)
                    || free.IsForbiddenAcrossLevel(hauler)
                    || level.reservationManager.IsReservedByAnyoneOf(free, hauler.Faction))
                {
                    continue;
                }

                if (hauler.CanReachAcrossLevel(free, level))
                {
                    charger = free;
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HasCellOnOtherLevel(
        Pawn hauler,
        Pawn mech,
        MechanoidWorkType workType,
        bool nearCharger
    )
    {
        var found = TryFindCellOnOtherLevel(hauler, mech, workType, nearCharger, out _, out _);
        // The reach check keeps the stair path for the next stair job, which may not be this one
        hauler.SecondaryStairPath() = null;
        return found;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Job TryMakeCarryJob(
        Pawn hauler,
        Pawn mech,
        MechanoidWorkType workType,
        bool nearCharger
    ) => TryFindCellOnOtherLevel(hauler, mech, workType, nearCharger, out var destMap, out var cell)
        ? MakeCarryJob(mech, new GlobalTargetInfo(cell, destMap))
        : null;

    // The destination, a charger or a zone cell, is kept in globalTarget, not a local target, since it's on another map
    public static Job MakeCarryJob(
        Pawn mech,
        GlobalTargetInfo destination
    )
    {
        var job = JobMaker.MakeJob(TweaksDefOf.Ordo_CarryMechToOtherLevel);
        job.targetB = mech;
        job.globalTarget = destination;
        job.count = 1;
        return job;
    }

    // Once the hauler is on the destination's level: the same-map job, which sees the mech is already carried. The planned
    // charger or cell first, since it may have been taken on the way; then another charger on this level, for a mech that
    // was going to one; then a zone cell.
    public static Job HaulOnArrival(
        Pawn hauler,
        Pawn mech,
        GlobalTargetInfo destination
    )
    {
        if (destination.Thing is Building_MechCharger planned)
        {
            var charger = ChargerUsable(planned, hauler, mech)
                ? planned
                : hauler.Map.listerThings.ThingsInGroup(ThingRequestGroup.MechCharger)
                    .OfType<Building_MechCharger>()
                    .FirstOrDefault(candidate => ChargerUsable(candidate, hauler, mech));
            if (charger != null)
            {
                var job = JobMaker.MakeJob(TweaksDefOf.Ordo_DeliverMechToCharger, mech, charger, charger.InteractionCell);
                job.count = 1;
                return job;
            }
        }

        return ZoneJobOnArrival(hauler, mech, destination.HasThing ? IntVec3.Invalid : destination.Cell);
    }

    private static bool ChargerUsable(
        Building_MechCharger charger,
        Pawn hauler,
        Pawn mech
    ) => charger.Spawned
         && charger.Map == hauler.Map
         && charger.CanPawnChargeCurrently(mech)
         && !charger.IsForbidden(hauler)
         && hauler.CanReserve(charger)
         && hauler.CanReach(charger, PathEndMode.InteractionCell, Danger.Some);

    private static Job ZoneJobOnArrival(
        Pawn hauler,
        Pawn mech,
        IntVec3 plannedCell
    )
    {
        var downed = mech.Downed;
        var cell = plannedCell;
        if (!ShutdownCells.Usable(cell, hauler, mech)
            && !ShutdownCells.TryFindCell(hauler, mech, MechanoidWorkType.Work, nearCharger: !downed, out cell))
        {
            return null;
        }

        var job = JobMaker.MakeJob(downed ? TweaksDefOf.HaulMechForRepair : TweaksDefOf.Ordo_HaulMechToShutdownZone, mech, cell);
        job.count = 1;
        return job;
    }

    // A zone cell on another level, nearest levels first, that the traveller (a hauler, or the mech walking) can reach
    // through the stairs and nobody has reserved
    private static bool TryFindCellOnOtherLevel(
        Pawn traveller,
        Pawn mech,
        MechanoidWorkType workType,
        bool nearCharger,
        out Map destMap,
        out IntVec3 cell
    )
    {
        destMap = null;
        cell = IntVec3.Invalid;
        var map = traveller.Map;
        if (map == null || traveller.StayOnCurrentMap() || !map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return false;
        }

        foreach (var level in map.GetOtherMapVerticallyOutward(controller))
        {
            var reachChecks = 0;
            foreach (var candidate in ShutdownCells.InPreferredOrder(ShutdownCells.ZoneCells(level, mech, workType), level, mech, traveller,
                         nearCharger))
            {
                // ReservationManager.CanReserve refuses a claimant on another map, so only ask whether it's taken
                if (!ShutdownCells.Suitable(candidate, level, traveller, mech)
                    || level.reservationManager.IsReservedByAnyoneOf(candidate, traveller.Faction))
                {
                    continue;
                }

                if (CanReachAcrossLevel(traveller, level, candidate))
                {
                    destMap = level;
                    cell = candidate;
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

    // MultiFloors' FindPathTo(map) picks the arrival stair that can reach the pawn's ReferencePosition, and clears it
    // once read. Cleared here too, for when it returns before reading it.
    private static bool CanReachAcrossLevel(
        Pawn traveller,
        Map level,
        IntVec3 cell
    )
    {
        traveller.ReferencePosition() = cell;
        try
        {
            return traveller.CanReachAcrossLevel(null, level);
        }
        finally
        {
            traveller.ReferencePosition() = IntVec3.Invalid;
        }
    }

    // MultiFloors keeps its own level-changing jobs in a private set, so that a gravship launch or a ritual leaves out
    // pawns that are on the stairs. Ours joins it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterChangingLevelJob()
    {
        if (AccessTools.Field(typeof(ChangingLevelJobDefs), "changingLevelJobDefs")?.GetValue(null) is HashSet<JobDef> jobDefs)
        {
            jobDefs.Add(TweaksDefOf.Ordo_CarryMechToOtherLevel);
        }
        else
        {
            Log.Warning("[Tweaks] Couldn't find MultiFloors' ChangingLevelJobDefs.changingLevelJobDefs, so a gravship launch or "
                        + "ritual may count a hauler carrying a mech up the stairs as present.");
        }
    }

    public static void Patch(
        Harmony harmony
    )
    {
        harmony.TryPatch(
            AccessTools.Method(typeof(JobGiver_GoToShutdownZone), "TryGiveJob"),
            "WVC's JobGiver_GoToShutdownZone.TryGiveJob",
            "mechs in a shutdown work mode don't look for shutdown zones on other levels",
            postfix: new HarmonyMethod(typeof(MultiFloorsShutdown), nameof(Postfix_JobGiver_GoToShutdownZone_TryGiveJob))
        );
    }

    // WVC gives no job when the mech is already in a zone or shutdown room, or when its map has no free zone cell. In the
    // last case, the mech takes the stairs to a level that has one, and WVC's job giver sends it to the cell from there.
    public static void Postfix_JobGiver_GoToShutdownZone_TryGiveJob(
        JobGiver_GoToShutdownZone __instance,
        ref Job __result,
        Pawn pawn
    )
    {
        if (__result != null
            || !pawn.Spawned
            || !TweaksSettings.ShutdownZonesAcrossLevels
            || !WVC_MMWM.settings.enable_GoToShutdownZoneJob
            || ShutdownUtility.MechInShutdownZone(pawn, pawn.Position, __instance.workModeType))
        {
            return;
        }

        if (__instance.possibleRooms != null
            && WVC_MMWM.settings.enable_GoToShutdownRoomJob
            && pawn.GetRoom() is { } room
            && __instance.PossibleRooms.Contains(room.Role))
        {
            return;
        }

        __result = TryMakeWalkJob(pawn, __instance.workModeType);
    }

    // The reach check keeps the stair path, which the stair job then follows
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Job TryMakeWalkJob(
        Pawn mech,
        MechanoidWorkType workType
    ) => TryFindCellOnOtherLevel(mech, mech, workType, nearCharger: false, out var destMap, out _)
        ? CrossLevelJobFactory.MakeChangeLevelThroughStairJob(null, destMap)
        : null;
}

[StaticConstructorOnStartup]
public static class MultiFloorsShutdownPatches
{
    static MultiFloorsShutdownPatches()
    {
        if (!ModsConfig.IsActive("telardo.MultiFloors"))
        {
            return;
        }

        var harmony = new Harmony(typeof(MultiFloorsShutdownPatches).Namespace);
        MultiFloorsShutdown.Patch(harmony);
        MultiFloorsShutdown.RegisterChangingLevelJob();
        MultiFloorsResources.Patch(harmony);
        MultiFloorsReadout.Patch(harmony);
        MultiFloorsBills.Patch(harmony);
        MultiFloorsSupercharger.Patch(harmony);
        if (MultiFloorsWorkTab.WorkTabActive)
        {
            MultiFloorsWorkTab.Patch(harmony);
        }
    }
}

/// <summary>
/// Carries a mech up or down MultiFloors' stairs to the level its charger or shutdown zone is on (globalTarget),
/// following MultiFloors' own <c>JobDriver_TakeToBedOnOtherLevel</c>. Each stair is one job: on the next level it starts
/// another for the next stair, or on the destination's level, the same-map job.
/// It derives from JobDriver, not MultiFloors' JobDriver_ChangeLevel: the game lists every type in the assembly at
/// startup, and one whose base type is missing would make Tweaks unusable without MultiFloors. (MultiFloors' types in
/// member signatures are fine, as they're only resolved when used.) What JobDriver_ChangeLevel adds is copied in.
/// </summary>
public class JobDriver_CarryMechToOtherLevel : JobDriver
{
    private const TargetIndex NextStairIndex = TargetIndex.A;
    private const TargetIndex MechIndex = TargetIndex.B;

    private Pawn Mech => (Pawn)job.GetTarget(MechIndex).Thing;

    private Stair NextStair => job.GetTarget(NextStairIndex).Thing as Stair;

    // JobDriver_ChangeLevel's ForcedBodyOffset (the hauler drawn climbing the stairs) is left out: MultiFloors'
    // DrawOffset returns Unity's Vector3 from an assembly this build doesn't reference
    private Map DestMap => job.globalTarget.Map;

    public override bool TryMakePreToilReservations(
        bool errorOnFailed
    ) => pawn.IsCarryingThing(Mech) || pawn.Reserve(Mech, job, errorOnFailed: errorOnFailed);

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(MechIndex);
        var carrying = Toils_General.Label();
        yield return Toils_Jump.JumpIf(carrying, () => pawn.IsCarryingThing(Mech));
        yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.ClosestTouch)
            .FailOnDespawnedNullOrForbidden(MechIndex)
            .FailOnSomeonePhysicallyInteracting(MechIndex)
            .FailOn(() => !Mech.Downed && !UnpoweredMechHauling.NeedsCharger(Mech));
        yield return Toils_Haul.StartCarryThing(MechIndex);
        yield return carrying;
        // A charger is the path's destination thing; a cell has none
        yield return Toils_Stairs.FindNextStair(pawn, NextStairIndex, DestMap, job.globalTarget.Thing);
        foreach (var toil in Toils_Stairs.GoToStairs(pawn, NextStairIndex, () => NextStair, ReadyForNextToil))
        {
            yield return toil;
        }

        yield return ChangeLevel();
        // SwitchMap ends the job as succeeded, so this only runs when the trip fails: JobDriver_ChangeLevel's
        // CleanupMovement, and the mech is put down
        AddFinishAction(condition =>
        {
            if (condition != JobCondition.Succeeded)
            {
                pawn.CurrentStairPath() = null;
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            }

            if (NextStair is Elevator elevator)
            {
                elevator.SwitchingCleanup(pawn);
            }
        });
    }

    private Toil ChangeLevel()
    {
        var toil = ToilMaker.MakeToil("Ordo_CarryMechToOtherLevel");
        toil.initAction = () =>
        {
            var entrance = NextStair;
            var enterCell = CrossLevelMoveJobUtility.ValidateEnterCell(entrance, out var newMap, pawn);
            if (!enterCell.IsValid)
            {
                EndJobWith(JobCondition.Incompletable);
                return;
            }

            // Read before SwitchMap ends this job
            var hauler = pawn;
            var mech = Mech;
            var destMap = DestMap;
            var destination = job.globalTarget;
            CrossLevelMoveJobUtility.CleanupLordBeforeSwitchingLevel(hauler);
            CrossLevelMoveJobUtility.SwitchMap(hauler, entrance, newMap, enterCell);

            var next = CrossLevelMoveJobUtility.NotReachedDestination(hauler, destMap)
                ? MultiFloorsShutdown.MakeCarryJob(mech, destination)
                : MultiFloorsShutdown.HaulOnArrival(hauler, mech, destination);
            // The same-map jobs drop what the hauler carries before they start, unless told not to
            if (next != null)
            {
                hauler.jobs.StartJob(next, JobCondition.None, keepCarryingThingOverride: true);
            }
            else
            {
                hauler.carryTracker.TryDropCarriedThing(hauler.Position, ThingPlaceMode.Near, out _);
            }
        };
        return toil;
    }
}
