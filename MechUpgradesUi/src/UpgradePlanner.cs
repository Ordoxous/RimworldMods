using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MU;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>An upgrade a plan passed over, and why.</summary>
public readonly struct SkippedUpgrade(MechUpgrade upgrade, string reason)
{
    public readonly MechUpgrade Upgrade = upgrade;

    public readonly string Reason = reason;
}

/// <summary>What a mech's next trip to an auto upgrader would do.</summary>
public sealed class UpgradePlan
{
    /// <summary>Installed upgrades that stay.</summary>
    public List<MechUpgrade> Keep = [];

    /// <summary>Upgrades fitted from stock, in the order they were picked.</summary>
    public List<MechUpgrade> Add = [];

    /// <summary>Installed upgrades taken off: the ones the policy doesn't allow, and the ones in <see cref="Replaced"/>.</summary>
    public List<MechUpgrade> Remove = [];

    /// <summary>Allowed installed upgrades taken off to make room for higher-ranked ones.</summary>
    public List<MechUpgrade> Replaced = [];

    /// <summary>Stock the plan passed over, and why.</summary>
    public List<SkippedUpgrade> Skipped = [];
}

/// <summary>
/// The one place that decides which upgrades a mech gets: the auto upgrader's operations, the policy preview and the
/// Mechs tab's status all go through it, so they agree.
/// </summary>
public static class UpgradePlanner
{
    private static readonly Dictionary<MechUpgradeDef, MechUpgrade> Samples = new();

    /// <summary>
    /// Plans a trip for a mech with <paramref name="installed"/> upgrades to an upgrader holding <paramref name="stock"/>.
    /// Upgrades the policy doesn't allow come off. The policy's allowed stock is picked greedily in its rank order (unranked
    /// upgrades by defName), each checked against the free points and, by MU's <c>CanAdd</c>, against everything the mech
    /// will end up with, so duplicates and shared exclusion tags are refused the way MU refuses them. A null
    /// <paramref name="maxPoints"/> means no point limit.
    /// <para>
    /// With the policy's <see cref="UpgradePolicy.ReplaceLowerRanked"/> on, installed upgrades take their place in the same
    /// ranking, so a higher-ranked upgrade in stock can push one out. One is only pushed out once something ranked above it
    /// has been added: a mech that's simply over its points loses nothing. The result doesn't change if the plan is carried
    /// out and made again, since what's removed is ranked below what replaced it.
    /// </para>
    /// </summary>
    public static UpgradePlan Plan(
        ThingDef mechDef,
        int? maxPoints,
        List<MechUpgrade> installed,
        UpgradePolicy policy,
        IEnumerable<MechUpgrade> stock
    )
    {
        var plan = new UpgradePlan();
        var allowedDefs = policy.AllowedUpgradeDefs.ToHashSet();
        var allowedInstalled = installed.Where(u => allowedDefs.Contains(u.def)).ToList();
        var candidates = stock.Where(u => allowedDefs.Contains(u.def)).ToList();

        if (!policy.ReplaceLowerRanked)
        {
            plan.Keep = allowedInstalled;
            plan.Add = PickAdditions(mechDef, maxPoints - MechUpgradeUtility.CurrentPoints(plan.Keep), plan.Keep, candidates, plan.Skipped, policy);
        }
        else
        {
            // Installed and stocked upgrades in one ranking. For the same upgrade, the installed one comes first
            var ordered = allowedInstalled.Select(u => (upgrade: u, isInstalled: true))
                .Concat(candidates.Select(u => (upgrade: u, isInstalled: false)))
                .OrderBy(x => policy.RankOf(x.upgrade.def))
                .ThenBy(x => x.upgrade.def.defName)
                .ThenBy(x => x.isInstalled ? 0 : 1);

            var resulting = new List<MechUpgrade>();
            var free = maxPoints;
            foreach (var (upgrade, isInstalled) in ordered)
            {
                var reason = WhyNot(mechDef, free, resulting, upgrade);

                // Nothing outranks it yet, so it stays whatever the points say
                if (reason != null && isInstalled && plan.Add.Count == 0)
                    reason = null;

                if (reason == null)
                {
                    resulting.Add(upgrade);
                    free -= upgrade.def.upgradePoints;
                    (isInstalled ? plan.Keep : plan.Add).Add(upgrade);
                }
                else if (isInstalled)
                    plan.Replaced.Add(upgrade);
                else
                    plan.Skipped.Add(new SkippedUpgrade(upgrade, reason));
            }
        }

        plan.Remove = installed.Where(u => !plan.Keep.Contains(u)).ToList();
        return plan;
    }

    /// <summary>
    /// Picks the upgrades to add to <paramref name="upgradesToKeep"/>, greedily in the policy's rank order (defName order
    /// without one), within the free points and passing MU's <c>CanAdd</c> against everything the mech will end up with. A
    /// null <paramref name="freePoints"/> means no point limit.
    /// </summary>
    public static List<MechUpgrade> PickAdditions(
        ThingDef mechDef,
        int? freePoints,
        List<MechUpgrade> upgradesToKeep,
        IEnumerable<MechUpgrade> candidates,
        List<SkippedUpgrade> skipped = null,
        [CanBeNull] UpgradePolicy policy = null
    )
    {
        var resulting = new List<MechUpgrade>(upgradesToKeep);
        var added = new List<MechUpgrade>();

        var ordered = policy == null
            ? candidates.OrderBy(u => u.def.defName)
            : candidates.OrderBy(u => policy.RankOf(u.def)).ThenBy(u => u.def.defName);
        foreach (var upgrade in ordered)
        {
            var reason = WhyNot(mechDef, freePoints, resulting, upgrade);
            if (reason != null)
            {
                skipped?.Add(new SkippedUpgrade(upgrade, reason));
                continue;
            }

            added.Add(upgrade);
            resulting.Add(upgrade);
            freePoints -= upgrade.def.upgradePoints;
        }

        return added;
    }

    // Why an upgrade can't join the others, or null if it can
    [CanBeNull]
    private static string WhyNot(ThingDef mechDef, int? freePoints, List<MechUpgrade> resulting, MechUpgrade upgrade)
    {
        if (freePoints < upgrade.def.upgradePoints)
            return "MU_Upgrades_Ui_Skip_NoPoints".Translate();

        var canAdd = upgrade.def.CanAdd(mechDef, resulting);
        return canAdd.Accepted ? null : canAdd.Reason;
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
