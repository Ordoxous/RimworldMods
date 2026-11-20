using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ordo.FloraTab;

// The Flora main tab: the map's harvestable wild plants grouped by kind, like Wildlife is for animals
public class MainTabWindow_Flora : MainTabWindow
{
    private enum SortColumn
    {
        Label,
        Count,
        Ready,
        Next,
        Yield,
        Risk,
        Near,
        Marked,
    }

    private const float RowHeight = 30f;
    private const float HeaderHeight = 30f;
    private const float ToolbarHeight = 32f;
    private const float IconSize = 24f;
    private const float RefreshSeconds = 1f;

    private const float LabelWidth = 200f;
    private const float ProductWidth = 150f;
    private const float NumberWidth = 60f;
    private const float NextWidth = 76f;
    private const float YieldWidth = 80f;
    private const float HarvestButtonWidth = 80f;
    private const float CancelButtonWidth = 70f;

    private static readonly int[] DistanceChoices = { 0, 25, 50, 75, 100, 150, 200 };
    private static readonly Color RiskColor = new(1f, 0.4f, 0.35f);

    // Kept across openings, as the Wildlife tab keeps its sort
    private static FloraFilter filter;
    private static SortColumn sortBy = SortColumn.Ready;
    private static bool sortDescending = true;

    private readonly QuickSearchWidget search = new();
    private readonly Dictionary<ThingDef, int> nextJumpIndex = new();
    private List<FloraRow> rows = new();
    private Map surveyedMap;
    private float lastSurvey = -999f;
    private Vector2 scroll;
    private FloraRow hoveredRow;
    private int hoveredFrame = -1;

    public override Vector2 RequestedTabSize => new(
        LabelWidth + ProductWidth + NumberWidth * 5 + NextWidth + YieldWidth + HarvestButtonWidth + CancelButtonWidth + 70f,
        Mathf.Max(260f, ToolbarHeight + HeaderHeight + RowHeight * Mathf.Clamp(rows.Count, 3, 16) + Margin * 2 + 16f));

    public override void PostOpen()
    {
        base.PostOpen();
        Survey();
        SetInitialSizeAndPosition();
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (Find.CurrentMap != surveyedMap || Time.realtimeSinceStartup - lastSurvey > RefreshSeconds)
            Survey();

        DoToolbar(new Rect(inRect.x, inRect.y, inRect.width, ToolbarHeight - 4f));

        var tableRect = new Rect(inRect.x, inRect.y + ToolbarHeight, inRect.width, inRect.height - ToolbarHeight);
        DoHeader(new Rect(tableRect.x, tableRect.y, tableRect.width - 16f, HeaderHeight));

        var outRect = new Rect(tableRect.x, tableRect.y + HeaderHeight, tableRect.width, tableRect.height - HeaderHeight);
        List<FloraRow> shown = rows.Where(r => search.filter.Matches(r.def.label)
            || (r.Product != null && search.filter.Matches(r.Product.label))).ToList();
        search.noResultsMatched = shown.Count == 0 && search.filter.Active;

        if (shown.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(outRect.x, outRect.y, outRect.width, RowHeight * 2), "Ordo_FloraTab_None".Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return;
        }

        // Rows scrolled out of view still have rects, so only a mouse inside the list can hover one
        bool mouseInList = Mouse.IsOver(outRect);
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, shown.Count * RowHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        for (int i = 0; i < shown.Count; i++)
        {
            var rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawLightHighlight(rowRect);
            if (mouseInList && Mouse.IsOver(rowRect))
            {
                Widgets.DrawHighlight(rowRect);
                hoveredRow = shown[i];
                hoveredFrame = Time.frameCount;
            }
            DoRow(rowRect, shown[i]);
        }
        Widgets.EndScrollView();
    }

    // Map drawing happens in the update loop, not OnGUI, so the hover is remembered by frame and drawn here. A row stays
    // highlighted for one frame after the mouse leaves, which is not noticeable.
    public override void WindowUpdate()
    {
        base.WindowUpdate();
        if (hoveredRow == null || Time.frameCount - hoveredFrame > 1 || surveyedMap != Find.CurrentMap)
            return;
        FloraHighlight.Draw(hoveredRow, surveyedMap);
    }

    private void Survey()
    {
        surveyedMap = Find.CurrentMap;
        lastSurvey = Time.realtimeSinceStartup;
        // An area that was deleted, or belongs to another map, is dropped
        if (filter.area != null && (surveyedMap == null || !surveyedMap.areaManager.AllAreas.Contains(filter.area)))
            filter.area = null;
        rows = surveyedMap == null ? new List<FloraRow>() : FloraSurvey.Build(surveyedMap, filter);
        Sort();
    }

