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

    /// <summary>The explicitly assigned policy, without falling back to the default.</summary>
    internal UpgradePolicy AssignedUpgradePolicy => _currentUpgradePolicy;

    public UpgradePolicy CurrentUpgradePolicy
    {
        get
        {
            return _currentUpgradePolicy ?? Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy();
        }
        set
        {
            _currentUpgradePolicy = value;
            Mech.mindState?.Notify_OutfitChanged();
        }
    }

    public List<MechUpgrade> UpgradesToKeep()
    {
        return upgrades
            .Where(u => CurrentUpgradePolicy.Filter.Allows(u.def.linkedThingDef))
            .ToList();
    }

    public List<MechUpgradeDef> WantedUpgrades(List<MechUpgrade> upgradesToKeep)
    {
        // Check against the upgrades that will remain, so an upgrade can replace an unwanted one in the same trip.
        // The point budget is handled by Building_AutoMechUpgrader.WantedOperations.
        return CurrentUpgradePolicy.Filter.AllowedThingDefs
            .Select(t => t.comps.OfType<CompProperties_MechUpgrade>().First().upgradeDef)
            .Where(d => d.CanAdd(Mech.def, upgradesToKeep))
            .ToList();
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_References.Look(ref _currentUpgradePolicy, "currentUpgradePolicy");
    }
}