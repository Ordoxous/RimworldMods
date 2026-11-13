using System.Collections.Generic;
using System.Linq;
using System.Text;
using MU;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>What a mech's policy would change, for the Mechs tab.</summary>
public sealed class UpgradeStatus
{
    private const float CacheSeconds = 1f;

    private static readonly Dictionary<Pawn, (float time, UpgradeStatus status)> Cache = new();

    /// <summary>Installed upgrades the policy doesn't allow. They go on the next trip.</summary>
    public List<MechUpgrade> Removals = [];

    /// <summary>What the next trip would fit, from the best stocked auto upgrader on the mech's map.</summary>
    public List<MechUpgrade> Additions = [];

    /// <summary>Upgrades the policy would pick if they were in stock, and aren't.</summary>
    public List<MechUpgradeDef> Unstocked = [];

    public bool HasChanges => Removals.Count > 0 || Additions.Count > 0;

    /// <summary>The status, worked out at most once a second per mech, since the Mechs tab asks every frame.</summary>
    public static UpgradeStatus For(CompUpgradableMechWithPolicy comp)
    {
        var now = Time.realtimeSinceStartup;
        if (Cache.TryGetValue(comp.Mech, out var cached) && now - cached.time < CacheSeconds)
            return cached.status;

        if (Cache.Count > 500)
            Cache.Clear();

        var status = Compute(comp);
        Cache[comp.Mech] = (now, status);
        return status;
    }

    public static UpgradeStatus Compute(CompUpgradableMechWithPolicy comp)
    {
        var status = new UpgradeStatus();
        if (comp.EffectiveUpgradePolicy == null)
            return status;

        var mech = comp.Mech;
        var keep = comp.UpgradesToKeep();
        var wanted = comp.WantedUpgrades(keep);
        var freePoints = MechUpgradeUtility.MaxUpgradePoints(mech) - MechUpgradeUtility.CurrentPoints(keep);

        status.Removals = comp.upgrades.Where(u => !keep.Contains(u)).ToList();

        var map = mech.MapHeld;
        if (map != null)
        {
            status.Additions = map.listerBuildings
                .AllColonistBuildingsOfType<Building_AutoMechUpgrader>()
                .Select(b => UpgradePlanner.PickAdditions(mech.def, freePoints, keep, b.StockedUpgrades().Where(u => wanted.Contains(u.def))))
                .MaxByWithFallback(added => added.Count) ?? [];
        }

        status.Unstocked = UpgradePlanner
            .PickAdditions(mech.def, freePoints, keep, wanted.Select(UpgradePlanner.Sample))
            .Select(u => u.def)
            .Where(d => status.Additions.All(a => a.def != d))
            .ToList();

        return status;
    }

    /// <summary>The Mechs tab's tooltip for a mech's upgrades.</summary>
    public static string Tooltip(CompUpgradableMechWithPolicy comp)
    {
        var text = new StringBuilder();
        var installed = comp.upgrades.Select(u => u.LabelCap.ToString()).ToList();
        text.AppendLine("MU_Upgrades_Ui_Status_Installed".Translate(
            MechUpgradeUtility.CurrentPoints(comp.upgrades),
            MechUpgradeUtility.MaxUpgradePoints(comp.Mech)
        ));
        text.AppendLine(installed.Count > 0 ? installed.ToLineList("  - ") : "  " + "MU_Upgrades_Ui_Status_None".Translate());
        text.AppendLine();

        var policy = comp.EffectiveUpgradePolicy;
        if (policy == null)
        {
            text.AppendLine("MU_Upgrades_Ui_Status_Unmanaged".Translate());
        }
        else
        {
            text.AppendLine("MU_Upgrades_Ui_Status_Policy".Translate(policy.label));
            var status = For(comp);
            if (status.Removals.Count > 0)
                text.AppendLine("MU_Upgrades_Ui_Status_Removes".Translate(Labels(status.Removals.Select(u => u.def))));
            if (status.Additions.Count > 0)
                text.AppendLine("MU_Upgrades_Ui_Status_Adds".Translate(Labels(status.Additions.Select(u => u.def))));
            if (status.Unstocked.Count > 0)
                text.AppendLine("MU_Upgrades_Ui_Status_Unstocked".Translate(Labels(status.Unstocked)));
            if (!status.HasChanges && status.Unstocked.Count == 0)
                text.AppendLine("MU_Upgrades_Ui_Status_UpToDate".Translate());
        }

        text.AppendLine();
        text.Append("MU_Upgrades_Ui_Status_Click".Translate());
        return text.ToString();
    }

    private static string Labels(IEnumerable<MechUpgradeDef> defs) => defs.Select(d => d.label).ToCommaList();
}
