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
        pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + Rand.Range((int) (intervalTicks * 0.8f), (int) (intervalTicks * 1.2f));

        var upgraders = pawn.Map.listerBuildings
            .AllColonistBuildingsOfType<Building_AutoMechUpgrader>()
            .Where(b => b.CanAcceptPawn(pawn)
                        && !b.IsForbidden(pawn)
                        && pawn.CanReserveAndReach(b, PathEndMode.InteractionCell, Danger.Deadly))
            .ToList();

        if (upgraders.Count == 0)
            return null;

        var currentUpgrades = mechComp.upgrades;
        var upgradesToKeep = mechComp.UpgradesToKeep();
        var wantedUpgrades = mechComp.WantedUpgrades(upgradesToKeep);

        if (wantedUpgrades.Empty() && upgradesToKeep.Count == currentUpgrades.Count)
            return null;

        var operationsByUpgrader = upgraders.ToDictionary(
            upgrader => upgrader,
            upgrader => upgrader.WantedOperations(pawn, currentUpgrades, upgradesToKeep, wantedUpgrades)
        );

        if (operationsByUpgrader.TryMaxBy(e => e.Value.Count, out var mostOperations))
        {
            if (mostOperations.Value.Count > 0)
            {
                return JobMaker.MakeJob(JobDefOf.EnterBuilding, (LocalTargetInfo) (Thing) mostOperations.Key);
            }
        }

        return null;
    }
}