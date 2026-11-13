using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ordo.MechUpgradesUi;

public class PawnColumnWorker_MechUpgradePolicy : PawnColumnWorker
{

    public override void DoHeader(Rect rect, PawnTable table)
    {
        base.DoHeader(rect, table);
        MouseoverSounds.DoRegion(rect);
        var rect1 = new Rect(rect.x, rect.y + (rect.height - 65f), Mathf.Min(rect.width, 360f), 32f);
        if (Widgets.ButtonText(rect1, "MU_Upgrades_Ui_Manage_Policies".Translate()))
        {
            Find.WindowStack.Add(new Dialog_ManageUpgradePolicies(null, null));
        }
    }

    public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
    {
        if (pawn.GetOverseer() == null) return;

        var comp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        if (comp == null) return;

        var left = rect.ContractedBy(0.0f, 2f);
        var currentLabel = UpgradePolicyMenu.LabelFor(comp);

        Widgets.Dropdown(
            left,
            comp,
            p => p.CurrentUpgradePolicy,
            MenuGenerator,
            currentLabel.Truncate(left.width),
            dragLabel: currentLabel,
            paintable: true
        );
    }

    private static IEnumerable<Widgets.DropdownMenuElement<UpgradePolicy>> MenuGenerator(CompUpgradableMechWithPolicy comp) =>
        UpgradePolicyMenu
            .Options(policy => comp.CurrentUpgradePolicy = policy, comp)
            .Select(o => new Widgets.DropdownMenuElement<UpgradePolicy>
            {
                option = new FloatMenuOption(o.Label, o.Action),
                payload = o.Policy
            });

    public override int GetMinWidth(PawnTable table)
    {
        return Mathf.Max(base.GetMinWidth(table), Mathf.CeilToInt(194f));
    }

    public override int GetOptimalWidth(PawnTable table)
    {
        return Mathf.Clamp(Mathf.CeilToInt(251f), GetMinWidth(table), GetMaxWidth(table));
    }

    public override int GetMinHeaderHeight(PawnTable table)
    {
        return Mathf.Max(base.GetMinHeaderHeight(table), 65);
    }

    public override int Compare(Pawn a, Pawn b)
    {
        return GetValueToCompare(a).CompareTo(GetValueToCompare(b));
    }

    private int GetValueToCompare(Pawn pawn)
    {
        if (pawn.GetOverseer() == null) return 0;

        var comp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        if (comp == null) return 0;

        return comp is { CurrentUpgradePolicy: not null }
            ? comp.CurrentUpgradePolicy.id
            : int.MinValue;
    }
}