using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using WVC_WorkModes;

namespace Ordo.Tweaks;

[DefOf]
public static class TweaksDefOf
{
    public static JobDef HaulMechForRepair;
    public static JobDef Ordo_HaulMechToShutdownZone;

    [MayRequire("telardo.MultiFloors")]
    public static JobDef Ordo_CarryMechToOtherLevel;

    [MayRequire("telardo.MultiFloors")]
    public static JobDef Ordo_DeliverMechToCharger;

    static TweaksDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TweaksDefOf));
}

public class JobDriver_HaulMechForRepair : JobDriver
{
    private const TargetIndex MechIndex = TargetIndex.A;
    private const TargetIndex ZoneCellIndex = TargetIndex.B;

    private Pawn Mech => (Pawn)job.GetTarget(MechIndex).Thing;

    // A hauler arriving from another MultiFloors level starts this job already carrying the mech
    public override bool TryMakePreToilReservations(
        bool errorOnFailed
    )
    {
        return pawn.Reserve(job.GetTarget(ZoneCellIndex), job, errorOnFailed: errorOnFailed)
               && (pawn.IsCarryingThing(Mech) || pawn.Reserve(job.GetTarget(MechIndex), job, errorOnFailed: errorOnFailed));
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(MechIndex);
        this.FailOnDespawnedNullOrForbidden(ZoneCellIndex);
        var carrying = Toils_General.Label();
        yield return Toils_Jump.JumpIf(carrying, () => pawn.IsCarryingThing(Mech));
        yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.ClosestTouch)
            .FailOnSomeonePhysicallyInteracting(MechIndex)
            .FailOn(() => !Mech.Downed);
        yield return Toils_Haul.StartCarryThing(MechIndex);
        yield return carrying;
        yield return Toils_Haul.CarryHauledThingToCell(ZoneCellIndex, PathEndMode.OnCell);
        yield return Toils_Haul.PlaceHauledThingInCell(ZoneCellIndex, null, false);
    }
}

public class WorkGiver_HaulMechForRepair : WorkGiver_Scanner
{

    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(
        Pawn pawn
    ) => pawn.Map.mapPawns.SpawnedColonyMechs.Where(mech => mech.Downed);

    public override bool ShouldSkip(
        Pawn pawn,
        bool forced = false
    ) => !TweaksSettings.HaulDownedMechs || !PotentialWorkThingsGlobal(pawn).Any();

    public override bool HasJobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    )
    {
        var targetPawn = (Pawn)target;

        if (
            !targetPawn.IsColonyMech
            || !targetPawn.Downed
            || targetPawn.CurJobDef == JobDefOf.MechCharge
            || targetPawn.IsForbidden(pawn)
            || !pawn.CanReserve((LocalTargetInfo)targetPawn, ignoreOtherReservations: forced)
            || ShutdownUtility.MechInShutdownZone(targetPawn, targetPawn.Position, MechanoidWorkType.Work)
            || UnpoweredMechHauling.VanillaTakesToCharger(targetPawn, pawn, forced)
        )
        {
            return false;
        }

        return DownedMechHauling.HasCell(pawn, targetPawn);
    }

    public override Job JobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    ) => DownedMechHauling.MakeJob(pawn, (Pawn)target);
}

public static class DownedMechHauling
{
    // A random zone cell on the hauler's map
    public static bool TryFindCell(
        Pawn hauler,
        Pawn mech,
        out IntVec3 cell
    ) => ShutdownCells.TryFindCell(hauler, mech, MechanoidWorkType.Work, nearCharger: false, out cell);

    // The job taking the mech to a zone: on the hauler's level, else (with MultiFloors) on another one
    public static Job MakeJob(
        Pawn hauler,
        Pawn mech
    )
    {
        if (TryFindCell(hauler, mech, out var cell))
        {
            var job = JobMaker.MakeJob(TweaksDefOf.HaulMechForRepair, mech, cell);
            job.count = 1;
            return job;
        }

        return TweaksSettings.ShutdownZonesAcrossLevels
            ? MultiFloorsShutdown.TryMakeCarryJob(hauler, mech, MechanoidWorkType.Work, nearCharger: false)
            : null;
    }

    public static bool HasCell(
        Pawn hauler,
        Pawn mech
    ) => TryFindCell(hauler, mech, out _)
         || (TweaksSettings.ShutdownZonesAcrossLevels
             && MultiFloorsShutdown.HasCellOnOtherLevel(hauler, mech, MechanoidWorkType.Work, nearCharger: false));
}
