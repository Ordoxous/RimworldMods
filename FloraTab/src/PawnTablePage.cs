using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.FloraTab;

// A page that moves in one of the game's pawn table tabs, such as Animals or Wildlife, whose button this mod hides and
// whose hotkey opens the page. When a mod has replaced that tab's window with its own pawn table window, such as Animal
// Tab, the page hosts that window, filters and all. Otherwise it draws the game's own table. Either way the table def is
// the shared one, so columns other mods add show here too, and the search box filters it by name, or by kind as "hen" or
// "chicken".
public abstract class PawnTablePage : IResourcePage
{
    // The window's tabs, toolbar, margins and scroll bar, which the table must leave room for
    private const float WindowChromeWidth = 70f;
    private const float WindowChromeHeight = 35f + 32f + 32f + 36f + 12f + 16f;

    // A pawn table window's own margin, inside which it draws; the page's rect is already inside one
    private const float HostedMargin = 6f;

    private PawnTable table;
    private int tableScreenWidth;
    private int tableScreenHeight;
    protected Map map;
    private QuickSearchFilter search;
    private string searchedText = "";

    private bool hostedLooked;
    private MainTabWindow_PawnTable hosted;
    private PawnTable hostedTable;

    public abstract string LabelKey { get; }
    protected abstract string NoneKey { get; }

    // The main button this page takes over, and the game's own window for it
    public abstract string ButtonDefName { get; }
    protected abstract Type VanillaWindowClass { get; }
    protected abstract PawnTableDef TableDef { get; }

    // What the game's own window lists
    protected abstract IEnumerable<Pawn> VanillaPawns(Map map);

    // The page's own toolbar buttons, drawn only when it isn't hosting a window, which has its own
    protected virtual void DoVanillaToolbar(
        ref float x,
        Rect bar)
    {
    }

    // A table's width includes its own scroll bar, which the window counts in already
    public float Width => HostedWindow != null ? HostedSize.x - HostedMargin * 2 - 16f : Table.Size.x - 16f;
    public float Height => HostedWindow != null ? HostedSize.y - HostedMargin * 2 : Table.Size.y;
    public bool UsesPlaceFilters => false;

    // The pawns the table lists now
    public List<Pawn> Listed => CurrentTable.PawnsListForReading;

    // The window a mod gave the button in place of the game's, if it is a pawn table window this page can host. It is
    // the button def's own instance, so a mod that keeps a static reference to its window, as Animal Tab's filters do,
    // finds this one.
    public MainTabWindow_PawnTable HostedWindow
    {
        get
        {
            if (hostedLooked)
                return hosted;
            hostedLooked = true;
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail(ButtonDefName);
            Type windowClass = def?.tabWindowClass;
            if (windowClass != null && windowClass != VanillaWindowClass
                && typeof(MainTabWindow_PawnTable).IsAssignableFrom(windowClass))
                hosted = def.TabWindow as MainTabWindow_PawnTable;
            return hosted;
        }
    }

    private Vector2 HostedSize
    {
        get
        {
            _ = HostedTable;
            return hosted.RequestedTabSize;
        }
    }

    // The hosted window's table, made as its own PostOpen would. The window makes it again when the screen size or, for
    // Animal Tab, its filter bar changes, so each new one is fitted below the tabs and toolbar and given the search.
    private PawnTable HostedTable
    {
        get
        {
            MainTabWindow_PawnTable window = HostedWindow;
            window.table ??= window.CreateTable();
            if (window.table == hostedTable)
                return hostedTable;

            hostedTable = window.table;
            Func<IEnumerable<Pawn>> own = hostedTable.pawnsGetter;
            hostedTable.pawnsGetter = () => Searched(own());
            hostedTable.SetMinMaxSize(window.PawnTableDef.minWidth, UI.screenWidth - (int)WindowChromeWidth, 0,
                (int)(UI.screenHeight - WindowChromeHeight - window.ExtraTopSpace - window.ExtraBottomSpace));
            return hostedTable;
        }
    }

