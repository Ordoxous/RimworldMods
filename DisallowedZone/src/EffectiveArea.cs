using Unity.Collections;
using UnityEngine;
using Verse;

namespace Ordo.DisallowedZone;

/// <summary>
/// A pawn's allowed area with the disallowed zone taken out: the base area's cells (or every cell,
/// for unrestricted pawns) that aren't in the zone. Given to pawns in place of their own area while
/// the zone is on, so the game's allowed-area code (work, hauling, food, wandering, seeking the
/// allowed area, path costs) applies to it unchanged.
/// </summary>
/// <remarks>
/// Not added to the area manager, so it isn't listed, assignable or saved. The path finder only
/// keeps bit grids for the manager's areas, so this keeps its own (see <see cref="Patches" />).
/// </remarks>
public class EffectiveArea : Area
{
    private readonly DisallowedZone zone;
    private NativeBitArray bits;

    public EffectiveArea(
        AreaManager areaManager,
        DisallowedZone zone,
        Area baseArea
    )
        : base(areaManager)
    {
        this.zone = zone;
        BaseArea = baseArea;
        bits = new NativeBitArray(areaManager.map.cellIndices.NumGridCells, Allocator.Persistent);
        Recompute();
    }

    /// <summary>The pawn's own allowed area, or null for unrestricted pawns.</summary>
    public Area BaseArea { get; }

    public NativeBitArray Bits => bits;

    // Shown where vanilla names the area a pawn is restricted to, such as "outside allowed area (Home)".
    public override string Label => BaseArea?.Label ?? "Ordo_DisallowedZone_Unrestricted".Translate();

    public override string RenamableLabel
    {
        get => Label;
        set { }
    }

    public override string BaseLabel
    {
        get => Label;
        set { }
    }

    public override Color Color => BaseArea?.Color ?? DisallowedZone.ZoneColor;

    public override int ListPriority => 0;

    public override string GetUniqueLoadID() => "Ordo_DisallowedZone_EffectiveArea_" + ID;

    // Nothing outside this mod should paint an effective area; its cells follow the base area and the zone.
    public override void Set(
        IntVec3 c,
        bool val
    )
    { }

    private bool Compute(int index) => (BaseArea == null || BaseArea[index]) && !zone[index];

    public void UpdateCell(int index)
    {
        var value = Compute(index);
        var grid = InnerGrid;
        if (grid[index] == value)
            return;
        grid[index] = value;
        bits.Set(index, value);
        var cell = Map.cellIndices.IndexToCell(index);
        cell.GetRegion(Map, RegionType.Set_All)?.Notify_AreaChanged(this);
        // The path finder reuses its cost grids until it sees a change to the map's path data
        Map.pathFinder.MapData.Notify_CellDelta(CellRect.SingleCell(cell));
    }

    public void Recompute()
    {
        var grid = InnerGrid;
        var count = Map.cellIndices.NumGridCells;
        for (var i = 0; i < count; i++)
        {
            var value = Compute(i);
            grid[i] = value;
            bits.Set(i, value);
        }
        foreach (var region in Map.regionGrid.AllRegions_NoRebuild_InvalidAllowed)
            region.Notify_AreaChanged(this);
        Map.pathFinder.MapData.Notify_CellDelta(CellRect.SingleCell(IntVec3.Zero));
    }

    public void Dispose()
    {
        if (bits.IsCreated)
            bits.Dispose();
    }
}
