using System.Collections.Generic;
using System.Linq;
using MU;
using Verse;

namespace Ordo.MechUpgradesUi;

public class CompProperties_CompUpgradableMechWithPolicy : CompProperties_UpgradableMechanoid
{
    public CompProperties_CompUpgradableMechWithPolicy() => compClass = typeof(CompUpgradableMechWithPolicy);
}

public class CompUpgradableMechWithPolicy : CompUpgradableMechanoid
{

    private UpgradePolicy _currentUpgradePolicy;

    public UpgradePolicy CurrentUpgradePolicy
    {
        get
        {
            return _currentUpgradePolicy ??= Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy();
        }
        set
        {
            _currentUpgradePolicy = value;
            Mech.mindState?.Notify_OutfitChanged();
        }
    }

    public List<MechUpgrade> UnwantedUpgrades()
    {
        return upgrades
            .Where(u => !CurrentUpgradePolicy.Filter.Allows(u.def.linkedThingDef))
            .ToList();
    }

    public List<MechUpgradeDef> WantedUpgrades()
    {
        return CurrentUpgradePolicy.Filter.AllowedThingDefs
            .Select(t => t.comps.OfType<CompProperties_MechUpgrade>().First().upgradeDef)
            .Where(d => d.CanAdd(Mech.def, upgrades) && (d.upgradePoints <= MechUpgradeUtility.MaxUpgradePoints(Mech) - MechUpgradeUtility.CurrentPoints(Mech)))
            .ToList();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_References.Look(ref _currentUpgradePolicy, "currentUpgradePolicy");
    }
}