using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ordo.BlueprintMaterials;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Showcase;

// The gizmo and its menu, driven with RimBridge's virtual cursor as a player would. The game pauses while the cursor works the
// menu, as a player would pause, so the colonist hauling the old material is still on the way when the change lands. Both
// clips film the whole window, because the gizmos are at its bottom edge. The checks only decide whether a take is worth keeping.
public static class BlueprintMaterialsShowcase
{
	private const int Stack = 75;

	public static readonly List<ShowcaseClip> All = new()
	{
		ShowcaseClip.Feature("change_walls", "Select Material",
			"Change a blueprint's material in place instead of cancelling it. Anyone already hauling the old material goes for the new one",
			ChangeWalls, showUi: true, frame: ShowcaseFrame.Window),
		ShowcaseClip.Feature("change_floors", "Floors Too",
			"Switch floor blueprints to another floor from the same group, such as another stone's tiles",
			ChangeFloors, showUi: true, frame: ShowcaseFrame.Window)
	};

	private static async Task ChangeWalls(
		ShowcaseStage stage
	)
	{
		// The colonist carries wood rightwards to the walls; the granite is back on the left, so the change turns them round
		stage.Place(ThingDefOf.WoodLog, -14, 0, stackCount: Stack);
		stage.Place(ThingDefOf.BlocksGranite, -12, 5, stackCount: Stack);
		stage.Place(ThingDefOf.Steel, -9, 5, stackCount: Stack);
		var walls = Enumerable.Range(4, 7).Select(x => stage.Blueprint(ThingDefOf.Wall, x, 2, ThingDefOf.WoodLog)).ToArray();
		var worker = stage.Worker(-15, -1, WorkTypeDefOf.Construction);

		bool Bringing(
			ThingDef stuff
		)
		{
			return worker.CurJobDef == JobDefOf.HaulToContainer
			       && worker.carryTracker.CarriedThing?.def == stuff
			       && walls.Contains(worker.CurJob.targetB.Thing);
		}

		await stage.Beat();
		if (!await stage.Until("the colonist sets off with wood", () => Bringing(ThingDefOf.WoodLog), 900))
		{
			return;
		}

		// Partway there
		await stage.Step(70);
		await ChooseFromGizmo(stage, walls.Cast<Thing>().ToArray(), ThingDefOf.BlocksGranite.LabelAsStuff);
		stage.Check($"the walls are granite: {string.Join(",", walls.Select(wall => wall.stuffToUse?.defName))}", walls.All(wall => wall.stuffToUse == ThingDefOf.BlocksGranite));

		if (await stage.Until("the colonist fetches granite instead", () => Bringing(ThingDefOf.BlocksGranite), 900))
		{
			await stage.Linger(200);
		}
	}

	private static async Task ChangeFloors(
		ShowcaseStage stage
	)
	{
		var marble = DefDatabase<ThingDef>.GetNamed("BlocksMarble");

		// A dropdown group with floors of granite, marble and some third stone, such as the stone tiles
		var group = DefDatabase<TerrainDef>.AllDefsListForReading
			.Where(def => def.BuildableByPlayer && def.designatorDropdown != null)
			.GroupBy(def => def.designatorDropdown)
			.Select(floors => floors.ToList())
			.FirstOrDefault(floors => floors.Any(def => Material(def) == ThingDefOf.BlocksGranite) && floors.Any(def => Material(def) == marble));
		var from = group?.FirstOrDefault(def => Material(def) is { } material && material != marble && material != ThingDefOf.BlocksGranite && material.stuffProps?.categories?.Contains(StuffCategoryDefOf.Stony) == true);
		if (from == null)
		{
			stage.Fail("no floor group with granite, marble and another stone");
			return;
		}

		stage.Place(ThingDefOf.BlocksGranite, -8, 5, stackCount: Stack);
		stage.Place(marble, -6, 5, stackCount: Stack);
		var floors = new List<Thing>();
		for (var x = -3; x <= 3; x++)
		{
			for (var z = 0; z <= 3; z++)
			{
				floors.Add(stage.Blueprint(from, x, z));
			}
		}

		await stage.Beat();
		await ChooseFromGizmo(stage, floors.ToArray(), marble.LabelAsStuff);
		var replaced = floors.Select(floor => floor.Position.GetThingList(stage.Map).OfType<Blueprint_Build>().FirstOrDefault()).ToList();
		stage.Check("every floor blueprint is now marble", replaced.All(blueprint => Material(blueprint?.def.entityDefToBuild as TerrainDef) == marble));
		await stage.Linger(200);
	}

	private static ThingDef Material(
		TerrainDef terrain
	)
	{
		return terrain?.CostList?.FirstOrDefault()?.thingDef;
	}

	// Pauses, selects, hovers the gizmo long enough to read its tooltip, opens its menu at the cursor, moves down to the
	// option and picks it, then carries on
	private static async Task ChooseFromGizmo(
		ShowcaseStage stage,
		Thing[] things,
		string label
	)
	{
		Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
		Stage.Select(things);
		await stage.Hold(0.8f);

		var center = stage.GizmoCenter(gizmo => gizmo is Command_Action_AlternativeMaterial);
		if (center is not { } position)
		{
			return;
		}

		stage.Hover(position);
		await stage.Hold(2.5f);

		var gizmo = Stage.SelectionGizmos().OfType<Command_Action_AlternativeMaterial>().First();
		var menu = stage.OpenMenu(gizmo);
		if (menu == null)
		{
			return;
		}

		try
		{
			// The menu sizes and places itself on its first draw
			await stage.Hold(0.6f);
			var top = 0f;
			foreach (var option in menu.options)
			{
				if (option.Label == label)
				{
					stage.Hover(new Vector2(menu.windowRect.x + 40f, menu.windowRect.y + top + option.RequiredHeight / 2f));
					break;
				}

				top += option.RequiredHeight - 1f;
			}

			await stage.Hold(1.5f);
			stage.Choose(menu, label);
			ShowcaseStage.ClearHover();
			await stage.Hold(0.8f);
		}
		finally
		{
			if (Find.WindowStack.IsOpen(menu))
			{
				menu.Close(false);
			}
		}
	}
}
