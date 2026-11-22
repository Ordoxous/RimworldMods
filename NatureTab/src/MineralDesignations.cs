using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.NatureTab;

// Marks or unmarks a whole row for mining, with the same checks as dragging vanilla's mine designator over it
public static class MineralDesignations
{
    private static readonly DesignationDef[] MineDesignations = { DesignationDefOf.Mine, DesignationDefOf.MineVein };
    private static Designator_Mine mineDesignator;

    // Returns how many cells it marked. Designators work on the current map, so the row must come from it.
    public static int MineAll(
        MineralRow row,
        Map map)
    {
        Designator_Mine designator = mineDesignator ??= new Designator_Mine();
        DesignationManager designations = map.designationManager;
        int count = 0;
        foreach (Mineable mineable in row.cells)
        {
            if (!mineable.Spawned || mineable.Map != map || !designator.CanDesignateThing(mineable).Accepted)
                continue;
            // Designator_Mine.DesignateSingleCell, without its ideoligion warning for every cell, and without god mode
            // mining the cell on the spot
            designations.AddDesignation(new Designation(mineable.Position, DesignationDefOf.Mine));
            designations.TryRemoveDesignation(mineable.Position, DesignationDefOf.SmoothWall);
            count++;
        }
        if (count > 0)
            Designator_Mine.PossiblyWarnPlayerOnDesignatingMining();
        return count;
    }

    private static Designator_MineVein veinDesignator;

    // The nearest of the row's veins that Mine vein would still mark, held by its cell nearest the base, or null. None for
    // a kind the game doesn't vein-mine.
    public static Mineable NextVein(
        MineralRow row,
        Map map)
    {
        if (!row.def.building.veinMineable)
            return null;
        Designator_MineVein designator = veinDesignator ??= new Designator_MineVein();
        foreach (Mineable mineable in row.veins)
        {
            if (mineable.Spawned && mineable.Map == map && designator.CanDesignateThing(mineable).Accepted)
                return mineable;
        }
        return null;
    }

    // Marks the next vein as the game's Mine vein does, flooding every visible touching cell of the kind whatever the
    // row's area or distance; returns how many cells it marked
    public static int MineVein(
        MineralRow row,
        Map map)
    {
        Mineable start = NextVein(row, map);
        if (start == null)
            return 0;
        DesignationManager designations = map.designationManager;
        int before = designations.SpawnedDesignationsOfDef(DesignationDefOf.MineVein).Count();
        Designator_MineVein.FloodFillDesignations(start.Position, map, row.def);
        Designator_Mine.PossiblyWarnPlayerOnDesignatingMining();
        return designations.SpawnedDesignationsOfDef(DesignationDefOf.MineVein).Count() - before;
    }

    // Removes both mine and mine-vein designations; returns how many it removed
    public static int Cancel(
        MineralRow row,
        Map map)
    {
        DesignationManager designations = map.designationManager;
        int count = 0;
        foreach (Mineable mineable in row.cells)
        {
            foreach (DesignationDef def in MineDesignations)
            {
                Designation designation = designations.DesignationAt(mineable.Position, def);
                if (designation == null)
                    continue;
                designations.RemoveDesignation(designation);
                count++;
            }
        }
        return count;
    }
}
