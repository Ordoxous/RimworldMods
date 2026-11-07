using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

public class Building_AutoMechUpgrader : Building_MechUpgrader
{
    public List<MechUpgradeOperation> WantedOperations(
        Pawn pawn,
        List<MechUpgrade> currentUpgrades,
        List<MechUpgrade> upgradesToKeep,
        List<MechUpgradeDef> wantedUpgrades
    )
    {
        var compAffectedByFacilities = this.TryGetComp<CompAffectedByFacilities>();
        var availableUpgrades = compAffectedByFacilities.LinkedFacilitiesListForReading
            .Where(compAffectedByFacilities.IsFacilityActive)
            .Select(s => s.TryGetComp<CompUpgradesStorage>())
            .SelectMany(s => s.Upgrades)
            .Where(u => wantedUpgrades.Contains(u.def))
            .OrderBy(u => u.def.defName);

        // Not using MechUpgradeUtility.GetOperationsFromLists: it overwrites the given upgrader's saved upgrades list
        var allowedOperations = currentUpgrades
            .Where(u => !upgradesToKeep.Contains(u))
            .Select(u => new MechUpgradeOperation(u, UpgradeOperationType.Remove))
            .ToList();

        var availablePoints = MechUpgradeUtility.MaxUpgradePoints(pawn) - MechUpgradeUtility.CurrentPoints(upgradesToKeep);

        // Check each addition against everything the mech will end up with, so duplicates and
        // conflicting exclusion tags between candidates are rejected the same way MU does
        var resultingUpgrades = new List<MechUpgrade>(upgradesToKeep);

        foreach (var upgrade in availableUpgrades)
        {
            var upgradePoints = upgrade.def.upgradePoints;
            if (availablePoints - upgradePoints < 0 || !upgrade.def.CanAdd(pawn.def, resultingUpgrades))
                continue;

            allowedOperations.Add(new MechUpgradeOperation(upgrade, UpgradeOperationType.Add));
            resultingUpgrades.Add(upgrade);
            availablePoints -= upgradePoints;
        }

        return allowedOperations;
    }

    public override void TryAcceptPawn(Pawn pawn)
    {
        if (!CanAcceptPawn(pawn))
            return;

        var mechComp = pawn.GetComp<CompUpgradableMechWithPolicy>();
        if (mechComp != null)
        {
            var upgradesToKeep = mechComp.UpgradesToKeep();
            var wantedOperations = WantedOperations(
                pawn,
                mechComp.upgrades,
                upgradesToKeep,
                mechComp.WantedUpgrades(upgradesToKeep)
            );

            if (wantedOperations.Count > 0)
            {
                ApplyOperations(pawn, wantedOperations);
                return;
            }
        }

        base.TryAcceptPawn(pawn);
    }

    private void ApplyOperations(
        Pawn pawn,
        List<MechUpgradeOperation> wantedOperations
    )
    {
        pawn.DeSpawnOrDeselect();
        innerContainer.TryAddOrTransfer(pawn);

        operations = wantedOperations;

        // Mirror MU's configure dialog, where upgrades holds the pending additions
        upgrades = operations
            .Where(o => o.type == UpgradeOperationType.Add)
            .Select(o => o.upgrade)
            .ToList();

        foreach (var pendingUpgrade in upgrades)
        {
            var comp = this.TryGetComp<CompAffectedByFacilities>();
            foreach (var linkedFacility in comp.LinkedFacilitiesListForReading)
            {
                var compUpgradesStorage = linkedFacility.TryGetComp<CompUpgradesStorage>();
                var storageUpgrades = compUpgradesStorage.Upgrades;
                if (comp.IsFacilityActive(linkedFacility) && storageUpgrades.Contains(pendingUpgrade))
                {
                    var upgrade = compUpgradesStorage.innerContainer.FirstOrDefault(innerThing => innerThing.TryGetComp<CompMechUpgrade>()?.upgrade == pendingUpgrade);
                    if (upgrade != null)
                    {
                        compUpgradesStorage.innerContainer.Remove(upgrade);
                        break;
                    }
                }
            }
        }

        fabricationTicksLeft = 2500 * operations.Last().upgrade.def.upgradePoints;
    }
}
