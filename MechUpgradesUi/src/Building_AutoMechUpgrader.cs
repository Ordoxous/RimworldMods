using System;
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
        List<MechUpgradeDef> wantedUpgrades,
        List<MechUpgrade> unwantedUpgrades
    )
    {
        var upgradesToKeep = currentUpgrades.Where(u => !unwantedUpgrades.Contains(u)).ToList();

        var compAffectedByFacilities = this.TryGetComp<CompAffectedByFacilities>();
        var availableUpgrades = compAffectedByFacilities.LinkedFacilitiesListForReading
            .Where(f => compAffectedByFacilities.IsFacilityActive(f))
            .Select(s => s.TryGetComp<CompUpgradesStorage>())
            .SelectMany(s => s.Upgrades)
            .GroupBy(p => p.def.exclusionTags.First())
            .Select(g => g.MinBy(u => u.def.defName))
            .Where(u => wantedUpgrades.Contains(u.def))
            .ToList();
        
        var wantedOperations = MechUpgradeUtility.GetOperationsFromLists(
            currentUpgrades,
            availableUpgrades.Concat(upgradesToKeep).ToList()
        )
            .GroupBy(o => o.type)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (!wantedOperations.Any())
            return [];
        
        var allowedOperations = new List<MechUpgradeOperation>();

        var maxPoints = MechUpgradeUtility.MaxUpgradePoints(pawn);
        var currentPoints = MechUpgradeUtility.CurrentPoints(pawn);

        var availablePoints = maxPoints - currentPoints;

        if (wantedOperations.TryGetValue(UpgradeOperationType.Remove, out var removals))
        {
            foreach (var operation in removals)
            {
                allowedOperations.Add(operation);
                availablePoints += operation.upgrade.def.upgradePoints;
            }
        }

        if (wantedOperations.TryGetValue(UpgradeOperationType.Add, out var additions))
        {
            foreach (var operation in additions)
            {
                var operationPoints = operation.upgrade.def.upgradePoints;
                if (availablePoints - operationPoints >= 0)
                {
                    allowedOperations.Add(operation);
                    availablePoints -= operationPoints;
                }
            }
        }

        return allowedOperations;
    }

    public override void TryAcceptPawn(Pawn pawn)
    {
        if (!CanAcceptPawn(pawn))
            return;

        var mechComp = pawn.GetComp<CompUpgradableMechWithPolicy>();
        // (d.upgradePoints <= MechUpgradeUtility.MaxUpgradePoints(Mech) - MechUpgradeUtility.CurrentPoints(Mech))
        var wantedOperations = WantedOperations(
            pawn,
            mechComp.upgrades,
            mechComp.WantedUpgrades(),
            mechComp.UnwantedUpgrades()
        );

        if (wantedOperations.NullOrEmpty())
        {
            base.TryAcceptPawn(pawn);
            return;
        }

        ApplyOperations(pawn, wantedOperations);
    }

    private void ApplyOperations(
        Pawn pawn,
        List<MechUpgradeOperation> wantedOperations
    )
    {
        pawn.DeSpawnOrDeselect();
        innerContainer.TryAddOrTransfer(pawn);
        
        operations = wantedOperations;
        if (!operations.NullOrEmpty())
        {
            foreach (var o in operations.Where(o => o.type == UpgradeOperationType.Add))
            {
                var comp = this.TryGetComp<CompAffectedByFacilities>();
                foreach (var linkedFacility in comp.LinkedFacilitiesListForReading)
                {
                    var compUpgradesStorage = linkedFacility.TryGetComp<CompUpgradesStorage>();
                    var storageUpgrades = compUpgradesStorage.Upgrades;
                    if (comp.IsFacilityActive(linkedFacility) && storageUpgrades.Contains(o.upgrade))
                    {
                        var upgrade = compUpgradesStorage.innerContainer.FirstOrDefault(innerThing => innerThing.TryGetComp<CompMechUpgrade>()?.upgrade == o.upgrade);
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
}