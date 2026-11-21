using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ordo.FloraTab;

// The Plants page: the map's harvestable wild plants grouped by kind, like Wildlife is for animals
public class PlantsPage : TablePage<FloraRow>
{
    // Kept across openings, as the Wildlife tab keeps its sort
    private FloraFilter filter = new() { hideYieldless = true };

    public PlantsPage() => SortByDefault(3, descending: true);

    public override string LabelKey => "Ordo_FloraTab_PlantsPage";
    protected override string NoneKey => "Ordo_FloraTab_None";

    protected override List<Column<FloraRow>> MakeColumns() => new()
    {
        new()
        {
            labelKey = "Ordo_FloraTab_Plant", width = 200f, headerIndent = IconSize + 8f, leftAligned = true,
            sortText = r => r.def.label,
            draw = (rect, row) => ThingCell(rect, row.def,
                row.def.LabelCap + "\n\n" + row.def.description + "\n\n" + "Ordo_FloraTab_JumpTip".Translate(row.def.label),
                () => JumpToNext(row)),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Yields", tipKey = "Ordo_FloraTab_YieldsTip", width = 170f, leftAligned = true,
            sortText = r => r.yields.Count == 0 ? "" : r.yields[0].Thing.label,
            draw = DoYieldsCell,
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Count", tipKey = "Ordo_FloraTab_CountTip", width = 60f, descendingFirst = true,
            sortValue = r => r.plants.Count,
            draw = (rect, row) => NumberCell(rect, row.plants.Count.ToString(), Color.white),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Ready", tipKey = "Ordo_FloraTab_ReadyTip", width = 60f, descendingFirst = true,
            sortValue = r => r.ready,
            draw = (rect, row) => NumberCell(rect, row.ready.ToString(), row.ready > 0 ? Color.green : Color.gray),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Next", tipKey = "Ordo_FloraTab_NextTip", width = 76f,
            sortValue = r => r.nextReadyDays,
            draw = (rect, row) => NumberCell(rect, NextText(row),
                float.IsPositiveInfinity(row.nextReadyDays) ? Color.gray : Color.white, NextTip(row)),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Yield", tipKey = "Ordo_FloraTab_YieldTip", width = 80f, descendingFirst = true,
            sortValue = r => r.YieldValue,
            draw = (rect, row) =>
            {
                bool any = row.yields.Any(y => y.now > 0f);
                NumberCell(rect, any ? YieldNowText(row) : "-", any ? Color.white : Color.gray, any ? YieldNowTip(row) : null);
            },
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Risk", tipKey = "Ordo_FloraTab_RiskTip", width = 60f, descendingFirst = true,
            sortValue = r => r.AtRisk,
            draw = (rect, row) => NumberCell(rect, row.AtRisk > 0 ? row.AtRisk.ToString() : "-",
                row.AtRisk > 0 ? RiskColor : Color.gray, row.AtRisk > 0 ? RiskTip(row) : null),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Near", tipKey = "Ordo_FloraTab_NearTip", width = 60f,
            sortValue = r => r.nearest,
            draw = (rect, row) => NumberCell(rect, DistanceText(row.nearest), Color.white,
                "Ordo_FloraTab_NearRowTip".Translate(Mathf.RoundToInt(row.nearest), row.def.label)),
        },
        new()
        {
            labelKey = "Ordo_FloraTab_Marked", tipKey = "Ordo_FloraTab_MarkedTip", width = 60f, descendingFirst = true,
            sortValue = r => r.marked,
            draw = (rect, row) => CountCell(rect, row.marked, Color.yellow),
        },
        new()
        {
            width = 80f,
            draw = (rect, row) => ButtonCell(rect,
                row.plants.Any(p => p.HarvestableNow && map.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null),
                "Ordo_FloraTab_HarvestAll", "Ordo_FloraTab_HarvestAllTip".Translate(row.def.label), () => HarvestAll(row)),
        },
        new()
        {
            width = 70f,
            draw = (rect, row) => ButtonCell(rect, row.marked > 0, "Ordo_FloraTab_Cancel",
                "Ordo_FloraTab_CancelTip".Translate(row.def.label), () => CancelAll(row)),
        },
    };

    public override bool DoToggles(
        ref float x,
        Rect bar)
    {
        bool changed = false;
        changed |= Toggle(ref x, bar, "Ordo_FloraTab_IncludeTrees", ref filter.includeTrees);
        changed |= Toggle(ref x, bar, "Ordo_FloraTab_IncludeCultivated", ref filter.includeCultivated);
        changed |= Toggle(ref x, bar, "Ordo_FloraTab_OnlyReady", ref filter.onlyReady);
        changed |= Toggle(ref x, bar, "Ordo_FloraTab_HideYieldless", ref filter.hideYieldless);
        return changed;
    }

    protected override List<FloraRow> Build(
        Map map,
        Area area,
        int maxDistance)
    {
        filter.area = area;
        filter.maxDistance = maxDistance;
        return FloraSurvey.Build(map, filter);
    }

    protected override bool Matches(
        FloraRow row,
        QuickSearchFilter search) =>
        search.Matches(row.def.label) || row.yields.Any(y => search.Matches(y.Thing.label));

    protected override string Label(FloraRow row) => row.def.label;

    protected override void DrawHighlight(
        FloraRow row,
        Map map) =>
        FloraHighlight.Draw(row, map);

