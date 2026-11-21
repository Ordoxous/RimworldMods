using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using WVC_WorkModes;

namespace Ordo.Tweaks;

/// <summary>
/// Finds WVC shutdown zone cells for a hauler to take a mech to. WVC's own <c>ShutdownUtility.CanSelfShutdown</c> asks
/// whether the mech can reach and reserve the cell on its own map, which fails while the mech is being carried or the
/// cell is on another MultiFloors level, so the reach and reservation checks here are the hauler's.
/// </summary>
internal static class ShutdownCells
{
    // The cells of every shutdown zone on map that takes mech for workType
    public static List<IntVec3> ZoneCells(
        Map map,
        Pawn mech,
        MechanoidWorkType workType
    )
    {
        var cells = new List<IntVec3>();
        foreach (var zone in map.zoneManager.AllZones)
        {
            if (zone is Zone_MechanoidShutdown shutdownZone
                && ShutdownUtility.MechanoidWorkTypeIsCorrect(shutdownZone, workType, mech))
            {
                cells.AddRange(shutdownZone.Cells);
            }
        }

        return cells;
    }

    // Nearest first to the interaction cell of a charger on map the mech is compatible with (taken or not), so it's
    // closer when one frees up; in random order when nearCharger is false or there's no such charger
    public static IEnumerable<IntVec3> InPreferredOrder(
        List<IntVec3> cells,
        Map map,
        Pawn mech,
        Pawn hauler,
        bool nearCharger
    )
    {
        if (!nearCharger)
        {
            return cells.InRandomOrder();
        }

        var chargers = map.listerThings.ThingsInGroup(ThingRequestGroup.MechCharger)
            .OfType<Building_MechCharger>()
            .Where(charger => charger.IsCompatibleWithCharger(mech.kindDef) && !charger.IsForbidden(hauler))
            .Select(charger => charger.InteractionCell)
            .ToList();

        return chargers.Count > 0
            ? cells.OrderBy(candidate => chargers.Min(spot => candidate.DistanceToSquared(spot)))
            : cells.InRandomOrder();
    }

    // WVC's CanSelfShutdown without its reach and reservation checks. Areas belong to one map, so forbiddance is only
    // checked for a pawn on the cell's map.
    public static bool Suitable(
        IntVec3 cell,
        Map map,
        Pawn hauler,
        Pawn mech
    )
    {
        if (!cell.Standable(map)
            || (hauler.Map == map && cell.IsForbidden(hauler))
            || (mech.Spawned && mech.Map == map && cell.IsForbidden(mech))
            || (!WVC_MMWM.settings.useCustomShutdownBehavior && cell.GetFirstBuilding(map) != null)
            || cell.GetRoom(map) is { IsPrisonCell: true })
        {
            return false;
        }

        foreach (var direction in GenAdj.CardinalDirections)
        {
            var neighbour = cell + direction;
            if (!neighbour.InBounds(map))
            {
                continue;
            }

            foreach (var thing in neighbour.GetThingList(map))
            {
                if (thing.def.hasInteractionCell && thing.InteractionCell == cell)
                {
                    return false;
                }
            }
        }

        return true;
    }

    // A cell on the hauler's map for it to take the mech to, the hauler being able to reach and reserve it
    public static bool TryFindCell(
        Pawn hauler,
        Pawn mech,
        MechanoidWorkType workType,
        bool nearCharger,
        out IntVec3 cell
    )
    {
        var map = hauler.Map;
        foreach (var candidate in InPreferredOrder(ZoneCells(map, mech, workType), map, mech, hauler, nearCharger))
        {
            if (Usable(candidate, hauler, mech))
            {
                cell = candidate;
                return true;
            }
        }

        cell = IntVec3.Invalid;
        return false;
    }

    // Whether the hauler can take the mech to this cell on the hauler's own map
    public static bool Usable(
        IntVec3 cell,
        Pawn hauler,
        Pawn mech
    ) => cell.IsValid
         && cell.InBounds(hauler.Map)
         && Suitable(cell, hauler.Map, hauler, mech)
         && hauler.CanReach(cell, PathEndMode.OnCell, Danger.Some)
         && hauler.CanReserve(cell);
}
