using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ordo.NatureTab;

// The Minerals page: the map's visible ore, compacted machinery and other resource rock, grouped by kind
public class MineralsPage : TablePage<MineralRow>
{
    private static readonly Color OverheadColor = new(1f, 0.6f, 0.2f);

    public MineralsPage() => SortByDefault(5, descending: true);

    public override string LabelKey => "Ordo_NatureTab_MineralsPage";
    protected override string NoneKey => "Ordo_NatureTab_NoMinerals";

    protected override List<Column<MineralRow>> MakeColumns() => new()
    {
        new()
        {
            labelKey = "Ordo_NatureTab_Mineral", width = 200f, headerIndent = IconSize + 8f, leftAligned = true,
            sortText = r => r.def.label,
            draw = (rect, row) => ThingCell(rect, row.def,
                row.def.LabelCap + "\n\n" + row.def.description + "\n\n" + "Ordo_NatureTab_JumpVeinTip".Translate(row.def.label),
                () => JumpToNext(row.def, row.veins.Where(m => m.Spawned).Cast<Thing>().ToList())),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Yields", tipKey = "Ordo_NatureTab_MineralYieldsTip", width = 150f, leftAligned = true,
            sortText = r => r.Product.label,
            draw = (rect, row) => ThingCell(rect, row.Product, row.Product.LabelCap + "\n\n" + row.Product.description,
                () => Find.WindowStack.Add(new Dialog_InfoCard(row.Product))),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_CellCount", tipKey = "Ordo_NatureTab_CellCountTip", width = 60f, descendingFirst = true,
            sortValue = r => r.cells.Count,
            draw = (rect, row) => NumberCell(rect, row.cells.Count.ToString(), Color.white),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Veins", tipKey = "Ordo_NatureTab_VeinsTip", width = 60f, descendingFirst = true,
            sortValue = r => r.veins.Count,
            draw = (rect, row) => NumberCell(rect, row.veins.Count.ToString(), Color.white),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_MineYield", tipKey = "Ordo_NatureTab_MineYieldTip", width = 80f, descendingFirst = true,
            sortValue = r => r.yield,
            draw = (rect, row) => NumberCell(rect, Mathf.RoundToInt(row.yield).ToString(), Color.white,
                "Ordo_NatureTab_YieldNowRowTip".Translate(Mathf.RoundToInt(row.yield), row.Product.label)),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Value", tipKey = "Ordo_NatureTab_ValueTip", width = 80f, descendingFirst = true,
            sortValue = r => r.YieldValue,
            draw = (rect, row) => NumberCell(rect, row.YieldValue.ToStringMoney(), Color.white),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Overhead", tipKey = "Ordo_NatureTab_OverheadTip", width = 76f, descendingFirst = true,
            sortValue = r => r.overhead,
            draw = (rect, row) => NumberCell(rect, row.overhead > 0 ? row.overhead.ToString() : "-",
                row.overhead > 0 ? OverheadColor : Color.gray,
                row.overhead > 0 ? "Ordo_NatureTab_OverheadRowTip".Translate(row.overhead, row.cells.Count) : null),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Near", tipKey = "Ordo_NatureTab_NearTip", width = 60f,
            sortValue = r => r.nearest,
            draw = (rect, row) => NumberCell(rect, DistanceText(row.nearest), Color.white,
                "Ordo_NatureTab_NearRowTip".Translate(Mathf.RoundToInt(row.nearest), row.def.label)),
        },
        new()
        {
            labelKey = "Ordo_NatureTab_Marked", tipKey = "Ordo_NatureTab_MarkedMineTip", width = 60f, descendingFirst = true,
            sortValue = r => r.marked,
            draw = (rect, row) => CountCell(rect, row.marked, Color.yellow),
        },
        new()
        {
            width = 80f,
            draw = (rect, row) => ButtonCell(rect, row.marked < row.cells.Count, "Ordo_NatureTab_MineAll",
                "Ordo_NatureTab_MineAllTip".Translate(row.def.label), () => MineAll(row)),
        },
        new()
        {
            width = 70f,
            draw = (rect, row) => ButtonCell(rect, row.marked > 0, "Ordo_NatureTab_Cancel",
                "Ordo_NatureTab_CancelMineTip".Translate(row.def.label), () => CancelAll(row)),
        },
    };

    public override bool DoToggles(
        ref float x,
        Rect bar) =>
        false;

    protected override List<MineralRow> Build(
        Map map,
        Area area,
        int maxDistance) =>
        MineralSurvey.Build(map, new MineralFilter { area = area, maxDistance = maxDistance });

    protected override bool Matches(
        MineralRow row,
        QuickSearchFilter search) =>
        search.Matches(row.def.label) || search.Matches(row.Product.label);

    protected override string Label(MineralRow row) => row.def.label;

    protected override void DrawHighlight(
        MineralRow row,
        Map map) =>
        MineralHighlight.Draw(row, map);

    private void MineAll(MineralRow row)
    {
        int count = MineralDesignations.MineAll(row, map);
        if (count == 0)
            return;
        SoundDefOf.Designate_Mine.PlayOneShotOnCamera();
        Messages.Message("Ordo_NatureTab_DesignatedMine".Translate(count, row.def.label), MessageTypeDefOf.SilentInput,
            historical: false);
        MainTabWindow_Nature.SurveyNow();
    }

    private void CancelAll(MineralRow row)
    {
        MineralDesignations.Cancel(row, map);
        SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
        MainTabWindow_Nature.SurveyNow();
    }
}
