using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

public class UpgradePolicy : Policy
{
    public ThingFilter Filter = new();

    /// <summary>
    /// The player's ranking, highest first. Upgrades are picked in this order; ones not in it come after, in defName order.
    /// It can hold upgrades the policy no longer allows, so they keep their place if allowed again.
    /// </summary>
    public List<MechUpgradeDef> Ranking = [];

    /// <summary>
    /// When on, a mech has an installed upgrade removed to make room for a higher-ranked one that's in stock. When off, a
    /// mech keeps every allowed upgrade it has, and the ranking only decides what fills its free points.
    /// </summary>
    public bool ReplaceLowerRanked;

    public UpgradePolicy() {
    }

    public UpgradePolicy(int id, string label)
        : base(id, label)
    {
    }

    public override string LoadKey => nameof (UpgradePolicy);

    /// <summary>The upgrades this policy allows.</summary>
    public IEnumerable<MechUpgradeDef> AllowedUpgradeDefs =>
        Filter.AllowedThingDefs
            .Select(UpgradePlanner.UpgradeDefOf)
            .Where(d => d != null);

    /// <summary>Upgrades in the order this policy picks them: by the ranking, then the unranked ones by defName.</summary>
    public IEnumerable<MechUpgradeDef> InRankOrder(IEnumerable<MechUpgradeDef> defs) =>
        defs.OrderBy(RankOf).ThenBy(d => d.defName);

    /// <summary>Where an upgrade comes in the ranking. Every unranked upgrade shares the place after the last ranked one.</summary>
    public int RankOf(MechUpgradeDef def)
    {
        var index = Ranking.IndexOf(def);
        return index >= 0 ? index : Ranking.Count;
    }

    /// <summary>
    /// Moves one of <paramref name="shown"/> (upgrades listed in rank order) from one place in that list to another. The
    /// whole ranking is written out first, unranked upgrades included, so the ones not shown keep their places.
    /// </summary>
    public void Move(List<MechUpgradeDef> shown, int from, int to)
    {
        if (from < 0 || from >= shown.Count || to < 0 || to > shown.Count || from == to || from == to - 1)
            return;

        var reordered = new List<MechUpgradeDef>(shown);
        var moved = reordered[from];
        reordered.RemoveAt(from);
        reordered.Insert(to > from ? to - 1 : to, moved);

        // Ranked first, then the unranked ones that are allowed, each shown upgrade's slot taken in turn by the new order
        var full = Ranking.Concat(InRankOrder(AllowedUpgradeDefs.Concat(shown).Where(d => !Ranking.Contains(d)).Distinct())).ToList();
        var next = 0;
        for (var i = 0; i < full.Count; i++)
        {
            if (shown.Contains(full[i]))
                full[i] = reordered[next++];
        }

        Ranking = full;
    }

    /// <summary>
    /// Makes this policy allow exactly the upgrades in one of MU's saved upgrade sets, ranked in the set's order.
    /// </summary>
    public void LoadFrom(UpgradeSet set)
    {
        Filter.SetDisallowAll();
        var defs = set.upgrades.Where(d => d?.linkedThingDef != null).Distinct().ToList();
        foreach (var def in defs)
        {
            Filter.SetAllow(def.linkedThingDef, true);
        }

        Ranking = defs;
    }

    public override void CopyFrom(Policy other)
    {
        if (other is not UpgradePolicy upgradePolicy)
            return;

        Filter.CopyAllowancesFrom(upgradePolicy.Filter);
        Ranking = [..upgradePolicy.Ranking];
        ReplaceLowerRanked = upgradePolicy.ReplaceLowerRanked;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref Filter, "filter");
        Scribe_Collections.Look(ref Ranking, "ranking", LookMode.Def);
        Scribe_Values.Look(ref ReplaceLowerRanked, "replaceLowerRanked");

        // An older save has no ranking, and a removed mod's upgrades load as null
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            Ranking ??= [];
            Ranking.RemoveAll(d => d == null);
        }
    }
}
