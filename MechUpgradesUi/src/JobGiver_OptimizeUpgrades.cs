using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.MechUpgradesUi;

public class JobGiver_OptimizeUpgrades : ThinkNode_JobGiver
{

    public override float GetPriority(Pawn pawn) => 5.9f;

    public override Job TryGiveJob(Pawn pawn)
    {
        if (Find.TickManager.TicksGame < pawn.mindState.nextApparelOptimizeTick)
            return null;

        var mechComp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        if (mechComp?.EffectiveUpgradePolicy == null)
            return null;

        // Set up front, so a trip that fails partway can't be retried in a tight loop
        var intervalTicks = MechUpgradesUiMod.Settings.PolicyCheckIntervalHours * GenDate.TicksPerHour;
        pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + Rand.Range((int)(intervalTicks * 0.8f), (int)(intervalTicks * 1.2f));

        // The status shown to the player starts from UsableBy too. Being free right now only matters for an actual trip
        // With MultiFloors an upgrader may be on another level, where the mech can't reserve it from here
        var upgraders = Building_AutoMechUpgrader.UsableBy(pawn)
            .Where(b => b.CanAcceptPawn(pawn)
                && (b.Map == pawn.Map ? pawn.CanReserve(b) : !b.Map.reservationManager.IsReservedByAnyoneOf(b, pawn.Faction)))
            .ToList();

        if (upgraders.Count == 0)
            return null;

        // The most operations wins; on a tie, the mech's own level, since UsableBy lists it first and the first maximum is kept
        var policy = mechComp.EffectiveUpgradePolicy;
        Building_AutoMechUpgrader best = null;
        var mostOperations = 0;
        foreach (var upgrader in upgraders)
        {
            var count = upgrader.WantedOperations(pawn, mechComp.upgrades, policy).Count;
            if (count > mostOperations)
            {
                best = upgrader;
                mostOperations = count;
            }
        }

        if (best == null)
            return null;

        var enter = JobMaker.MakeJob(JobDefOf.EnterBuilding, (LocalTargetInfo)(Thing)best);
        return best.Map == pawn.Map ? enter : MultiFloorsLevels.TripTo(pawn, best, enter);
    }
}
