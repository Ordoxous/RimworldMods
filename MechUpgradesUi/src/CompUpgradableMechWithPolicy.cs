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

    /// <summary>The assigned policy. Null means the mech is unmanaged and its upgrades are left alone.</summary>
    public UpgradePolicy CurrentUpgradePolicy
    {
        get
        {
            return _currentUpgradePolicy;
        }
        set
        {
            _currentUpgradePolicy = value;
            Mech.mindState?.Notify_OutfitChanged();
        }
    }

    /// <summary>The policy to act on. Null means the mech's upgrades are left alone.</summary>
    public UpgradePolicy EffectiveUpgradePolicy =>
        _currentUpgradePolicy
        ?? (MechUpgradesUiMod.Settings.UseDefaultPolicyForUnassigned
            ? Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy()
            : null);

    public List<MechUpgrade> UpgradesToKeep()
    {
        var filter = EffectiveUpgradePolicy.Filter;
        return upgrades
            .Where(u => filter.Allows(u.def.linkedThingDef))
            .ToList();
    }

    public List<MechUpgradeDef> WantedUpgrades(List<MechUpgrade> upgradesToKeep)
    {
        // Check against the upgrades that will remain, so an upgrade can replace an unwanted one in the same trip.
        // The point budget is handled by Building_AutoMechUpgrader.WantedOperations.
        return EffectiveUpgradePolicy.Filter.AllowedThingDefs
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