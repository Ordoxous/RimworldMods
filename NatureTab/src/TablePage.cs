using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Ordo.NatureTab;

// One column of a page's table: its header, width, how it sorts and how it draws a row's cell
public sealed class Column<TRow>
{
    // Null for a column without a header, such as the row's buttons
    public string labelKey;
    public string tipKey;
    public float width;

    // Space before the header label, to line it up with the row's text after an icon
    public float headerIndent;
    public bool leftAligned;

    // Sorts by a number, or else by text; a column with neither doesn't sort
    public Func<TRow, float> sortValue;
    public Func<TRow, string> sortText;

    // Most first, rather than least or A to Z first, when the column is first clicked
    public bool descendingFirst;
    public Action<Rect, TRow> draw;

    public bool Sortable => sortValue != null || sortText != null;
}

// The window's view of one page, without its row type
public interface IResourcePage
{
    string LabelKey { get; }
    float Width { get; }

    // The height the page would like, below the toolbar
    float Height { get; }

    // Whether the window's area and distance filters apply to it
    bool UsesPlaceFilters { get; }

    // Draws the page's own toggles from x, and returns whether any changed
    bool DoToggles(
        ref float x,
        Rect bar);

    void Survey(
        Map map,
        Area area,
        int maxDistance);

    // Returns whether the search hid every row
    bool Draw(
        Rect rect,
        QuickSearchFilter search);

    void WindowUpdate(Map map);

    // Called when the page comes into view, as the window opens on it or switches to it
    void Shown();

    // Drops the rows, so a closed tab doesn't keep a map's things alive
    void Clear();

    // The sort, saved with the game by GameComponent_NatureTab, and back to the page's default for a new or loaded game
    void ExposeSort();

    void ResetSort();
}

