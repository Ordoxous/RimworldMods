using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// A MultiFloors level above the test map, reached by reflection. Spawning an up-stair makes the level, which is void apart
// from the cells around its exit, so the exit's floor is laid over the rest of the scenario's corner. A level is its own
// map: Dispose destroys what's on it, since removing the level would pass its pawns to the world, then removes it.
public sealed class UpperLevel : IDisposable
{
    // The stair's cell, relative to the scenario's origin. The floor on the level covers (2, 2) to (13, 13) around it.
    private const int StairX = 6, StairZ = 6;

    public readonly Map Map;

    private readonly Thing _entrance;

    public UpperLevel(
        ScenarioContext context
    )
    {
        _entrance = context.Build(ThingDef.Named("MF_BrickStairsMUpA"), StairX, StairZ);
        Map = (Map)AccessTools.Property(_entrance.GetType(), "ConnectedMap").GetValue(_entrance);
        context.Check($"the stair made a level above: {Map}", Map != null && Map != context.Map);
        if (Map == null)
        {
            return;
        }

        var exit = (Thing)AccessTools.Field(_entrance.GetType(), "Exit").GetValue(_entrance);
        var floor = Map.terrainGrid.TerrainAt(exit.Position);
        foreach (var cell in CellRect.FromLimits(context.Cell(2, 2), context.Cell(13, 13)))
        {
            Map.terrainGrid.SetTerrain(cell, floor);
            Map.fogGrid.Unfog(cell);
        }
    }

    // Its cells are the same coordinates as the ground map's
    public List<IntVec3> ZoneCells(
        ScenarioContext context
    ) => CellRect.CenteredOn(context.Cell(10, 10), 1).Cells.ToList();

    // A light-mech charger on the level, on a power net with a charged battery, as FreeCharger makes on the ground.
    // It goes when the level does.
    public async Task<Building_MechCharger> FreeCharger(
        ScenarioContext context,
        Pawn mech
    )
    {
        var charger = (Building_MechCharger)Build(ThingDef.Named("BasicRecharger"), context.Cell(10, 4));
        var battery = Build(ThingDef.Named("Battery"), context.Cell(12, 4));
        battery.TryGetComp<CompPowerBattery>().SetStoredEnergyPct(1f);
        await context.Until($"the charger upstairs is powered: net {charger.PowerComp.PowerNet != null}", () => charger.CanPawnChargeCurrently(mech), 300, 30);
        return charger;
    }

    // A player building on the level. It goes when the level does.
    public Thing Build(
        ThingDef def,
        IntVec3 cell,
        Rot4 rotation = default
    )
    {
        var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        thing.SetFaction(Faction.OfPlayer);
        return GenSpawn.Spawn(thing, cell, Map, rotation);
    }

    // Moves a pawn the scenario spawned on the ground up to the level. Respawning undrafts it, so a mechanitor would go
    // and haul the mech itself; it's drafted again.
    public void MoveHere(
        Pawn pawn,
        IntVec3 cell
    )
    {
        var drafted = pawn.Drafted;
        pawn.DeSpawn();
        GenSpawn.Spawn(pawn, cell, Map);
        if (drafted)
        {
            pawn.drafter.Drafted = true;
        }
    }

    public void Dispose()
    {
        if (Map == null)
        {
            return;
        }

        foreach (var zone in Map.zoneManager.AllZones.ToList())
        {
            zone.Delete();
        }

        foreach (var pawn in Map.mapPawns.AllPawnsSpawned.ToList())
        {
            pawn.Destroy();
        }

        AccessTools.Method(_entrance.GetType(), "DeleteConnectedLevel").Invoke(_entrance, null);
    }
}
