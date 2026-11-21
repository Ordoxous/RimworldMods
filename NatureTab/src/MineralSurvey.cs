using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.NatureTab;

// One row of the Minerals page: every visible cell of one kind of ore on the map
public class MineralRow
{
    public ThingDef def;
    public readonly List<Mineable> cells = new();
    public int marked;

    // Expected yield of every listed cell, mined by a miner with 100% mining yield
    public float yield;

    // Groups of touching cells, each held by the cell nearest the base, nearest first
    public readonly List<Mineable> veins = new();

    // Cells under overhead mountain: mining them leaves that roof up, and it collapses if it ends up unsupported
    public int overhead;
    public readonly HashSet<Mineable> overheadCells = new();

    public float nearest = float.PositiveInfinity;

    public ThingDef Product => def.building.mineableThing;
    public float YieldValue => yield * Product.BaseMarketValue;
}

public struct MineralFilter
{
    // Only cells in this area, when set. It must belong to the surveyed map.
    public Area area;

    // Only cells within this many cells of the base, when above 0
    public int maxDistance;
}

// Builds the Minerals page's rows. There's no lister group for mineables, but listerThings indexes things by def, so a
// survey walks only the ore kinds' own cells, never the map's ordinary rock.
public static class MineralSurvey
{
    private static List<ThingDef> oreDefs;
    private static readonly Dictionary<ThingDef, MineralRow> RowsByDef = new();
    private static readonly HashSet<IntVec3> Visited = new();
    private static readonly Queue<IntVec3> Frontier = new();

    // Every resource rock: vanilla's ores, compacted machinery, and modded ores that set isResourceRock
    public static List<ThingDef> OreDefs => oreDefs ??= DefDatabase<ThingDef>.AllDefsListForReading
        .FindAll(d => d.building is { isResourceRock: true, mineableThing: not null });

    public static List<MineralRow> Build(
        Map map,
        MineralFilter filter)
    {
        RowsByDef.Clear();
        DesignationManager designations = map.designationManager;
        Area area = filter.area?.Map == map ? filter.area : null;
        IntVec3 baseCenter = FloraSurvey.BaseCenter(map);
        float maxDistanceSquared = filter.maxDistance > 0 ? filter.maxDistance * filter.maxDistance : float.MaxValue;

        foreach (ThingDef def in OreDefs)
        {
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing is not Mineable mineable || !mineable.Spawned)
                    continue;
                IntVec3 cell = mineable.Position;
                // Never list what the player can't see, god mode or not
                if (cell.Fogged(map))
                    continue;
                if (area != null && !area[cell])
                    continue;
                float distanceSquared = (cell - baseCenter).LengthHorizontalSquared;
                if (distanceSquared > maxDistanceSquared)
                    continue;

                if (!RowsByDef.TryGetValue(def, out MineralRow row))
                    RowsByDef[def] = row = new MineralRow { def = def };

                row.cells.Add(mineable);
                row.nearest = Mathf.Min(row.nearest, Mathf.Sqrt(distanceSquared));
                row.yield += ExpectedYield(mineable);
                if (IsMarked(designations, cell))
                    row.marked++;
                if (cell.GetRoof(map) is { isThickRoof: true })
                {
                    row.overhead++;
                    row.overheadCells.Add(mineable);
                }
            }
        }

        var rows = new List<MineralRow>(RowsByDef.Values);
        RowsByDef.Clear();
        foreach (MineralRow row in rows)
            FindVeins(row, map, baseCenter);
        return rows;
    }

    public static bool IsMarked(
        DesignationManager designations,
        IntVec3 cell) =>
        designations.DesignationAt(cell, DesignationDefOf.Mine) != null
        || designations.DesignationAt(cell, DesignationDefOf.MineVein) != null;

    // Mineable.TrySpawnYield without its randomness: at least 1 of EffectiveMineableYield, times the drop chance. Wasteable
    // yields scale with yieldPct, which mining damage adds to as HitPoints / MaxHitPoints × the miner's MiningYield, so a
    // cell part-mined already has some banked and the rest still to come.
    public static float ExpectedYield(Mineable mineable)
    {
        BuildingProperties building = mineable.def.building;
        float yield = Mathf.Max(1, building.EffectiveMineableYield);
        if (building.mineableYieldWasteable)
        {
            float pct = mineable.yieldPct + (float)mineable.HitPoints / mineable.MaxHitPoints;
            yield = Mathf.Max(1f, yield * Mathf.Clamp01(pct));
        }
        return yield * building.mineableDropChance;
    }

    // Splits the row's cells into veins of cardinally touching cells, the way vein mining spreads, and keeps the cell of
    // each nearest the base
    private static void FindVeins(
        MineralRow row,
        Map map,
        IntVec3 baseCenter)
    {
        var byCell = new Dictionary<IntVec3, Mineable>(row.cells.Count);
        foreach (Mineable mineable in row.cells)
            byCell[mineable.Position] = mineable;

        Visited.Clear();
        foreach (Mineable start in row.cells)
        {
            if (!Visited.Add(start.Position))
                continue;
            Mineable closest = start;
            int closestDistance = (start.Position - baseCenter).LengthHorizontalSquared;
            Frontier.Enqueue(start.Position);
            while (Frontier.Count > 0)
            {
                IntVec3 cell = Frontier.Dequeue();
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + offset;
                    if (!byCell.TryGetValue(next, out Mineable neighbour) || !Visited.Add(next))
                        continue;
                    Frontier.Enqueue(next);
                    int distance = (next - baseCenter).LengthHorizontalSquared;
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closest = neighbour;
                    }
                }
            }
            row.veins.Add(closest);
        }
        Visited.Clear();
        row.veins.Sort((a, b) =>
            (a.Position - baseCenter).LengthHorizontalSquared.CompareTo((b.Position - baseCenter).LengthHorizontalSquared));
    }
}
