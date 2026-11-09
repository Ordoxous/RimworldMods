using System.Linq;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

public sealed class ColonyTools
{
	[Tool("ordo/start_test_colony", Description = "From the main menu, start the same quick test colony as rimworld/start_debug_game but on a smaller map. Returns straight away, so follow it with rimbridge/wait_for_long_event_idle.")]
	public static object StartTestColony(
		[ToolParameter(Description = "Width and height of the map in cells. A stage needs at least 33.")] int mapSize = 33
	)
	{
		if (Current.ProgramState != ProgramState.Entry)
		{
			return new { success = false, message = "Only works from the main menu" };
		}

		LongEventHandler.QueueLongEvent(
			() =>
			{
				Root_Play.SetupForQuickTestPlay();
				Find.GameInitData.mapSize = mapSize;
				PageUtility.InitGameStart();
			},
			"GeneratingMap",
			true,
			null
		);
		return new { success = true, mapSize };
	}

	[Tool("ordo/blank_map", Description = "Strip the current map down to flat soil in the open, with no pawns, things, roofs or fog, to save as a test map. Destroys everything on it.")]
	public static object BlankMap()
	{
		if (Find.CurrentMap == null)
		{
			return new { success = false, message = "No map is loaded" };
		}

		var map = Find.CurrentMap;
		foreach (var thing in map.listerThings.AllThings.ToList().Where(thing => !thing.Destroyed && thing.Spawned))
		{
			if (thing.def.destroyable)
			{
				thing.Destroy();
			}
			else
			{
				thing.DeSpawn();
			}
		}

		foreach (var cell in map.AllCells)
		{
			map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
			map.roofGrid.SetRoof(cell, null);
			map.fogGrid.Unfog(cell);
		}

		map.weatherManager.curWeather = WeatherDefOf.Clear;
		map.weatherManager.lastWeather = WeatherDefOf.Clear;
		return new { success = true, thingsLeft = map.listerThings.AllThings.Count, pawnsLeft = map.mapPawns.AllPawnsSpawnedCount };
	}

	[Tool("ordo/set_hour", Description = "Move time forward to an hour of the day on the current map without simulating it, so a test save can start in daylight.")]
	public static object SetHour(
		[ToolParameter(Description = "Hour of the day, from 0 to 23")] int hour = 12
	)
	{
		if (Find.CurrentMap == null)
		{
			return new { success = false, message = "No map is loaded" };
		}

		var hoursAhead = (hour - GenLocalDate.HourOfDay(Find.CurrentMap) + GenDate.HoursPerDay) % GenDate.HoursPerDay;
		Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + hoursAhead * GenDate.TicksPerHour);
		return new { success = true, hour = GenLocalDate.HourOfDay(Find.CurrentMap), hoursAhead };
	}
}
