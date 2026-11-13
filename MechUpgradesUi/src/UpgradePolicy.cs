using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

public class UpgradePolicy : Policy
{
    public ThingFilter Filter = new();

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

    /// <summary>Makes this policy allow exactly the upgrades in one of MU's saved upgrade sets.</summary>
    public void LoadFrom(UpgradeSet set)
    {
        Filter.SetDisallowAll();
        foreach (var def in set.upgrades.Where(d => d?.linkedThingDef != null))
        {
            Filter.SetAllow(def.linkedThingDef, true);
        }
    }

    public override void CopyFrom(Policy other)
    {
        if (other is not UpgradePolicy upgradePolicy)
            return;

        Filter.CopyAllowancesFrom(upgradePolicy.Filter);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref Filter, "filter");
    }
}
