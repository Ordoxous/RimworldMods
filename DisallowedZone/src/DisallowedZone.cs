using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.DisallowedZone;

/// <summary>
/// A map's disallowed zone: the painted cells, whether the zone is switched on, and the
/// effective areas (each allowed area minus the zone) handed to pawns while it is on.
/// </summary>
public class DisallowedZone : MapComponent, ICellBoolGiver
{
    public static readonly Color ZoneColor = new(0.9f, 0.15f, 0.1f);

    /// <summary>The zones switched on, on any map. Usually empty, so the area patch exits at once.</summary>
    public static readonly List<DisallowedZone> ActiveZones = new();

    private BoolGrid grid;
    private bool active;
    private CellBoolDrawer drawer;

    // Effective areas by the pawn's own allowed area; the unrestricted one is kept separately.
    private readonly Dictionary<Area, EffectiveArea> effectiveAreas = new();
    private EffectiveArea unrestricted;

    public DisallowedZone(Map map)
        : base(map)
    {
        grid = new BoolGrid(map);
    }

    public bool Active => active;

    public int CellCount => grid.TrueCount;

    public bool this[int index] => grid[index];

    public bool this[IntVec3 c] => grid[map.cellIndices.CellToIndex(c)];

    public Color Color => ZoneColor;

    private CellBoolDrawer Drawer => drawer ??= new CellBoolDrawer(this, map.Size.x, map.Size.z, 3650, 0.4f);

    public static DisallowedZone For(Map map) => map?.GetComponent<DisallowedZone>();

    /// <summary>The active zone on <paramref name="map" />, found without a component search.</summary>
    internal static DisallowedZone ActiveOn(Map map)
    {
        var zones = ActiveZones;
        for (var i = 0; i < zones.Count; i++)
            if (zones[i].map == map)
                return zones[i];
        return null;
    }

    public bool GetCellBool(int index) => grid[index];

    public Color GetCellExtraColor(int index) => Color.white;

    public void Set(
        IntVec3 c,
        bool value
    )
    {
        var index = map.cellIndices.CellToIndex(c);
        if (grid[index] == value)
            return;
        grid[index] = value;
        Drawer.SetDirty();
        foreach (var area in effectiveAreas.Values)
            area.UpdateCell(index);
        unrestricted?.UpdateCell(index);
        if (active && grid.TrueCount == 0)
            SetActive(false);
    }

    public void SetActive(bool value)
    {
        if (value && grid.TrueCount == 0)
        {
            Messages.Message("Ordo_DisallowedZone_Empty".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }
        if (active == value)
            return;
        active = value;
        Register();
        if (active)
            InterruptJobsInside();
        Messages.Message(
            (active ? "Ordo_DisallowedZone_Activated" : "Ordo_DisallowedZone_Deactivated").Translate(),
            MessageTypeDefOf.SilentInput,
            historical: false
        );
    }

    public void Toggle() => SetActive(!active);

    /// <summary>
    /// The area a pawn restricted to <paramref name="baseArea" /> (null: unrestricted) may use while
    /// the zone is on.
    /// </summary>
    public EffectiveArea EffectiveAreaFor(Area baseArea)
    {
        if (baseArea == null)
            return unrestricted ??= new EffectiveArea(map.areaManager, this, null);
        if (!effectiveAreas.TryGetValue(baseArea, out var area))
        {
            area = new EffectiveArea(map.areaManager, this, baseArea);
            effectiveAreas[baseArea] = area;
        }
        return area;
    }

    internal void Notify_BaseAreaCellChanged(
        Area baseArea,
        IntVec3 c
    )
    {
        if (effectiveAreas.TryGetValue(baseArea, out var area))
            area.UpdateCell(map.cellIndices.CellToIndex(c));
    }

    internal void Notify_BaseAreaChanged(Area baseArea)
    {
        if (effectiveAreas.TryGetValue(baseArea, out var area))
            area.Recompute();
    }

    internal void Notify_BaseAreaRemoved(Area baseArea)
    {
        if (!effectiveAreas.TryGetValue(baseArea, out var area))
            return;
        effectiveAreas.Remove(baseArea);
        area.Dispose();
    }

    /// <summary>
    /// What vanilla does when a pawn's allowed area is changed: end jobs that target cells the pawn may
    /// no longer use. Called when the zone is switched on or grows while on.
    /// </summary>
    internal void InterruptJobsInside()
    {
        if (!active)
            return;
        var pawns = map.mapPawns.AllPawnsSpawned;
        for (var i = pawns.Count - 1; i >= 0; i--)
        {
            var pawn = pawns[i];
            var job = pawn.jobs?.curJob;
            if (job == null || pawn.playerSettings == null || pawn.Drafted)
                continue;
            if (!ForbidUtility.CaresAboutForbidden(pawn, cellTarget: true))
                continue;
            if (pawn.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap is EffectiveArea area && job.AnyTargetOutsideArea(area))
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }
    }

    public void MarkForDraw()
    {
        if (map == Find.CurrentMap && !Find.ScreenshotModeHandler.Active)
            Drawer.MarkForDraw();
    }

    public override void MapComponentUpdate()
    {
        if (active)
            MarkForDraw();
        drawer?.CellBoolDrawerUpdate();
    }

    public override void MapComponentOnGUI()
    {
        if (KeyBindings.Ordo_ToggleDisallowedZone != null && KeyBindings.Ordo_ToggleDisallowedZone.KeyDownEvent)
        {
            Toggle();
            Event.current.Use();
        }
    }

    public override void FinalizeInit()
    {
        ActiveZones.RemoveAll(zone => !Find.Maps.Contains(zone.map));
        Register();
    }

    public override void MapRemoved()
    {
        ActiveZones.Remove(this);
        foreach (var area in effectiveAreas.Values)
            area.Dispose();
        effectiveAreas.Clear();
        unrestricted?.Dispose();
        unrestricted = null;
    }

    public override void ExposeData()
    {
        Scribe_Deep.Look(ref grid, "grid");
        Scribe_Values.Look(ref active, "active");
        if (Scribe.mode == LoadSaveMode.PostLoadInit && grid == null)
            grid = new BoolGrid(map);
    }

    private void Register()
    {
        ActiveZones.Remove(this);
        if (active)
            ActiveZones.Add(this);
    }
}

[DefOf]
public static class KeyBindings
{
    public static KeyBindingDef Ordo_ToggleDisallowedZone;

    static KeyBindings() => DefOfHelper.EnsureInitializedInCtor(typeof(KeyBindings));
}