    private void Sort()
    {
        Func<FloraRow, float> key = sortBy switch
        {
            SortColumn.Count => r => r.plants.Count,
            SortColumn.Ready => r => r.ready,
            SortColumn.Next => r => r.nextReadyDays,
            SortColumn.Yield => r => r.yieldNow,
            SortColumn.Risk => r => r.AtRisk,
            SortColumn.Near => r => r.nearest,
            SortColumn.Marked => r => r.marked,
            _ => null,
        };
        IOrderedEnumerable<FloraRow> sorted = key == null
            ? (sortDescending ? rows.OrderByDescending(r => r.def.label) : rows.OrderBy(r => r.def.label))
            : (sortDescending ? rows.OrderByDescending(key) : rows.OrderBy(key));
        rows = sorted.ThenBy(r => r.def.label).ToList();
    }

    private void DoToolbar(Rect rect)
    {
        float x = rect.x;
        bool changed = false;
        changed |= Toggle(ref x, rect, "Ordo_FloraTab_IncludeTrees", ref filter.includeTrees);
        changed |= Toggle(ref x, rect, "Ordo_FloraTab_IncludeCultivated", ref filter.includeCultivated);
        changed |= Toggle(ref x, rect, "Ordo_FloraTab_OnlyReady", ref filter.onlyReady);
        if (changed)
            Survey();

        AreaButton(ref x, rect);
        DistanceButton(ref x, rect);

        float searchWidth = Mathf.Min(200f, rect.xMax - x - 10f);
        search.OnGUI(new Rect(rect.xMax - searchWidth, rect.y + (rect.height - QuickSearchWidget.WidgetHeight) / 2f,
            searchWidth, QuickSearchWidget.WidgetHeight));
    }

    private static bool Toggle(
        ref float x,
        Rect bar,
        string key,
        ref bool value)
    {
        string label = key.Translate();
        float width = Text.CalcSize(label).x + 34f;
        var rect = new Rect(x, bar.y, width, bar.height);
        bool before = value;
        Widgets.CheckboxLabeled(rect, label, ref value);
        TooltipHandler.TipRegion(rect, (key + "Tip").Translate());
        x += width + 16f;
        return before != value;
    }

    private void AreaButton(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, 150f, bar.height);
        x += rect.width + 8f;
        string current = filter.area?.Label ?? "Ordo_FloraTab_Anywhere".Translate();
        TooltipHandler.TipRegion(rect, "Ordo_FloraTab_AreaTip".Translate());
        if (!Widgets.ButtonText(rect, "Ordo_FloraTab_Area".Translate(current).Truncate(rect.width - 10f)))
            return;

