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
        // Not using MechUpgradeUtility.GetOperationsFromLists: it overwrites the given upgrader's saved upgrades list
        var allowedOperations = UpgradePlanner
            .PickAdditions(
                pawn.def,
                MechUpgradeUtility.MaxUpgradePoints(pawn) - MechUpgradeUtility.CurrentPoints(upgradesToKeep),
                upgradesToKeep,
                StockedUpgrades().Where(u => wantedUpgrades.Contains(u.def))
            )
            .Select(u => new MechUpgradeOperation(u, UpgradeOperationType.Add))
            .ToList();

        // MU runs operations from the end of the list. Removals go last so they run first, otherwise
        // removing an upgrade would also take a shared ability from the upgrade replacing it
        allowedOperations.AddRange(
            currentUpgrades
                .Where(u => !upgradesToKeep.Contains(u))
                .Select(u => new MechUpgradeOperation(u, UpgradeOperationType.Remove))
        );

        return allowedOperations;
    }

    /// <summary>The storages linked to this upgrader that are active, so their upgrades can be fitted.</summary>
    public IEnumerable<CompUpgradesStorage> ActiveStorages()
    {
        var compAffectedByFacilities = this.TryGetComp<CompAffectedByFacilities>();
        return compAffectedByFacilities.LinkedFacilitiesListForReading
            .Where(compAffectedByFacilities.IsFacilityActive)
            .Select(s => s.TryGetComp<CompUpgradesStorage>())
            .Where(s => s != null);
    }

    /// <summary>Every upgrade in this upgrader's active linked storages.</summary>
    public IEnumerable<MechUpgrade> StockedUpgrades() => ActiveStorages().SelectMany(s => s.Upgrades);

    public override string GetInspectString()
    {
        var storages = ActiveStorages().ToList();
        string line = "MU_Upgrades_Ui_Upgrader_Stock".Translate(storages.Count, storages.Sum(s => s.Upgrades.Count));
        var text = base.GetInspectString();
        return text.NullOrEmpty() ? line : $"{text}\n{line}";
    }

    public override void TryAcceptPawn(Pawn pawn)
    {
        if (!CanAcceptPawn(pawn))
            return;

        // Every player-issued insert goes through an ordered job, the think tree's trips don't
        var playerOrdered = pawn.CurJob is { playerForced: true };
        var showDialog = MechUpgradesUiMod.Settings.ShowDialogOnManualInsert && playerOrdered;

        var mechComp = pawn.GetComp<CompUpgradableMechWithPolicy>();
        if (!showDialog && mechComp?.EffectiveUpgradePolicy != null)
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

        // A trip of the mech's own has nothing left to do, for example because another mech took the stock first. Going in would
        // leave it waiting behind MU's dialog for someone to answer
        if (!playerOrdered)
            return;

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
