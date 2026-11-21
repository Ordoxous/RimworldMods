using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.FloraTab;

// The Resources main tab: a page for the map's harvestable wild plants and one for its ore, each a table of kinds, like
// Wildlife is for animals. The area, distance and search are shared by both pages.
public class MainTabWindow_Flora : MainTabWindow
{
    private const float ToolbarHeight = 32f;
    private const float RefreshSeconds = 1f;

    private static readonly int[] DistanceChoices = { 0, 25, 50, 75, 100, 150, 200 };

    // Kept across openings, as the Wildlife tab keeps its sort
    private static readonly IResourcePage[] Pages = { new PlantsPage(), new MineralsPage() };
    private static int currentPage;
    private static Area area;
    private static int maxDistance;

    // Set by a page's buttons, so the window surveys again on its next frame
    private static bool surveyRequested;

    private readonly QuickSearchWidget search = new();
    private List<TabRecord> tabs;
    private Map surveyedMap;
    private float lastSurvey = -999f;

    private static IResourcePage Page => Pages[currentPage];

    public override Vector2 RequestedTabSize => new(
        Page.Width + 70f,
        Mathf.Max(260f, TabDrawer.TabHeight + ToolbarHeight + TablePage<object>.HeaderHeight
            + TablePage<object>.RowHeight * Mathf.Clamp(Page.RowCount, 3, 16) + Margin * 2 + 16f));

    public static void SurveyNow() => surveyRequested = true;

    public override void PostOpen()
    {
        base.PostOpen();
        Survey();
        SetInitialSizeAndPosition();
    }

    public override void PostClose()
    {
        base.PostClose();
        foreach (IResourcePage page in Pages)
            page.Clear();
    }

    public override void DoWindowContents(Rect inRect)
    {
        if (surveyRequested || Find.CurrentMap != surveyedMap || Time.realtimeSinceStartup - lastSurvey > RefreshSeconds)
            Survey();

        // The tabs hang above the rect they're given, inside the window
        inRect.yMin += TabDrawer.TabHeight;
        tabs ??= Pages.Select((page, i) => new TabRecord(page.LabelKey.Translate(), () => SwitchTo(i), () => currentPage == i))
            .ToList();
        Widgets.DrawMenuSection(inRect);
        TabDrawer.DrawTabs(inRect, tabs);
        inRect = inRect.ContractedBy(6f);

        DoToolbar(new Rect(inRect.x, inRect.y, inRect.width, ToolbarHeight - 4f));
        search.noResultsMatched = Page.Draw(
            new Rect(inRect.x, inRect.y + ToolbarHeight, inRect.width, inRect.height - ToolbarHeight), search.filter);
    }

    public override void WindowUpdate()
    {
        base.WindowUpdate();
        Page.WindowUpdate(Find.CurrentMap);
    }

    private void SwitchTo(int page)
    {
        if (page == currentPage)
            return;
        currentPage = page;
        Survey();
        SetInitialSizeAndPosition();
    }

    private void Survey()
    {
        surveyRequested = false;
        surveyedMap = Find.CurrentMap;
        lastSurvey = Time.realtimeSinceStartup;
        // An area that was deleted, or belongs to another map, is dropped
        if (area != null && (surveyedMap == null || !surveyedMap.areaManager.AllAreas.Contains(area)))
            area = null;
        Page.Survey(surveyedMap, area, maxDistance);
    }

    private void DoToolbar(Rect rect)
    {
        float x = rect.x;
        if (Page.DoToggles(ref x, rect))
            Survey();

        AreaButton(ref x, rect);
        DistanceButton(ref x, rect);

        float searchWidth = Mathf.Min(200f, rect.xMax - x - 10f);
        search.OnGUI(new Rect(rect.xMax - searchWidth, rect.y + (rect.height - QuickSearchWidget.WidgetHeight) / 2f,
            searchWidth, QuickSearchWidget.WidgetHeight));
    }

    private void AreaButton(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, 150f, bar.height);
        x += rect.width + 8f;
        string current = area?.Label ?? "Ordo_FloraTab_Anywhere".Translate();
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
            foreach (Area other in surveyedMap.areaManager.AllAreas)
            {
                if (other != home && other.AssignableAsAllowed())
                    options.Add(new FloatMenuOption(other.Label, () => SetArea(other)));
            }
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SetArea(Area chosen)
    {
        area = chosen;
        Survey();
    }

    private void DistanceButton(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, 160f, bar.height);
        x += rect.width + 8f;
        string current = maxDistance > 0
            ? "Ordo_FloraTab_Cells".Translate(maxDistance).ToString()
            : "Ordo_FloraTab_AnyDistance".Translate().ToString();
        TooltipHandler.TipRegion(rect, "Ordo_FloraTab_WithinTip".Translate());
        if (!Widgets.ButtonText(rect, "Ordo_FloraTab_Within".Translate(current).Truncate(rect.width - 10f)))
            return;

        var options = DistanceChoices.Select(cells => new FloatMenuOption(
            cells > 0 ? "Ordo_FloraTab_Cells".Translate(cells).ToString() : "Ordo_FloraTab_AnyDistance".Translate().ToString(),
            () =>
            {
                maxDistance = cells;
                Survey();
            })).ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