        var options = new List<FloatMenuOption>
        {
            new("Ordo_FloraTab_Anywhere".Translate(), () => SetArea(null)),
        };
        if (surveyedMap != null)
        {
            Area home = surveyedMap.areaManager.Home;
            options.Add(new FloatMenuOption(home.Label, () => SetArea(home)));
            foreach (Area area in surveyedMap.areaManager.AllAreas)
            {
                if (area != home && area.AssignableAsAllowed())
                    options.Add(new FloatMenuOption(area.Label, () => SetArea(area)));
            }
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SetArea(Area area)
    {
        filter.area = area;
        Survey();
    }

    private void DistanceButton(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, 130f, bar.height);
        x += rect.width + 8f;
        string current = filter.maxDistance > 0
            ? "Ordo_FloraTab_Cells".Translate(filter.maxDistance).ToString()
            : "Ordo_FloraTab_AnyDistance".Translate().ToString();
        TooltipHandler.TipRegion(rect, "Ordo_FloraTab_WithinTip".Translate());
        if (!Widgets.ButtonText(rect, "Ordo_FloraTab_Within".Translate(current).Truncate(rect.width - 10f)))
            return;

        var options = DistanceChoices.Select(cells => new FloatMenuOption(
            cells > 0 ? "Ordo_FloraTab_Cells".Translate(cells).ToString() : "Ordo_FloraTab_AnyDistance".Translate().ToString(),
            () =>
            {
                filter.maxDistance = cells;
                Survey();
            })).ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void DoHeader(Rect rect)
    {
        float x = rect.x + IconSize + 8f;
        HeaderCell(ref x, rect, LabelWidth - IconSize - 8f, "Ordo_FloraTab_Plant", SortColumn.Label, null);
        Text.Anchor = TextAnchor.LowerLeft;
        Widgets.Label(new Rect(x, rect.y, ProductWidth, rect.height), "Ordo_FloraTab_Yields".Translate());
        x += ProductWidth;
        HeaderCell(ref x, rect, NumberWidth, "Ordo_FloraTab_Count", SortColumn.Count, "Ordo_FloraTab_CountTip");
        HeaderCell(ref x, rect, NumberWidth, "Ordo_FloraTab_Ready", SortColumn.Ready, "Ordo_FloraTab_ReadyTip");
        HeaderCell(ref x, rect, NextWidth, "Ordo_FloraTab_Next", SortColumn.Next, "Ordo_FloraTab_NextTip");
        HeaderCell(ref x, rect, YieldWidth, "Ordo_FloraTab_Yield", SortColumn.Yield, "Ordo_FloraTab_YieldTip");
        HeaderCell(ref x, rect, NumberWidth, "Ordo_FloraTab_Risk", SortColumn.Risk, "Ordo_FloraTab_RiskTip");
        HeaderCell(ref x, rect, NumberWidth, "Ordo_FloraTab_Near", SortColumn.Near, "Ordo_FloraTab_NearTip");
        HeaderCell(ref x, rect, NumberWidth, "Ordo_FloraTab_Marked", SortColumn.Marked, "Ordo_FloraTab_MarkedTip");
        Text.Anchor = TextAnchor.UpperLeft;
        Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
    }

    private void HeaderCell(
        ref float x,
        Rect bar,
        float width,
        string key,
        SortColumn column,
        string tipKey)
    {
        var rect = new Rect(x, bar.y, width, bar.height);
        x += width;
        Text.Anchor = column == SortColumn.Label ? TextAnchor.LowerLeft : TextAnchor.LowerCenter;
        string label = key.Translate();
        if (sortBy == column)
            label += sortDescending ? " ▼" : " ▲";
        Widgets.Label(rect, label);
        if (tipKey != null)
            TooltipHandler.TipRegion(rect, tipKey.Translate());
        Widgets.DrawHighlightIfMouseover(rect);
        if (Widgets.ButtonInvisible(rect))
        {
            if (sortBy == column)
                sortDescending = !sortDescending;
            else
            {
                sortBy = column;
                // Soonest and nearest first; most first for counts
                sortDescending = column is not (SortColumn.Label or SortColumn.Next or SortColumn.Near);
            }
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            Sort();
        }
    }

    private void DoRow(
        Rect rect,
        FloraRow row)
    {
        float x = rect.x;
        Text.Anchor = TextAnchor.MiddleLeft;

        // The plant: click to jump to the next one
        var plantRect = new Rect(x, rect.y, LabelWidth, rect.height);
        Widgets.ThingIcon(new Rect(x + 2f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize), row.def);
        Widgets.Label(new Rect(x + IconSize + 8f, rect.y, LabelWidth - IconSize - 8f, rect.height),
            row.def.LabelCap.Truncate(LabelWidth - IconSize - 8f));
        Widgets.DrawHighlightIfMouseover(plantRect);
        TooltipHandler.TipRegion(plantRect, () => row.def.LabelCap + "\n\n" + row.def.description + "\n\n"
            + "Ordo_FloraTab_JumpTip".Translate(row.def.label), row.def.GetHashCode());
        if (Widgets.ButtonInvisible(plantRect))
            JumpToNext(row);
        x += LabelWidth;

        // What it gives
        if (row.Product != null)
        {
            Widgets.ThingIcon(new Rect(x, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize), row.Product);
            var productRect = new Rect(x + IconSize + 6f, rect.y, ProductWidth - IconSize - 10f, rect.height);
            Widgets.Label(productRect, row.Product.LabelCap.Truncate(productRect.width));
            var cellRect = new Rect(x, rect.y, ProductWidth, rect.height);
            Widgets.DrawHighlightIfMouseover(cellRect);
            TooltipHandler.TipRegion(cellRect, row.Product.LabelCap + "\n\n" + row.Product.description);
            if (Widgets.ButtonInvisible(cellRect))
                Find.WindowStack.Add(new Dialog_InfoCard(row.Product));
        }
        x += ProductWidth;

        Text.Anchor = TextAnchor.MiddleCenter;
        NumberCell(ref x, rect, NumberWidth, row.plants.Count.ToString(), Color.white, null);
        NumberCell(ref x, rect, NumberWidth, row.ready.ToString(), row.ready > 0 ? Color.green : Color.gray, null);
        NumberCell(ref x, rect, NextWidth, NextText(row), float.IsPositiveInfinity(row.nextReadyDays) ? Color.gray : Color.white,
            NextTip(row));
        NumberCell(ref x, rect, YieldWidth, row.yieldNow > 0f ? Mathf.RoundToInt(row.yieldNow).ToString() : "-",
            row.yieldNow > 0f ? Color.white : Color.gray, null);
        NumberCell(ref x, rect, NumberWidth, row.AtRisk > 0 ? row.AtRisk.ToString() : "-", row.AtRisk > 0 ? RiskColor : Color.gray,
            row.AtRisk > 0 ? RiskTip(row) : null);
        NumberCell(ref x, rect, NumberWidth, float.IsPositiveInfinity(row.nearest) ? "-" : Mathf.RoundToInt(row.nearest).ToString(),
            Color.white, "Ordo_FloraTab_NearRowTip".Translate(Mathf.RoundToInt(row.nearest), row.def.label));
        NumberCell(ref x, rect, NumberWidth, row.marked.ToString(), row.marked > 0 ? Color.yellow : Color.gray, null);
        Text.Anchor = TextAnchor.UpperLeft;

        // Designate every ready plant of this kind, or cancel
        x += 6f;
        int unmarkedReady = row.plants.Count(p => p.HarvestableNow
            && surveyedMap.designationManager.DesignationOn(p, DesignationDefOf.HarvestPlant) == null);
        var harvestRect = new Rect(x, rect.y + 3f, HarvestButtonWidth - 6f, rect.height - 6f);
        if (unmarkedReady > 0)
        {
            TooltipHandler.TipRegion(harvestRect, "Ordo_FloraTab_HarvestAllTip".Translate(row.def.label));
            if (Widgets.ButtonText(harvestRect, "Ordo_FloraTab_HarvestAll".Translate()))
                HarvestAll(row);
        }
        x += HarvestButtonWidth;

        var cancelRect = new Rect(x, rect.y + 3f, CancelButtonWidth - 6f, rect.height - 6f);
        if (row.marked > 0)
        {
            TooltipHandler.TipRegion(cancelRect, "Ordo_FloraTab_CancelTip".Translate(row.def.label));
            if (Widgets.ButtonText(cancelRect, "Ordo_FloraTab_Cancel".Translate()))
                CancelAll(row);
        }
    }

    private static string Period(float days) =>
        Mathf.Max(1, Mathf.RoundToInt(days * GenDate.TicksPerDay)).ToStringTicksToPeriod(allowSeconds: false, shortForm: true);

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

    private static void NumberCell(
        ref float x,
        Rect row,
        float width,
        string text,
        Color color,
        string tip)
    {
        var rect = new Rect(x, row.y, width, row.height);
        GUI.color = color;
        Widgets.Label(rect, text);
        GUI.color = Color.white;
        if (tip != null)
            TooltipHandler.TipRegion(rect, tip);
        x += width;
    }

    // Cycles through the kind's plants: at risk first, then ready, then by position
    private void JumpToNext(FloraRow row)
    {
        List<Plant> order = row.plants
            .Where(p => p.Spawned)
            .OrderByDescending(p => row.atRisk.Contains(p))
            .ThenByDescending(p => p.HarvestableNow)
            .ThenBy(p => p.Position.z)
            .ThenBy(p => p.Position.x)
            .ToList();
        if (order.Count == 0)
            return;
        nextJumpIndex.TryGetValue(row.def, out int index);
        Plant target = order[index % order.Count];
        nextJumpIndex[row.def] = (index + 1) % order.Count;
        CameraJumper.TryJumpAndSelect(target);
    }

    private void HarvestAll(FloraRow row)
    {
        int count = FloraDesignations.HarvestReady(row, surveyedMap);
        if (count == 0)
            return;
        (row.IsTree ? SoundDefOf.Designate_HarvestWood : SoundDefOf.Designate_HarvestPlants).PlayOneShotOnCamera();
        Messages.Message("Ordo_FloraTab_Designated".Translate(count + " " + row.def.label), MessageTypeDefOf.SilentInput,
            historical: false);
        Survey();
    }

    private void CancelAll(FloraRow row)
    {
        FloraDesignations.Cancel(row, surveyedMap);
        SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
        Survey();
    }
}
