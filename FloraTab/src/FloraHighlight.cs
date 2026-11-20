using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.FloraTab;

// Outlines a row's plants on the map while the row is hovered: marked for harvest in yellow, else at risk (dying or frost
// coming) in red, else ready in green, else growing in white. When none are on screen, an arrow at the screen edge points to the nearest ready one.
public static class FloraHighlight
{
    private static readonly Color ReadyColor = new(0.35f, 1f, 0.35f);
    private static readonly Color GrowingColor = new(1f, 1f, 1f, 0.6f);
    private static readonly Color MarkedColor = new(1f, 0.9f, 0.2f);

    private static readonly List<IntVec3> ReadyCells = new();
    private static readonly List<IntVec3> GrowingCells = new();
    private static readonly List<IntVec3> MarkedCells = new();
    private static readonly Color RiskColor = new(1f, 0.3f, 0.25f);
    private static readonly List<IntVec3> RiskCells = new();

    public static void Draw(
        FloraRow row,
        Map map)
    {
        ReadyCells.Clear();
        GrowingCells.Clear();
        MarkedCells.Clear();
        RiskCells.Clear();

        CellRect view = Find.CameraDriver.CurrentViewRect;
        IntVec3 center = view.CenterCell;
        bool anyOnScreen = false;
        Plant nearestReady = null;
        Plant nearest = null;
        int nearestReadyDist = int.MaxValue;
        int nearestDist = int.MaxValue;
        DesignationManager designations = map.designationManager;

        foreach (Plant plant in row.plants)
        {
            if (!plant.Spawned || plant.Map != map)
                continue;
            IntVec3 cell = plant.Position;
            bool ready = plant.HarvestableNow;

            if (designations.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                MarkedCells.Add(cell);
            else if (row.atRisk.Contains(plant))
                RiskCells.Add(cell);
            else if (ready)
                ReadyCells.Add(cell);
            else
                GrowingCells.Add(cell);

            if (view.Contains(cell))
            {
                anyOnScreen = true;
                continue;
            }
            int dist = (cell - center).LengthHorizontalSquared;
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = plant;
            }
            if (ready && dist < nearestReadyDist)
            {
                nearestReadyDist = dist;
                nearestReady = plant;
            }
        }

        if (GrowingCells.Count > 0)
            GenDraw.DrawFieldEdges(GrowingCells, GrowingColor);
        if (ReadyCells.Count > 0)
            GenDraw.DrawFieldEdges(ReadyCells, ReadyColor);
        if (RiskCells.Count > 0)
            GenDraw.DrawFieldEdges(RiskCells, RiskColor);
        if (MarkedCells.Count > 0)
            GenDraw.DrawFieldEdges(MarkedCells, MarkedColor);

        Plant arrowTo = nearestReady ?? nearest;
        if (!anyOnScreen && arrowTo != null)
            GenDraw.DrawArrowPointingAt(arrowTo.TrueCenter(), offscreenOnly: true);
    }
}