// A sortable table of rows, one per kind of thing on the map: the header, scrolling, hover highlight and the shared cell
// drawing. Each page says what its rows and columns are.
public abstract class TablePage<TRow> : IResourcePage
    where TRow : class
{
    public const float RowHeight = 30f;
    public const float HeaderHeight = 30f;
    public const float IconSize = 24f;

    protected static readonly Color RiskColor = new(1f, 0.4f, 0.35f);

    protected List<TRow> rows = new();
    protected Map map;
    private Vector2 scroll;
    private TRow hoveredRow;
    private int hoveredFrame = -1;
    private Column<TRow> sortBy;
    private bool sortDescending;
    private List<Column<TRow>> columns;

    public abstract string LabelKey { get; }
    protected abstract string NoneKey { get; }
    public float Width => Columns.Sum(c => c.width);
    public float Height => HeaderHeight + RowHeight * Mathf.Clamp(rows.Count, 3, 16);
    public bool UsesPlaceFilters => true;

    protected List<Column<TRow>> Columns => columns ??= MakeColumns();

    protected abstract List<Column<TRow>> MakeColumns();

    protected abstract List<TRow> Build(
        Map map,
        Area area,
        int maxDistance);

    protected abstract bool Matches(
        TRow row,
        QuickSearchFilter search);

    // Ties in the sort go by this, A to Z
    protected abstract string Label(TRow row);

    protected abstract void DrawHighlight(
        TRow row,
        Map map);

    public abstract bool DoToggles(
        ref float x,
        Rect bar);

    // The column a page sorts by until the player picks another
    protected void SortByDefault(
        int column,
        bool descending)
    {
        defaultSortColumn = column;
        defaultSortDescending = descending;
        if (sortBy != null)
            return;
        sortBy = Columns[column];
        sortDescending = descending;
    }

    private int defaultSortColumn = -1;
    private bool defaultSortDescending;

    public void ResetSort()
    {
        sortBy = defaultSortColumn >= 0 ? Columns[defaultSortColumn] : null;
        sortDescending = defaultSortDescending;
        Sort();
    }

    // The column is kept by its header's key, so adding or reordering columns keeps it
    public void ExposeSort()
    {
        string key = sortBy?.labelKey;
        bool descending = sortDescending;
        Scribe_Values.Look(ref key, "sortBy");
        Scribe_Values.Look(ref descending, "sortDescending");
        if (Scribe.mode != LoadSaveMode.LoadingVars)
            return;
        Column<TRow> column = Columns.FirstOrDefault(c => c.Sortable && c.labelKey != null && c.labelKey == key);
        if (column == null)
            return;
        sortBy = column;
        sortDescending = descending;
        Sort();
    }

    public void Survey(
        Map map,
        Area area,
        int maxDistance)
    {
        this.map = map;
        rows = map == null ? new List<TRow>() : Build(map, area, maxDistance);
        Sort();
    }

    public void Shown()
    {
    }

    public void Clear()
    {
        rows = new List<TRow>();
        map = null;
        hoveredRow = null;
    }

    public bool Draw(
        Rect rect,
        QuickSearchFilter search)
    {
        DoHeader(new Rect(rect.x, rect.y, rect.width - 16f, HeaderHeight));

        var outRect = new Rect(rect.x, rect.y + HeaderHeight, rect.width, rect.height - HeaderHeight);
        List<TRow> shown = search.Active ? rows.Where(r => Matches(r, search)).ToList() : rows;
        if (shown.Count == 0)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(outRect.x, outRect.y, outRect.width, RowHeight * 2), NoneKey.Translate());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            return search.Active;
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
        return false;
    }

    // Map drawing happens in the update loop, not OnGUI, so the hover is remembered by frame and drawn here. A row stays
    // highlighted for one frame after the mouse leaves, which is not noticeable.
    public void WindowUpdate(Map currentMap)
    {
        if (hoveredRow == null || Time.frameCount - hoveredFrame > 1 || map != currentMap)
            return;
        DrawHighlight(hoveredRow, map);
    }

    private void Sort()
    {
        if (sortBy == null || !sortBy.Sortable)
        {
            rows = rows.OrderBy(Label).ToList();
            return;
        }
        IOrderedEnumerable<TRow> sorted;
        if (sortBy.sortValue != null)
            sorted = sortDescending ? rows.OrderByDescending(sortBy.sortValue) : rows.OrderBy(sortBy.sortValue);
        else
        {
            // Rows with no text, such as plants that give nothing, go last either way
            sorted = rows.OrderBy(r => string.IsNullOrEmpty(sortBy.sortText(r)));
            sorted = sortDescending ? sorted.ThenByDescending(sortBy.sortText) : sorted.ThenBy(sortBy.sortText);
        }
        rows = sorted.ThenBy(Label).ToList();
    }

    private void DoHeader(Rect rect)
    {
        float x = rect.x;
        foreach (Column<TRow> column in Columns)
        {
            var cell = new Rect(x, rect.y, column.width, rect.height);
            x += column.width;
            if (column.labelKey == null)
                continue;
            Text.Anchor = column.leftAligned ? TextAnchor.LowerLeft : TextAnchor.LowerCenter;
            string label = column.labelKey.Translate();
            if (sortBy == column)
                label += sortDescending ? " ▼" : " ▲";
            Widgets.Label(new Rect(cell.x + column.headerIndent, cell.y, cell.width - column.headerIndent, cell.height), label);
            if (column.tipKey != null)
                TooltipHandler.TipRegion(cell, column.tipKey.Translate());
            if (!column.Sortable)
                continue;
            Widgets.DrawHighlightIfMouseover(cell);
            if (Widgets.ButtonInvisible(cell))
            {
                if (sortBy == column)
                    sortDescending = !sortDescending;
                else
                {
                    sortBy = column;
                    sortDescending = column.descendingFirst;
                }
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                Sort();
            }
        }
        Text.Anchor = TextAnchor.UpperLeft;
        Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);
    }

    private void DoRow(
        Rect rect,
        TRow row)
    {
        float x = rect.x;
        foreach (Column<TRow> column in Columns)
        {
            Text.Anchor = column.leftAligned ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            column.draw(new Rect(x, rect.y, column.width, rect.height), row);
            x += column.width;
        }
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = Color.white;
    }

    // A column's cell: text in a color, with an optional tooltip
    protected static void NumberCell(
        Rect rect,
        string text,
        Color color,
        string tip = null)
    {
        GUI.color = color;
        Widgets.Label(rect, text);
        GUI.color = Color.white;
        if (tip != null)
            TooltipHandler.TipRegion(rect, tip);
    }

    protected static void CountCell(
        Rect rect,
        int count,
        Color color) =>
        NumberCell(rect, count.ToString(), count > 0 ? color : Color.gray);

    // A thing's icon and name; clicking does the action
    protected static void ThingCell(
        Rect rect,
        ThingDef def,
        string tip,
        Action clicked)
    {
        Widgets.ThingIcon(new Rect(rect.x + 2f, rect.y + (rect.height - IconSize) / 2f, IconSize, IconSize), def);
        var labelRect = new Rect(rect.x + IconSize + 8f, rect.y, rect.width - IconSize - 10f, rect.height);
        Widgets.Label(labelRect, def.LabelCap.Truncate(labelRect.width));
        Widgets.DrawHighlightIfMouseover(rect);
        TooltipHandler.TipRegion(rect, tip);
        if (Widgets.ButtonInvisible(rect))
            clicked();
    }

    // A row action button, drawn only when there's something for it to do
    protected static void ButtonCell(
        Rect rect,
        bool shown,
        string labelKey,
        string tip,
        Action clicked)
    {
        if (!shown)
            return;
        var button = new Rect(rect.x + 6f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
        TooltipHandler.TipRegion(button, tip);
        Text.Anchor = TextAnchor.MiddleCenter;
        if (Widgets.ButtonText(button, labelKey.Translate()))
            clicked();
    }

    protected static string DistanceText(float cells) =>
        float.IsPositiveInfinity(cells) ? "-" : Mathf.RoundToInt(cells).ToString();

    protected static string Period(float days) =>
        Mathf.Max(1, Mathf.RoundToInt(days * GenDate.TicksPerDay)).ToStringTicksToPeriod(allowSeconds: false, shortForm: true);

    // A toolbar checkbox with its label and tooltip; returns whether it changed
    public static bool Toggle(
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

    // Cycles a kind's things on the map: each call jumps to and selects the next one in the order given
    private readonly Dictionary<object, int> nextJumpIndex = new();

    protected void JumpToNext(
        object key,
        List<Thing> order)
    {
        if (order.Count == 0)
            return;
        nextJumpIndex.TryGetValue(key, out int index);
        Thing target = order[index % order.Count];
        nextJumpIndex[key] = (index + 1) % order.Count;
        CameraJumper.TryJumpAndSelect(target);
    }
}
