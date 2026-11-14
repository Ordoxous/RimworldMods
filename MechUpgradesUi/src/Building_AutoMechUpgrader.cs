using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.MechUpgradesUi;

public class Building_AutoMechUpgrader : Building_MechUpgrader
{
    /// <summary>What the policy would have this upgrader do to the mech, from its stock: the plan as MU operations.</summary>
    public List<MechUpgradeOperation> WantedOperations(
        Pawn pawn,
        List<MechUpgrade> currentUpgrades,
        UpgradePolicy policy
    )
    {
        // Not using MechUpgradeUtility.GetOperationsFromLists: it overwrites the given upgrader's saved upgrades list
        var plan = UpgradePlanner.Plan(pawn.def, MechUpgradeUtility.MaxUpgradePoints(pawn), currentUpgrades, policy, StockedUpgrades());
        var operations = plan.Add
            .Select(u => new MechUpgradeOperation(u, UpgradeOperationType.Add))
            .ToList();

        // MU runs operations from the end of the list. Removals go last so they run first, otherwise
        // removing an upgrade would also take a shared ability from the upgrade replacing it
        operations.AddRange(plan.Remove.Select(u => new MechUpgradeOperation(u, UpgradeOperationType.Remove)));
        return operations;
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

    /// <summary>
    /// The auto upgraders on the mech's map that it could go to by itself: powered, not forbidden and reachable. Another mech
    /// being inside doesn't count against one, since it will be free again. Why each of the others can't be used goes in
    /// <paramref name="reasons"/>. The job giver and the status shown to the player both start from this, so they agree.
    /// </summary>
    public static List<Building_AutoMechUpgrader> UsableBy(Pawn mech, List<string> reasons = null)
    {
        var usable = new List<Building_AutoMechUpgrader>();
        var map = mech.MapHeld;
        if (map == null)
            return usable;

        foreach (var upgrader in map.listerBuildings.AllColonistBuildingsOfType<Building_AutoMechUpgrader>())
        {
            var reason = upgrader.UnusableReason(mech);
            if (reason == null)
                usable.Add(upgrader);
            else
                reasons?.Add(reason);
        }

        return usable;
    }

    /// <summary>Why the mech can't use this upgrader on its own trips, or null if it can.</summary>
    public string UnusableReason(Pawn mech)
    {
        if (!PowerOn)
            return "MU_Upgrades_Ui_Upgrader_NoPower".Translate();
        // Vanilla doesn't hold controlled mechs to forbidding, so this only stops a mech that cares
        if (this.IsForbidden(mech))
            return "MU_Upgrades_Ui_Upgrader_Forbidden".Translate();
        // A mech inside an upgrader isn't spawned, so there's no path to ask about
        if (mech.Spawned && !mech.CanReach(this, PathEndMode.InteractionCell, Danger.Deadly))
            return "MU_Upgrades_Ui_Upgrader_Unreachable".Translate();
        return null;
    }

    public override string GetInspectString()
    {
        var lines = new List<string>();
        var text = base.GetInspectString();
        if (!text.NullOrEmpty())
            lines.Add(text);

        var storages = ActiveStorages().ToList();
        lines.Add(storages.Count == 0
            ? "MU_Upgrades_Ui_Upgrader_NoStorage".Translate().Resolve().Colorize(ColorLibrary.RedReadable)
            : "MU_Upgrades_Ui_Upgrader_Stock".Translate(storages.Count, storages.Sum(s => s.Upgrades.Count)).Resolve());

        if (Spawned)
        {
            var mechs = CompUpgradableMechWithPolicy.PlayerMechs(Map).ToList();
            var onTheWay = mechs
                .Where(c => c.Mech.CurJobDef == JobDefOf.EnterBuilding && c.Mech.CurJob.targetA.Thing == this)
                .ToList();
            var waiting = mechs
                .Where(c => !onTheWay.Contains(c) && c.EffectiveUpgradePolicy != null)
                .Where(c => UpgradeStatus.For(c) is { HasChanges: true } status && status.Upgrader == this)
                .ToList();

            if (onTheWay.Count > 0)
                lines.Add("MU_Upgrades_Ui_Upgrader_OnTheWay".Translate(onTheWay.Select(c => c.Mech.LabelShort).ToCommaList()));
            if (waiting.Count > 0)
                lines.Add("MU_Upgrades_Ui_Upgrader_Waiting".Translate(waiting.Select(c => c.Mech.LabelShort).ToCommaList()));
        }

        return string.Join("\n", lines);
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
            var wantedOperations = WantedOperations(pawn, mechComp.upgrades, mechComp.EffectiveUpgradePolicy);

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
