using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.MechUpgradesUi;

public class JobGiver_OptimizeUpgrades : ThinkNode_JobGiver
{

    public override float GetPriority(Pawn pawn) => 5.9f;

    protected override Job TryGiveJob(Pawn pawn)
    {
        if (Find.TickManager.TicksGame < pawn.mindState.nextApparelOptimizeTick)
            return null;

        var upgraders = pawn.Map.listerBuildings
            .AllColonistBuildingsOfType<Building_AutoMechUpgrader>()
            .Where(b => b.CanAcceptPawn(pawn))
            .ToList();

        if (upgraders.Count == 0)
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + Rand.Range(6000, 9000);
            return null;
        }

        var mechComp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        var currentUpgrades = mechComp.upgrades;
        var wantedUpgrades = mechComp.WantedUpgrades();
        var wantedDowngrades = mechComp.UnwantedUpgrades();

        if (wantedUpgrades.Empty() && wantedDowngrades.Empty())
        {
            pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + Rand.Range(6000, 9000);
            return null;
        }

        var operationsByUpgrader = upgraders.ToDictionary(
            upgrader => upgrader,
            upgrader => upgrader.WantedOperations(pawn, currentUpgrades, wantedUpgrades, wantedDowngrades)
        );

        if (operationsByUpgrader.TryMaxBy(e => e.Value.Count, out var mostOperations))
        {
            if (mostOperations.Value.Count > 0)
            {
                return JobMaker.MakeJob(JobDefOf.EnterBuilding, (LocalTargetInfo) (Thing) mostOperations.Key);
            }
        }

        pawn.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + Rand.Range(6000, 9000);
        return null;
    }
}