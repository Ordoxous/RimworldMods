using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.NatureTab;

// Outlines a row's cells on the map while the row is hovered: marked for mining in yellow, else under overhead mountain
// in orange, else white. When none are on screen, an arrow at the screen edge points to the vein nearest the base.
public static class MineralHighlight
{
    private static readonly Color OpenColor = new(1f, 1f, 1f, 0.8f);
    private static readonly Color OverheadColor = new(1f, 0.6f, 0.2f);
    private static readonly Color MarkedColor = new(1f, 0.9f, 0.2f);

    private static readonly List<IntVec3> OpenCells = new();
    private static readonly List<IntVec3> OverheadCells = new();
    private static readonly List<IntVec3> MarkedCells = new();

    public static void Draw(
        MineralRow row,
        Map map)
    {
        OpenCells.Clear();
        OverheadCells.Clear();
        MarkedCells.Clear();
        CellRect view = Find.CameraDriver.CurrentViewRect;
        bool anyOnScreen = false;
        DesignationManager designations = map.designationManager;

        foreach (Mineable mineable in row.cells)
        {
            if (!mineable.Spawned || mineable.Map != map)
                continue;
            IntVec3 cell = mineable.Position;
            if (MineralSurvey.IsMarked(designations, cell))
                MarkedCells.Add(cell);
            else if (row.overheadCells.Contains(mineable))
                OverheadCells.Add(cell);
            else
                OpenCells.Add(cell);
            anyOnScreen |= view.Contains(cell);
        }

        if (OpenCells.Count > 0)
            GenDraw.DrawFieldEdges(OpenCells, OpenColor);
        if (OverheadCells.Count > 0)
            GenDraw.DrawFieldEdges(OverheadCells, OverheadColor);
        if (MarkedCells.Count > 0)
            GenDraw.DrawFieldEdges(MarkedCells, MarkedColor);

        if (!anyOnScreen && row.veins.Count > 0 && row.veins[0].Spawned)
            GenDraw.DrawArrowPointingAt(row.veins[0].TrueCenter(), offscreenOnly: true);
    }
}
