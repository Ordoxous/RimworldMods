using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using Verse;

namespace MechUi;

public class Building_AutoMechUpgrader : Building_MechUpgrader
{
    public List<MechUpgradeOperation> WantedOperations(
        List<MechUpgrade> currentUpgrades,
        List<MechUpgradeDef> wantedUpgrades,
        List<MechUpgrade> wantedDowngrades
    )
    {
        var upgradesToKeep = currentUpgrades.Where(u => !wantedDowngrades.Contains(u)).ToList();

        var compAffectedByFacilities = this.TryGetComp<CompAffectedByFacilities>();
        var availableUpgrades = compAffectedByFacilities.LinkedFacilitiesListForReading
            .Where(f => compAffectedByFacilities.IsFacilityActive(f))
            .Select(s => s.TryGetComp<CompUpgradesStorage>())
            .SelectMany(s => s.Upgrades)
            .GroupBy(p => p.def.exclusionTags.First())
            .Select(g => g.MinBy(u => u.def.defName))
            .Where(u => wantedUpgrades.Contains(u.def))
            .ToList();

        return MechUpgradeUtility.GetOperationsFromLists(
            currentUpgrades,
            availableUpgrades.Concat(upgradesToKeep).ToList()
        ).ToList();
    }

    public override void TryAcceptPawn(Pawn pawn)
    {
        if (!CanAcceptPawn(pawn))
            return;

        var mechComp = pawn.GetComp<CompUpgradableMechWithPolicy>();

        var wantedOperations = WantedOperations(
            mechComp.upgrades,
            mechComp.WantedUpgrades(),
            mechComp.WantedDowngrades()
        );

        if (wantedOperations.NullOrEmpty())
        {
            base.TryAcceptPawn(pawn);
            return;
        }

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
                        var upgrade = compUpgradesStorage.innerContainer.FirstOrDefault(t2 => t2.TryGetComp<CompMechUpgrade>()?.upgrade == o.upgrade);
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