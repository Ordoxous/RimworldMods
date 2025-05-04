using MU;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

public class PawnColumnWorker_MechUpgrades : PawnColumnWorker
{

    public override void DoCell(Rect rect, Pawn pawn, PawnTable table)
    {
        if (pawn.GetOverseer() == null) return;

        var comp = pawn.TryGetComp<CompUpgradableMechWithPolicy>();
        if (comp == null) return;

        var currPoints = MechUpgradeUtility.CurrentPoints(comp.upgrades);
        var maxPoints = MechUpgradeUtility.MaxUpgradePoints(pawn);
        var pointsRatio = currPoints / (float) maxPoints;

        var fillableBar = Widgets.FillableBar(
            rect.ContractedBy(4f),
            pointsRatio,
            PawnColumnWorker_Energy.EnergyBarTex,
            BaseContent.ClearTex,
            false
        );
        
        if (Widgets.ButtonInvisible(fillableBar))
        {
            CameraJumper.TryJumpAndSelect((GlobalTargetInfo) (Thing) pawn);
            Find.MainTabsRoot.EscapeCurrentTab(false);
            InspectPaneUtility.OpenTab(typeof(ITab_MechUpgrades));
            
            MechUpgradeUtility.ApplyCombination(pawn, new UpgradeCombinationDef());
        }

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, $"{currPoints} / {maxPoints}");
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;
    }

    public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 100);

    public override int GetMaxWidth(PawnTable table) => Mathf.Min(base.GetMaxWidth(table), GetMinWidth(table));
}