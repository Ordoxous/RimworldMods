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
        var pointsRatio = maxPoints > 0 ? currPoints / (float) maxPoints : 0f;

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
        }

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, $"{currPoints} / {maxPoints}");
        Text.Anchor = TextAnchor.UpperLeft;
        Text.Font = GameFont.Small;

        // White: the next trip would change something. Red: it can't, for want of a usable upgrader. Yellow: upgrades are
        // wanted that nothing has in stock
        var status = UpgradeStatus.For(comp);
        Color? marker = status.HasChanges ? Color.white
            : status.Blocker != null ? UpgradeStatus.RemovingColor
            : status.HasShortage ? UpgradeStatus.WantedColor
            : null;
        if (marker.HasValue)
        {
            var iconRect = new Rect(fillableBar.xMax - PendingIconSize - 2f, fillableBar.center.y - PendingIconSize / 2f, PendingIconSize, PendingIconSize);
            GUI.color = marker.Value;
            GUI.DrawTexture(iconRect, PendingIcon.Texture);
            GUI.color = Color.white;
        }

        TooltipHandler.TipRegion(rect, new TipSignal(() => UpgradeStatus.Tooltip(comp), pawn.thingIDNumber ^ 0x4D55));
    }

    private const float PendingIconSize = 16f;

    private static readonly CachedTexture PendingIcon = new("UI/Gizmos/MU_ChangeUpgrades");

    public override int GetMinWidth(PawnTable table) => Mathf.Max(base.GetMinWidth(table), 100);

    public override int GetMaxWidth(PawnTable table) => Mathf.Min(base.GetMaxWidth(table), GetMinWidth(table));
}