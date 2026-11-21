using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using WVC_WorkModes;

namespace Ordo.Tweaks;

/// <summary>
/// Carries a colony mech that has shut itself down for lack of power to a WVC shutdown zone, when vanilla's
/// HaulMechsToCharger has no charger to take it to. The cell picked is the zone cell nearest a charger the mech can use,
/// so it's closer when one frees up; vanilla then carries it on from there.
/// </summary>
public static class UnpoweredMechHauling
{
    // The same mechs vanilla's WorkGiver_HaulMechToCharger would carry, short of the charger
    public static bool NeedsCharger(
        Pawn mech
    )
    {
        if (!mech.Spawned || !mech.IsColonyMech || mech.Downed || mech.needs?.energy is not { } energy)
        {
            return false;
        }

        return energy.IsLowEnergySelfShutdown
               && mech.GetMechControlGroup()?.WorkMode != MechWorkModeDefOf.SelfShutdown
               && energy.CurLevel < JobGiver_GetEnergy.GetMaxRechargeLimit(mech)
               && mech.CurJobDef != JobDefOf.MechCharge;
    }

    // Whether vanilla's WorkGiver_HaulMechToCharger would carry this downed mech to a charger: it takes a downed mech when it's
    // below its recharge limit, not in a self-shutdown control group and not already charging, and a charger is free for it.
    // A downed mech at its limit isn't vanilla's, so the downed-mech tweak still takes it.
    public static bool VanillaTakesToCharger(
        Pawn mech,
        Pawn hauler,
        bool forced
    )
    {
        if (mech.needs?.energy is not { } energy
            || mech.GetMechControlGroup()?.WorkMode == MechWorkModeDefOf.SelfShutdown
            || energy.CurLevel >= JobGiver_GetEnergy.GetMaxRechargeLimit(mech))
        {
            return false;
        }

        return RimWorld.JobGiver_GetEnergy_Charger.GetClosestCharger(mech, hauler, forced) != null;
    }

    // The zone cell on the hauler's map nearest a charger the mech is compatible with (taken or not), or a random one
    // when there's none
    public static bool TryFindCell(
        Pawn hauler,
        Pawn mech,
        out IntVec3 cell
    ) => ShutdownCells.TryFindCell(hauler, mech, MechanoidWorkType.Work, nearCharger: true, out cell);

    // The job taking the mech to a zone: on the hauler's level, else (with MultiFloors) on another one
    public static Job MakeJob(
        Pawn hauler,
        Pawn mech
    )
    {
        if (TryFindCell(hauler, mech, out var cell))
        {
            var job = JobMaker.MakeJob(TweaksDefOf.Ordo_HaulMechToShutdownZone, mech, cell);
            job.count = 1;
            return job;
        }

        return TweaksSettings.ShutdownZonesAcrossLevels
            ? MultiFloorsShutdown.TryMakeCarryJob(hauler, mech, MechanoidWorkType.Work, nearCharger: true)
            : null;
    }

    public static bool HasCell(
        Pawn hauler,
        Pawn mech
    ) => TryFindCell(hauler, mech, out _)
         || (TweaksSettings.ShutdownZonesAcrossLevels
             && MultiFloorsShutdown.HasCellOnOtherLevel(hauler, mech, MechanoidWorkType.Work, nearCharger: true));
}

public class WorkGiver_HaulMechToShutdownZone : WorkGiver_Scanner
{
    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(
        Pawn pawn
    ) => pawn.Map.mapPawns.SpawnedColonyMechs.Where(UnpoweredMechHauling.NeedsCharger);

    public override bool ShouldSkip(
        Pawn pawn,
        bool forced = false
    ) => !TweaksSettings.HaulUnpoweredMechs || !PotentialWorkThingsGlobal(pawn).Any();

    public override bool HasJobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    )
    {
        if (target is not Pawn mech
            || mech == pawn
            || !UnpoweredMechHauling.NeedsCharger(mech)
            || mech.IsForbidden(pawn)
            || !pawn.CanReserve(mech, ignoreOtherReservations: forced)
            || ShutdownUtility.MechInShutdownZone(mech, mech.Position, MechanoidWorkType.Work)
            // Vanilla's own check: when it finds a charger, HaulMechsToCharger takes the mech there instead
            || RimWorld.JobGiver_GetEnergy_Charger.GetClosestCharger(mech, pawn, forced) != null)
        {
            return false;
        }

        return UnpoweredMechHauling.HasCell(pawn, mech);
    }

    public override Job JobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    ) => UnpoweredMechHauling.MakeJob(pawn, (Pawn)target);
}

public class JobDriver_HaulMechToShutdownZone : JobDriver
{
    private const TargetIndex MechIndex = TargetIndex.A;
    private const TargetIndex CellIndex = TargetIndex.B;

    private Pawn Mech => (Pawn)job.GetTarget(MechIndex).Thing;

    // A hauler arriving from another MultiFloors level starts this job already carrying the mech
    public override bool TryMakePreToilReservations(
        bool errorOnFailed
    )
    {
        return pawn.Reserve(job.GetTarget(CellIndex), job, errorOnFailed: errorOnFailed)
               && (pawn.IsCarryingThing(Mech) || pawn.Reserve(job.GetTarget(MechIndex), job, errorOnFailed: errorOnFailed));
    }

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(MechIndex);
        var carrying = Toils_General.Label();
        yield return Toils_Jump.JumpIf(carrying, () => pawn.IsCarryingThing(Mech));
        yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.ClosestTouch)
            .FailOnSomeonePhysicallyInteracting(MechIndex)
            .FailOn(() => !UnpoweredMechHauling.NeedsCharger(Mech));
        yield return Toils_Haul.StartCarryThing(MechIndex);
        yield return carrying;
        yield return Toils_Haul.CarryHauledThingToCell(CellIndex, PathEndMode.OnCell);
        yield return Toils_Haul.PlaceHauledThingInCell(CellIndex, null, false);
        // Shut it down where it was put, as vanilla starts the charge job after carrying a mech to a charger. Otherwise
        // the energy need would pick its own nearby spot, which may be outside the zone.
        yield return Toils_General.Do(() =>
        {
            // The mech's own job reserves the cell, so the hauler lets it go first, as vanilla does with the charger
            pawn.Map.reservationManager.Release(job.targetB, pawn, job);
            var mech = Mech;
            if (mech is { Spawned: true, Downed: false, jobs: not null } && mech.needs?.energy?.selfShutdown == true)
            {
                mech.jobs.StartJob(JobMaker.MakeJob(JobDefOf.SelfShutdown, mech.Position), JobCondition.InterruptForced,
                    resumeCurJobAfterwards: false, cancelBusyStances: true, tag: JobTag.SatisfyingNeeds);
            }
        });
    }
}
