using System.Threading;
using RimBridgeServer.Sdk;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

public sealed class MechUpgradesUiTools
{
	[Tool("ordo/stage_mechupgradesui", Description = "Clear the map and leave a Mech Upgrade Policies site standing for looking at its UI: an auto upgrader with a stocked storage, a mechanitor, a lifter with a policy that wants changes and a lifter with none. Adds a policy to the world, so don't save afterwards.")]
	public static object StageMechUpgradesUi(
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

		return MechUpgradesUiScenarios.Demo(new ScenarioContext(bridge, map, sites[0], cancellationToken));
	}
}
