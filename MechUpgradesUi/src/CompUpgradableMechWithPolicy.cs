using System.Collections.Generic;
using System.Linq;
using MU;
using Verse;
using RimWorld;

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
        return EffectiveUpgradePolicy.AllowedUpgradeDefs
            .Where(d => d.CanAdd(Mech.def, upgradesToKeep))
            .ToList();
    }

    private static readonly CachedTexture CheckNowIcon = new("UI/Gizmos/MU_InsertMechanoid");

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (var gizmo in base.CompGetGizmosExtra())
            yield return gizmo;

        if (!Mech.IsColonyMechPlayerControlled)
            yield break;

        yield return new Command_UpgradePolicy(this);

        if (EffectiveUpgradePolicy != null)
        {
            yield return new Command_Action
            {
                defaultLabel = "MU_Upgrades_Ui_Gizmo_CheckNow".Translate(),
                defaultDesc = "MU_Upgrades_Ui_Gizmo_CheckNow_Desc".Translate(),
                icon = CheckNowIcon.Texture,
                action = CheckNow
            };
        }
    }

    /// <summary>Makes the mech's next policy check due now, as assigning a policy does.</summary>
    public void CheckNow() => Mech.mindState?.Notify_OutfitChanged();

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_References.Look(ref _currentUpgradePolicy, "currentUpgradePolicy");
    }
}