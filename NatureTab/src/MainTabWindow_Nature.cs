using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.NatureTab;

// The Nature main tab: a page for the map's harvestable wild plants and one for its ore, each a table of kinds, like
// Wildlife is for animals, and the Animals and Wildlife tabs moved in. The plants and minerals pages each have their own area
// and share the distance; the search is shared by all four.
public class MainTabWindow_Nature : MainTabWindow
{
    private const float ToolbarHeight = 32f;
    private const float RefreshSeconds = 1f;

    private static readonly int[] DistanceChoices = { 0, 25, 50, 75, 100, 150, 200 };

    // Kept across openings, as the Wildlife tab keeps its sort
    private static readonly IResourcePage[] Pages =
        { new PlantsPage(), new MineralsPage(), new AnimalsPage(), new WildlifePage() };
    private static int currentPage;
    private static int maxDistance;

    // Each page's own area
    private static readonly AreaChoice[] Areas = Pages.Select(_ => new AreaChoice()).ToArray();
    private static Area area;

    // Set by a page's buttons, so the window surveys again on its next frame
    private static bool surveyRequested;

    private readonly QuickSearchWidget search = new();
    private List<TabRecord> tabs;
    private Map surveyedMap;
    private float lastSurvey = -999f;

    public static IResourcePage Page => Pages[currentPage];

    // The current page's area on the map last surveyed, or null for anywhere
    public static Area CurrentArea => area;

    public override Vector2 RequestedTabSize => new(
        Page.Width + 70f,
        Mathf.Max(260f, TabDrawer.TabHeight + ToolbarHeight + Page.Height + Margin * 2 + 16f));

    public static void SurveyNow() => surveyRequested = true;

    // What a hidden button that moved in, such as Animals or Wildlife, and its hotkey do: open this window on that
    // button's page, or close it if that page is showing
    public static void TogglePageFor(MainButtonDef button)
    {
        int page = Array.FindIndex(Pages, p => p is PawnTablePage tablePage && tablePage.ButtonDefName == button.defName);
        if (page < 0)
            return;
        MainButtonDef def = DefDatabase<MainButtonDef>.GetNamed("Ordo_Nature");
        MainTabsRoot tabsRoot = Find.MainTabsRoot;
        if (tabsRoot.OpenTab != def)
        {
            currentPage = page;
            tabsRoot.SetCurrentTab(def);
        }
        else if (currentPage == page)
            tabsRoot.EscapeCurrentTab();
        else
            ((MainTabWindow_Nature)def.TabWindow).SwitchTo(page);
    }

    public override void PostOpen()
    {
        base.PostOpen();
        Page.Shown();
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

        // Grow or shrink with the page, as rows and animals come and go and Animal Tab's filter bar opens and closes
        Vector2 wanted = RequestedTabSize;
        if (Mathf.Abs(wanted.x - windowRect.width) > 1f || Mathf.Abs(wanted.y - windowRect.height) > 1f)
            SetInitialSizeAndPosition();

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
        DrawReach(Find.CurrentMap);
    }

    // While the Within button or one of its choices is hovered, the map shows the base's middle and, for a distance, the
    // circle it reaches. Like a row's hover, it's noted in OnGUI by frame and drawn in the update loop.
    private static int reachHoverFrame = -1;
    private static int reachHoverCells;

    private static void HoverReach(int cells)
    {
        reachHoverFrame = Time.frameCount;
        reachHoverCells = cells;
    }

    private static void DrawReach(Map map)
    {
        if (map == null || Time.frameCount - reachHoverFrame > 1)
            return;
        IntVec3 center = FloraSurvey.BaseCenter(map);
        GenDraw.DrawTargetHighlightWithLayer(center, AltitudeLayer.MetaOverlays);
        if (reachHoverCells <= 0)
            return;
        Vector3 at = center.ToVector3Shifted();
        at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
        GenDraw.DrawCircleOutline(at, reachHoverCells, SimpleColor.White);
    }

    public void SwitchTo(int page)
    {
        if (page == currentPage)
            return;
        currentPage = page;
        Page.Shown();
        Survey();
        SetInitialSizeAndPosition();
    }

    private void Survey()
    {
        surveyRequested = false;
        surveyedMap = Find.CurrentMap;
        lastSurvey = Time.realtimeSinceStartup;
        area = Areas[currentPage].On(surveyedMap);
        Page.Survey(surveyedMap, area, maxDistance);
    }

    private void DoToolbar(Rect rect)
    {
        float x = rect.x;
        if (Page.DoToggles(ref x, rect))
            Survey();

        if (Page.UsesPlaceFilters)
        {
            AreaButton(ref x, rect);
            DistanceButton(ref x, rect);
        }

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
        string current = area?.Label ?? "Ordo_NatureTab_Anywhere".Translate();
        TooltipHandler.TipRegion(rect, "Ordo_NatureTab_AreaTip".Translate());
        if (!Widgets.ButtonText(rect, "Ordo_NatureTab_Area".Translate(current).Truncate(rect.width - 10f)))
            return;

        var options = new List<FloatMenuOption>
        {
            new("Ordo_NatureTab_Anywhere".Translate(), () => SetArea(null)),
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

    public void SetArea(Area chosen)
    {
        Areas[currentPage].Set(chosen);
        Survey();
    }

    private void DistanceButton(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, 160f, bar.height);
        x += rect.width + 8f;
        string current = maxDistance > 0
            ? "Ordo_NatureTab_Cells".Translate(maxDistance).ToString()
            : "Ordo_NatureTab_AnyDistance".Translate().ToString();
        TooltipHandler.TipRegion(rect, "Ordo_NatureTab_WithinTip".Translate());
        if (Mouse.IsOver(rect))
            HoverReach(maxDistance);
        if (!Widgets.ButtonText(rect, "Ordo_NatureTab_Within".Translate(current).Truncate(rect.width - 10f)))
            return;

        var options = DistanceChoices.Select(cells => new FloatMenuOption(
            cells > 0 ? "Ordo_NatureTab_Cells".Translate(cells).ToString() : "Ordo_NatureTab_AnyDistance".Translate().ToString(),
            () =>
            {
                maxDistance = cells;
                Survey();
            },
            mouseoverGuiAction: _ => HoverReach(cells))).ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }
}
