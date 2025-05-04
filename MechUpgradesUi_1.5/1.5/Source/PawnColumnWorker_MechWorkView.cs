using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MechUi;

public class PawnColumnWorker_MechWorkView : PawnColumnWorker {

    public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
    {
        if (pawn.GetOverseer() == null) return;

        var comp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        if (comp == null) return;

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, pawn.jobs.curJob?.def.reportString ?? "");
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
    }
}