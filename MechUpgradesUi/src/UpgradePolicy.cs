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

    protected override string LoadKey => nameof (UpgradePolicy);

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