    // Each thing the kind gives, as an icon that opens its info card; one thing also gets its name, several get theirs
    // joined after the icons
    private static void DoYieldsCell(
        Rect cell,
        FloraRow row)
    {
        if (row.yields.Count == 0)
        {
            GUI.color = Color.gray;
            Widgets.Label(cell, "Ordo_FloraTab_NoYield".Translate());
            GUI.color = Color.white;
            TooltipHandler.TipRegion(cell, "Ordo_FloraTab_NoYieldTip".Translate());
            return;
        }

        float x = cell.x;
        foreach (FloraYield y in row.yields)
        {
            if (x + IconSize > cell.xMax - 4f)
                break;
            var iconRect = new Rect(x, cell.y + (cell.height - IconSize) / 2f, IconSize, IconSize);
            Widgets.ThingIcon(iconRect, y.Thing);
            Widgets.DrawHighlightIfMouseover(iconRect);
            TooltipHandler.TipRegion(iconRect, YieldTip(y.source));
            if (Widgets.ButtonInvisible(iconRect))
                Find.WindowStack.Add(new Dialog_InfoCard(y.Thing));
            x += IconSize + 2f;
        }

        var labelRect = new Rect(x + 4f, cell.y, cell.xMax - x - 8f, cell.height);
        if (labelRect.width < 20f)
            return;
        string label = row.yields.Count == 1
            ? row.yields[0].Thing.LabelCap.ToString()
            : string.Join(", ", row.yields.Select(y => y.Thing.label)).CapitalizeFirst();
        Widgets.Label(labelRect, label.Truncate(labelRect.width));
        Widgets.DrawHighlightIfMouseover(labelRect);
        TooltipHandler.TipRegion(labelRect, string.Join("\n\n", row.yields.Select(y => YieldTip(y.source))));
        if (Widgets.ButtonInvisible(labelRect))
            Find.WindowStack.Add(new Dialog_InfoCard(row.yields[0].Thing));
    }

    private static string YieldTip(YieldSource source)
    {
        string tip = source.thing.LabelCap;
        if (!source.primary)
        {
            tip += "\n" + (source.share < 1f
                ? "Ordo_FloraTab_RandomYieldTip".Translate(source.amount, source.share.ToStringPercent())
                : "Ordo_FloraTab_SecondaryYieldTip".Translate(source.amount));
        }
        return tip + "\n\n" + source.thing.description;
    }

    private static string YieldNowText(FloraRow row) =>
        string.Join(" + ", row.yields.Select(y => FloraYields.AmountText(y.now)));

    private static string YieldNowTip(FloraRow row)
    {
        var lines = row.yields.Select(y => "Ordo_FloraTab_YieldNowRowTip".Translate(FloraYields.AmountText(y.now), y.Thing.label)
            .ToString()).ToList();
        if (row.yields.Count > 1)
            lines.Add("\n" + "Ordo_FloraTab_YieldValueRowTip".Translate(row.YieldValue.ToStringMoney()));
        return string.Join("\n", lines);
    }

    private static string NextText(FloraRow row)
    {
        if (row.ready == row.plants.Count)
            return "-";
        if (float.IsPositiveInfinity(row.nextReadyDays))
            return "Ordo_FloraTab_Stalled".Translate();
        return Period(row.nextReadyDays);
    }

    private static string NextTip(FloraRow row)
    {
        int growing = row.plants.Count - row.ready;
        if (growing == 0)
            return null;
        var lines = new List<string>();
        if (!float.IsPositiveInfinity(row.nextReadyDays))
            lines.Add("Ordo_FloraTab_NextRowTip".Translate(Period(row.nextReadyDays)));
        lines.Add("Ordo_FloraTab_SoonRowTip".Translate(row.soon, growing));
        if (row.stalled > 0)
            lines.Add("Ordo_FloraTab_StalledRowTip".Translate(row.stalled));
        if (!float.IsPositiveInfinity(row.growthStopsDays))
            lines.Add("Ordo_FloraTab_GrowthStopsRowTip".Translate(Period(row.growthStopsDays),
                row.def.plant.minGrowthTemperature.ToStringTemperature("F0")));
        return string.Join("\n", lines);
    }

    private static string RiskTip(FloraRow row)
    {
        var lines = new List<string>();
        if (row.dying > 0)
            lines.Add("Ordo_FloraTab_DyingRowTip".Translate(row.dying));
        if (row.oldAge > 0)
            lines.Add("Ordo_FloraTab_OldAgeRowTip".Translate(row.oldAge, Period(row.oldAgeDays)));
        if (row.frost > 0)
            lines.Add("Ordo_FloraTab_FrostRowTip".Translate(row.frost, Period(row.frostDays)));
        lines.Add("Ordo_FloraTab_RiskHighlightTip".Translate());
        return string.Join("\n", lines);
    }

    // Cycles through the kind's plants: at risk first, then ready, then by position
    private void JumpToNext(FloraRow row) =>
        JumpToNext(row.def, row.plants
            .Where(p => p.Spawned)
            .OrderByDescending(p => row.atRisk.Contains(p))
            .ThenByDescending(p => p.HarvestableNow)
            .ThenBy(p => p.Position.z)
            .ThenBy(p => p.Position.x)
            .Cast<Thing>()
            .ToList());

    private void HarvestAll(FloraRow row)
    {
        int count = FloraDesignations.HarvestReady(row, map);
        if (count == 0)
            return;
        (row.IsTree ? SoundDefOf.Designate_HarvestWood : SoundDefOf.Designate_HarvestPlants).PlayOneShotOnCamera();
        Messages.Message("Ordo_FloraTab_Designated".Translate(count + " " + row.def.label), MessageTypeDefOf.SilentInput,
            historical: false);
        MainTabWindow_Flora.SurveyNow();
    }

    private void CancelAll(FloraRow row)
    {
        FloraDesignations.Cancel(row, map);
        SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
        MainTabWindow_Flora.SurveyNow();
    }
}
