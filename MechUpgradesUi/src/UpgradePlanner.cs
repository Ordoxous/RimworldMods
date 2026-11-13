using System.Collections.Generic;
using System.Linq;
using MU;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>An upgrade a plan passed over, and why.</summary>
public readonly struct SkippedUpgrade(MechUpgrade upgrade, string reason)
{
    public readonly MechUpgrade Upgrade = upgrade;

    public readonly string Reason = reason;
}

/// <summary>
/// The one place that decides which upgrades a mech gets: the auto upgrader's operations, the policy preview and the
/// Mechs tab's status all go through it, so they agree.
/// </summary>
public static class UpgradePlanner
{
    private static readonly Dictionary<MechUpgradeDef, MechUpgrade> Samples = new();

    /// <summary>
    /// Picks the upgrades to add, greedily in defName order. Each is checked against the free points and, by MU's
    /// <c>CanAdd</c>, against everything the mech will end up with, so duplicates and shared exclusion tags are refused the
    /// way MU refuses them. A null <paramref name="freePoints"/> means no point limit.
    /// </summary>
    public static List<MechUpgrade> PickAdditions(
        ThingDef mechDef,
        int? freePoints,
        List<MechUpgrade> upgradesToKeep,
        IEnumerable<MechUpgrade> candidates,
        List<SkippedUpgrade> skipped = null
    )
    {
        var resulting = new List<MechUpgrade>(upgradesToKeep);
        var added = new List<MechUpgrade>();

        foreach (var upgrade in candidates.OrderBy(u => u.def.defName))
        {
            var points = upgrade.def.upgradePoints;
            if (freePoints < points)
            {
                skipped?.Add(new SkippedUpgrade(upgrade, "MU_Upgrades_Ui_Skip_NoPoints".Translate()));
                continue;
            }

            var canAdd = upgrade.def.CanAdd(mechDef, resulting);
            if (!canAdd.Accepted)
            {
                skipped?.Add(new SkippedUpgrade(upgrade, canAdd.Reason));
                continue;
            }

            added.Add(upgrade);
            resulting.Add(upgrade);
            freePoints -= points;
        }

        return added;
    }

    /// <summary>
    /// A stand-in upgrade for a def, for planning with upgrades that don't exist yet. Made once per def and never given to
    /// a mech.
    /// </summary>
    public static MechUpgrade Sample(MechUpgradeDef def)
    {
        if (!Samples.TryGetValue(def, out var sample))
        {
            sample = MechUpgradeUtility.MakeUpgrade(def);
            Samples[def] = sample;
        }

        return sample;
    }

    /// <summary>The upgrade an item installs, or null if it isn't an upgrade item.</summary>
    public static MechUpgradeDef UpgradeDefOf(ThingDef item) =>
        item.GetCompProperties<CompProperties_MechUpgrade>()?.upgradeDef;
}
