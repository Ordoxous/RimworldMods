using System.Linq;
using System.Threading;
using Ordo.ColonistBarJobs;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

public sealed class ColonistBarJobsTools
{
    [Tool("ordo/stage_colonistbarjobs", Description = "Clear the map and leave colonists at work for looking at the colonist bar's job icons: a miner at designated granite, a builder at wood wall blueprints, a hauler with steel and a stockpile, and an idle colonist. With Biotech, also a selected mechanitor with two lifters hauling, for the control group gizmo. Unpauses the game. Reports each colonist's job and the icon it resolves to. Don't save afterwards.")]
    public static object StageColonistBarJobs(
        IRimBridgeContext bridge,
        CancellationToken cancellationToken
    )
    {
        if (Find.CurrentMap == null)
        {
            return new { success = false, message = "No map is loaded" };
        }

        var map = Find.CurrentMap;
        StageSites.ClearOfVisitors(map);
        var sites = StageSites.FindAndClear(map);
        if (sites.Count == 0)
        {
            return new { success = false, message = $"No clear {StageSites.Radius * 2}-cell site" };
        }

        var stage = new ScenarioContext(bridge, map, sites[0], cancellationToken);

        var granite = DefDatabase<ThingDef>.GetNamed("Granite");
        for (var z = -2; z <= 2; z++)
        {
            var rock = stage.Place(granite, -8, z);
            map.designationManager.AddDesignation(new Designation(rock, DesignationDefOf.Mine));
        }

        stage.Place(ThingDefOf.WoodLog, 2, 4, stackCount: 75);
        for (var x = 0; x < 4; x++)
        {
            stage.Blueprint(ThingDefOf.Wall, x, 6, ThingDefOf.WoodLog);
        }

        var stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        map.zoneManager.RegisterZone(stockpile);
        for (var x = 8; x < 11; x++)
        {
            for (var z = 6; z < 9; z++)
            {
                stockpile.AddCell(stage.Cell(x, z));
            }
        }

        for (var x = 0; x < 3; x++)
        {
            stage.Place(ThingDefOf.Steel, 4 + x, -6, stackCount: 10);
        }

        // The bar lists colonists in spawn order, and dev mode's toolbar covers the middle slots: the first and last slots show best
        stage.Worker(6, -4, WorkTypeDefOf.Hauling);
        stage.Worker(2, -2);
        stage.Worker(-6, 0, WorkTypeDefOf.Mining);
        stage.Worker(0, 3, WorkTypeDefOf.Construction);

        // With Biotech, a mechanitor (drafted, so they stay put) and two lifters hauling more steel, selected so the control
        // group gizmo shows the mechs' icons
        if (ModsConfig.BiotechActive)
        {
            for (var x = 0; x < 3; x++)
            {
                stage.Place(ThingDefOf.Steel, -4 + x, -6, stackCount: 10);
            }

            var mechanitor = stage.Mechanitor(-2, -4);
            stage.ControlledMech("Mech_Lifter", mechanitor, -4, -4);
            stage.ControlledMech("Mech_Lifter", mechanitor, -3, -4);
            Find.Selector.ClearSelection();
            Find.Selector.Select(mechanitor);
        }

        Find.CameraDriver.JumpToCurrentMapLoc(sites[0]);
        Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        ColonistBarJobIcons.ClearCache();

        return new
        {
            success = true,
            colonists = map.mapPawns.FreeColonistsSpawned.Concat(map.mapPawns.SpawnedColonyMechs).Select(pawn => new
            {
                name = pawn.LabelShort,
                job = pawn.CurJobDef?.defName,
                icon = JobIconResolver.For(pawn).ToString()
            })
        };
    }
}
