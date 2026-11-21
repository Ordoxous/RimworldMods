using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors: carries a mech to a free charger on another level, when vanilla's HaulMechsToCharger would take it
/// to a charger but finds none free on its level. Scanned before both zone tweaks, and unlike them it also takes a mech
/// parked in a zone, so the mech moves on once a charger frees up upstairs. The MultiFloors parts are in
/// <see cref="MultiFloorsShutdown"/>.
/// </summary>
public class WorkGiver_HaulMechToChargerOnOtherLevel : WorkGiver_Scanner
{
    public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

    public override IEnumerable<Thing> PotentialWorkThingsGlobal(
        Pawn pawn
    ) => pawn.Map.mapPawns.SpawnedColonyMechs.Where(WantsCharger);

    public override bool ShouldSkip(
        Pawn pawn,
        bool forced = false
    ) => !TweaksSettings.ChargersAcrossLevels || !PotentialWorkThingsGlobal(pawn).Any();

    public override bool HasJobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    ) => target is Pawn mech
         && mech != pawn
         && WantsCharger(mech)
         && !mech.IsForbidden(pawn)
         && pawn.CanReserve(mech, ignoreOtherReservations: forced)
         // Vanilla's own check: with a free charger on this level, HaulMechsToCharger takes it there
         && RimWorld.JobGiver_GetEnergy_Charger.GetClosestCharger(mech, pawn, forced) == null
         && MultiFloorsShutdown.HasChargerOnOtherLevel(pawn, mech);

    public override Job JobOnThing(
        Pawn pawn,
        Thing target,
        bool forced = false
    ) => MultiFloorsShutdown.TryMakeChargerCarryJob(pawn, (Pawn)target);

    // The mechs vanilla's WorkGiver_HaulMechToCharger.HasJobOnThing would carry, short of the charger: downed or in
    // low-energy self-shutdown, not in a self-shutdown control group, below the recharge limit and not charging
    public static bool WantsCharger(
        Pawn mech
    ) => mech.Spawned
         && mech.IsColonyMech
         && mech.needs?.energy is { } energy
         && (mech.Downed || energy.IsLowEnergySelfShutdown)
         && mech.GetMechControlGroup()?.WorkMode != MechWorkModeDefOf.SelfShutdown
         && energy.CurLevel < JobGiver_GetEnergy.GetMaxRechargeLimit(mech)
         && mech.CurJobDef != JobDefOf.MechCharge;
}

/// <summary>
/// Vanilla's JobDriver_HaulMechToCharger, for a hauler that may already be carrying the mech: one arriving from another
/// level with it. Vanilla's always walks to the mech first, which fails when it's carried.
/// </summary>
public class JobDriver_DeliverMechToCharger : JobDriver
{
    private const TargetIndex MechIndex = TargetIndex.A;
    private const TargetIndex ChargerIndex = TargetIndex.B;
    private const TargetIndex ChargerCellIndex = TargetIndex.C;

    private Pawn Mech => (Pawn)job.GetTarget(MechIndex).Thing;

    public override bool TryMakePreToilReservations(
        bool errorOnFailed
    ) => pawn.Reserve(job.GetTarget(ChargerIndex), job, errorOnFailed: errorOnFailed)
         && (pawn.IsCarryingThing(Mech) || pawn.Reserve(job.GetTarget(MechIndex), job, errorOnFailed: errorOnFailed));

    public override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(MechIndex);
        this.FailOnDespawnedNullOrForbidden(ChargerIndex);
        var carrying = Toils_General.Label();
        yield return Toils_Jump.JumpIf(carrying, () => pawn.IsCarryingThing(Mech));
        yield return Toils_Goto.GotoThing(MechIndex, PathEndMode.ClosestTouch).FailOnSomeonePhysicallyInteracting(MechIndex);
        yield return Toils_Haul.StartCarryThing(MechIndex);
        yield return carrying;
        yield return Toils_Haul.CarryHauledThingToCell(ChargerCellIndex, PathEndMode.OnCell);
        yield return Toils_Haul.PlaceHauledThingInCell(ChargerCellIndex, null, false);
        yield return Toils_General.Do(() =>
        {
            pawn.Map.reservationManager.Release(job.targetB, pawn, job);
            Mech.jobs.StartJob(JobMaker.MakeJob(JobDefOf.MechCharge, job.targetB.Thing), JobCondition.InterruptForced);
        });
    }
}
