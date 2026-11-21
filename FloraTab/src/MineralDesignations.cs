using RimWorld;
using Verse;

namespace Ordo.FloraTab;

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
