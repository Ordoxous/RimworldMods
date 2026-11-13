using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>Picks the upgrade policy for every selected mech, from one menu.</summary>
public class Command_UpgradePolicy : Command
{
    private static readonly CachedTexture Icon = new("UI/Gizmos/MU_ChangeUpgrades");

    private readonly CompUpgradableMechWithPolicy _comp;

    public Command_UpgradePolicy(CompUpgradableMechWithPolicy comp)
    {
        _comp = comp;
        defaultLabel = "MU_Upgrades_Ui_Gizmo_Policy".Translate(UpgradePolicyMenu.LabelFor(comp));
        defaultDesc = "MU_Upgrades_Ui_Gizmo_Policy_Desc".Translate();
        icon = Icon.Texture;

        // Group across mechs whatever their policy, so one button covers the whole selection
        groupKeyIgnoreContent = 0x4D55_5000;
    }

    public override void ProcessInput(Event ev)
    {
        base.ProcessInput(ev);
        var options = UpgradePolicyMenu
            .Options(AssignToSelected, _comp)
            .Select(o => new FloatMenuOption(o.Label, o.Action))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // The menu already covers every selected mech, so the other mechs' buttons mustn't open their own
    public override bool InheritInteractionsFrom(Gizmo other) => false;

    private static void AssignToSelected(UpgradePolicy policy)
    {
        foreach (var pawn in Find.Selector.SelectedPawns)
        {
            if (pawn.IsColonyMechPlayerControlled && pawn.TryGetComp<CompUpgradableMechWithPolicy>(out var comp))
                comp.CurrentUpgradePolicy = policy;
        }
    }
}
