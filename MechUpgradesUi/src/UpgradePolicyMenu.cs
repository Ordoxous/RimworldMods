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

        /// <summary>What choosing it would do to the mech's upgrades. Null for "edit".</summary>
        public Func<string> Tooltip;

        public FloatMenuOption ToFloatMenuOption(int tipId)
        {
            var option = new FloatMenuOption(Label, Action);
            if (Tooltip != null)
                option.tooltip = new TipSignal(Tooltip, tipId);
            return option;
        }
    }

    private static WorldComponent_UpgradeDatabase Database => Find.World.GetComponent<WorldComponent_UpgradeDatabase>();

    /// <summary>What a mech without a policy is shown as, which depends on the "use the default policy" setting.</summary>
    public static string NoPolicyLabel() =>
        MechUpgradesUiMod.Settings.UseDefaultPolicyForUnassigned
            ? "MU_Upgrades_Ui_Policy_Default".Translate(Database.DefaultPolicy().label)
            : "MU_Upgrades_Ui_Policy_None".Translate();

    public static string LabelFor(CompUpgradableMechWithPolicy comp) => comp.CurrentUpgradePolicy?.label ?? NoPolicyLabel();

    /// <summary>A tooltip id unique to a mech and a menu position, so each option's tooltip is its own.</summary>
    public static int TipId(CompUpgradableMechWithPolicy comp, int index) => Gen.HashCombineInt(comp.Mech.thingIDNumber, 0x4D55_7000 + index);

    /// <summary>
    /// "No policy" first, then each policy, then "Edit...". The order matters to the column: painting picks the first option
    /// matching the payload, and both "no policy" and "edit" have a null one. Tooltips describe the policy for
    /// <paramref name="comp"/>'s mech.
    /// </summary>
    public static IEnumerable<Option> Options(
        Action<UpgradePolicy> assign,
        CompUpgradableMechWithPolicy comp
    )
    {
        yield return new Option
        {
            Label = NoPolicyLabel(),
            Policy = null,
            Action = () => assign(null),
            Tooltip = () => MechUpgradesUiMod.Settings.UseDefaultPolicyForUnassigned
                ? UpgradeStatus.Describe(comp, Database.DefaultPolicy())
                : "MU_Upgrades_Ui_Status_Unmanaged".Translate().ToString()
        };

        foreach (var policy in Database.GetPolicies())
        {
            yield return new Option
            {
                Label = policy.label,
                Policy = policy,
                Action = () => assign(policy),
                Tooltip = () => UpgradeStatus.Describe(comp, policy)
            };
        }

        yield return new Option
        {
            Label = $"{"AssignTabEdit".Translate()}...",
            Policy = null,
            Action = () => Find.WindowStack.Add(new Dialog_ManageUpgradePolicies(comp.CurrentUpgradePolicy, comp))
        };
    }
}