    // The game's table, made as its window makes it, and made again when the screen size changes
    private PawnTable Table
    {
        get
        {
            if (table == null || tableScreenWidth != UI.screenWidth || tableScreenHeight != UI.screenHeight)
            {
                tableScreenWidth = UI.screenWidth;
                tableScreenHeight = UI.screenHeight;
                table = (PawnTable)Activator.CreateInstance(TableDef.workerClass, TableDef,
                    (Func<IEnumerable<Pawn>>)(() => Searched(map == null ? Enumerable.Empty<Pawn>() : VanillaPawns(map))),
                    UI.screenWidth - (int)WindowChromeWidth, (int)(UI.screenHeight - WindowChromeHeight));
            }
            return table;
        }
    }

    private PawnTable CurrentTable => HostedWindow != null ? HostedTable : Table;

    public bool DoToggles(
        ref float x,
        Rect bar)
    {
        if (HostedWindow == null)
            DoVanillaToolbar(ref x, bar);
        return false;
    }

    // Pawns come and go while the window is open, and the game only tells pawn table windows that are in the window
    // stack, so the table is read again each survey
    public void Survey(
        Map map,
        Area area,
        int maxDistance)
    {
        this.map = map;
        if (HostedWindow != null)
        {
            _ = HostedTable;
            HostedWindow.Notify_PawnsChanged();
        }
        else
            Table.SetDirty();
    }

    public bool Draw(
        Rect rect,
        QuickSearchFilter search)
    {
        SearchFor(search);
        if (HostedWindow != null)
            HostedWindow.DoWindowContents(new Rect(rect.x, rect.y, rect.width, Height));
        else
            Table.PawnTableOnGUI(rect.position);

        if (CurrentTable.PawnsListForReading.Count > 0)
            return false;
        Text.Anchor = TextAnchor.MiddleCenter;
        GUI.color = Color.gray;
        Widgets.Label(new Rect(rect.x, rect.y + CurrentTable.HeaderHeight, rect.width, 60f), NoneKey.Translate());
        GUI.color = Color.white;
        Text.Anchor = TextAnchor.UpperLeft;
        return search.Active;
    }

    public void SearchFor(QuickSearchFilter search)
    {
        this.search = search;
        if (search.Text == searchedText)
            return;
        searchedText = search.Text;
        CurrentTable.SetDirty();
    }

    public void WindowUpdate(Map currentMap)
    {
    }

    public void Clear()
    {
        map = null;
        table?.SetDirty();
    }

    private IEnumerable<Pawn> Searched(IEnumerable<Pawn> pawns)
    {
        if (search == null || !search.Active)
            return pawns;
        return pawns.Where(p => search.Matches(p.LabelShort) || search.Matches(p.KindLabel) || search.Matches(p.def.label));
    }
}

// The colony's animals, from the Animals tab
public class AnimalsPage : PawnTablePage
{
    private const float SlaughterButtonWidth = 220f;

    public override string LabelKey => "Ordo_FloraTab_AnimalsPage";
    protected override string NoneKey => "Ordo_FloraTab_NoAnimals";
    public override string ButtonDefName => "Animals";
    protected override Type VanillaWindowClass => typeof(MainTabWindow_Animals);
    protected override PawnTableDef TableDef => PawnTableDefOf.Animals;

    protected override IEnumerable<Pawn> VanillaPawns(Map map) => map.mapPawns.ColonyAnimals;

    protected override void DoVanillaToolbar(
        ref float x,
        Rect bar)
    {
        var rect = new Rect(x, bar.y, SlaughterButtonWidth, bar.height);
        x += rect.width + 8f;
        if (map != null && Widgets.ButtonText(rect, "ManageAutoSlaughter".Translate()))
            Find.WindowStack.Add(new Dialog_AutoSlaughter(map));
    }
}

// The wild animals in view, from the Wildlife tab, for hunting and taming
public class WildlifePage : PawnTablePage
{
    public override string LabelKey => "Ordo_FloraTab_WildlifePage";
    protected override string NoneKey => "Ordo_FloraTab_NoWildlife";
    public override string ButtonDefName => "Wildlife";
    protected override Type VanillaWindowClass => typeof(MainTabWindow_Wildlife);
    protected override PawnTableDef TableDef => PawnTableDefOf.Wildlife;

    // As MainTabWindow_Wildlife lists them
    protected override IEnumerable<Pawn> VanillaPawns(Map map) =>
        map.mapPawns.AllPawns.Where(p => p.Spawned && (p.Faction == null || p.Faction == Faction.OfInsects)
            && p.AnimalOrWildMan() && !p.Position.Fogged(p.Map) && !p.IsPrisonerInPrisonCell());
}
