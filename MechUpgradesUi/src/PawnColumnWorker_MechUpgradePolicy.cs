using System;
using System.Collections.Generic;
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
        if (Widgets.ButtonText(rect1, "Manage Upgrade Policies"))
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

        var upgradeDatabase = Find.World.GetComponent<WorldComponent_UpgradeDatabase>();
        var policies = upgradeDatabase.GetPolicies();

        Widgets.Dropdown(
            left,
            comp,
            p => p.CurrentUpgradePolicy,
            MenuGenerator,
            comp.CurrentUpgradePolicy.label.Truncate(left.width),
            dragLabel: comp.CurrentUpgradePolicy.label,
            paintable: true
        );
        return;

        IEnumerable<Widgets.DropdownMenuElement<UpgradePolicy>> MenuGenerator(CompUpgradableMechWithPolicy currComp)
        {
            foreach (var policy in policies)
            {
                yield return new Widgets.DropdownMenuElement<UpgradePolicy>
                {
                    option = new FloatMenuOption(policy.label, () => currComp.CurrentUpgradePolicy = policy), payload = policy
                };
            }

            yield return new Widgets.DropdownMenuElement<UpgradePolicy>
            {
                option = new FloatMenuOption(
                    $"{"AssignTabEdit".Translate()}...",
                    (Action) (() => Find.WindowStack.Add(new Dialog_ManageUpgradePolicies(currComp.CurrentUpgradePolicy, currComp)))
                )
            };
        }
    }

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