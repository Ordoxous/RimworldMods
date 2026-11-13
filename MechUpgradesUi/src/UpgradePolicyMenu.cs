using System;
using System.Collections.Generic;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>The policy choices offered by the Mechs tab column and the mech's gizmo.</summary>
public static class UpgradePolicyMenu
{
    public struct Option
    {
        public string Label;

        /// <summary>The policy this option assigns. Null for both "no policy" and "edit".</summary>
        public UpgradePolicy Policy;

        public Action Action;
    }

    private static WorldComponent_UpgradeDatabase Database => Find.World.GetComponent<WorldComponent_UpgradeDatabase>();

    /// <summary>What a mech without a policy is shown as, which depends on the "use the default policy" setting.</summary>
    public static string NoPolicyLabel() =>
        MechUpgradesUiMod.Settings.UseDefaultPolicyForUnassigned
            ? "MU_Upgrades_Ui_Policy_Default".Translate(Database.DefaultPolicy().label)
            : "MU_Upgrades_Ui_Policy_None".Translate();

    public static string LabelFor(CompUpgradableMechWithPolicy comp) => comp.CurrentUpgradePolicy?.label ?? NoPolicyLabel();

    /// <summary>
    /// "No policy" first, then each policy, then "Edit...". The order matters to the column: painting picks the first option
    /// matching the payload, and both "no policy" and "edit" have a null one.
    /// </summary>
    public static IEnumerable<Option> Options(
        Action<UpgradePolicy> assign,
        CompUpgradableMechWithPolicy editFor
    )
    {
        yield return new Option { Label = NoPolicyLabel(), Policy = null, Action = () => assign(null) };

        foreach (var policy in Database.GetPolicies())
        {
            yield return new Option { Label = policy.label, Policy = policy, Action = () => assign(policy) };
        }

        yield return new Option
        {
            Label = $"{"AssignTabEdit".Translate()}...",
            Policy = null,
            Action = () => Find.WindowStack.Add(new Dialog_ManageUpgradePolicies(editFor.CurrentUpgradePolicy, editFor))
        };
    }
}
