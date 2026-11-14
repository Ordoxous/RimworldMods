using System.Linq;
using System.Threading;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

public sealed class BlueprintMaterialsTools
{
	[Tool("ordo/stage_blueprintmaterials", Description = "Clear the map and leave a row of wood wall blueprints selected, with granite and steel on the map, for looking at the Select Material gizmo. Reports the selection's gizmos in grid order and the UI size. Don't save afterwards.")]
	public static object StageBlueprintMaterials(
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
		stage.Place(ThingDefOf.BlocksGranite, 0, 3, stackCount: 75);
		stage.Place(ThingDefOf.Steel, 2, 3, stackCount: 75);
		var walls = Enumerable.Range(-2, 5).Select(x => stage.Blueprint(ThingDefOf.Wall, x, 0, ThingDefOf.WoodLog)).Cast<Thing>().ToArray();
		Stage.Select(walls);
		Find.CameraDriver.JumpToCurrentMapLoc(sites[0]);

		return new
		{
			success = true,
			ui = new { width = UI.screenWidth, height = UI.screenHeight, scale = Prefs.UIScale },
			paneWidth = InspectPaneUtility.PaneWidthFor(Find.WindowStack.WindowOfType<IInspectPane>()),
			gizmos = Stage.SelectionGizmos().OrderBy(gizmo => gizmo.Order).Select(gizmo => new { type = gizmo.GetType().Name, label = (gizmo as Command)?.LabelCap.ToString(), order = gizmo.Order })
		};
	}
}
