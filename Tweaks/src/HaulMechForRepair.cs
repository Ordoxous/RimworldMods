using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using WVC_WorkModes;

namespace Ordo.Tweaks;

public class JobDriver_HaulMechForRepair : JobDriver
{
    private const TargetIndex MechIndex = TargetIndex.A;
    private const TargetIndex ZoneCellIndex = TargetIndex.B;

    public override bool TryMakePreToilReservations(
        bool errorOnFailed
    )
    {
        return pawn.Reserve(job.GetTarget(ZoneCellIndex), job, errorOnFailed: errorOnFailed)
               && pawn.Reserve(job.GetTarget(MechIndex), job, errorOnFailed: errorOnFailed);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(MechIndex);
        this.FailOnDespawnedNullOrForbidden(ZoneCellIndex);
        yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(MechIndex);
        yield return Toils_Haul.StartCarryThing(MechIndex);
        yield return Toils_Haul.CarryHauledThingToCell(ZoneCellIndex, PathEndMode.OnCell);
        yield return Toils_Haul.PlaceHauledThingInCell(ZoneCellIndex, null, false);
    }
}

public class WorkGiver_HaulMechForRepair : WorkGiver_Scanner
{

    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(
        Pawn pawn
    ) => pawn.Map.mapPawns.SpawnedColonyMechs;

    public override bool HasJobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    )
    {
        var targetPawn = (Pawn) target;

        if (
            !targetPawn.IsColonyMech
            || !targetPawn.Downed
            || targetPawn.CurJobDef == JobDefOf.MechCharge
            || targetPawn.IsForbidden(pawn)
            || !pawn.CanReserve((LocalTargetInfo) targetPawn, ignoreOtherReservations: forced)
            || ShutdownUtility.MechInShutdownZone(targetPawn, targetPawn.Position, MechanoidWorkType.Work)
        )
        {
            return false;
        }

        var allZones = targetPawn.Map?.zoneManager?.AllZones;

        if (allZones == null)
        {
            return false;
        }

        return ShutdownUtility.TryFindRandomMechShutdownZone(allZones, targetPawn, targetPawn.Map, MechanoidWorkType.Work, out var closestSpot);
    }

    public override Job JobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    )
    {
        var targetPawn = (Pawn) target;
        
        var allZones = targetPawn.Map?.zoneManager?.AllZones;

        if (ShutdownUtility.TryFindRandomMechShutdownZone(allZones, targetPawn, targetPawn.Map, MechanoidWorkType.Work, out var targetSpot))
        {
            var job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HaulMechForRepair"), (LocalTargetInfo) targetPawn, targetSpot);
            job.count = 1;
            return job;
        }

        return null;

    }
}