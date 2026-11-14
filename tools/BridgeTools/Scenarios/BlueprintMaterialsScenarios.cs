using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ordo.BlueprintMaterials;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Scenarios;

// The gizmo is driven as a player drives it: select blueprints, take the gizmo from what's selected, open its menu and pick an
// option. Floors are picked from the defs at runtime, so the scenarios don't depend on which floors Core has.
public static class BlueprintMaterialsScenarios
{
	private const int Stack = 75;

	// Suites: "stuff" for buildings made from stuff, "floor" for floors, "availability" for which materials are offered,
	// "grouping" for which blueprints a gizmo changes, "ai" for haulers, and "setup" for where the gizmo appears
	public static readonly List<Scenario> All = new()
	{
		new("setup_where_the_gizmo_appears", "setup", WhereTheGizmoAppears),
		new("visuals_gizmos_draw", "visuals setup", GizmosDraw),

		new("stuff_change", "stuff", StuffChange),
		new("stuff_only_the_gizmos_group", "stuff grouping", OnlyTheGizmosGroup),
		new("stuff_unselected_untouched", "stuff grouping", UnselectedUntouched),
		new("stuff_haulers_replanned", "stuff ai", HaulersReplanned),

		new("availability_disabled_without_alternative", "availability", DisabledWithoutAlternative),
		new("availability_lists_what_is_on_the_map", "availability", ListsWhatIsOnTheMap),
		new("availability_god_mode_lists_everything", "availability", GodModeListsEverything),

		new("floor_change", "floor grouping", FloorChange),
		new("floor_labels_by_terrain_when_material_shared", "floor", LabelsByTerrain)
	};

	private static ThingDef Wood => ThingDefOf.WoodLog;

	private static ThingDef Steel => ThingDefOf.Steel;

	private static ThingDef Granite => ThingDefOf.BlocksGranite;

	private static Command_Action_AlternativeMaterial MaterialGizmo(
		ScenarioContext context,
		Blueprint_Build blueprint
	)
	{
		var gizmo = blueprint.GetGizmos().OfType<Command_Action_AlternativeMaterial>().FirstOrDefault();
		context.Check($"{blueprint.Label} has a Select Material gizmo", gizmo != null);
		return gizmo;
	}

	// Runs the whole player path: select, click the gizmo, pick a material
	private static void ChangeMaterial(
		ScenarioContext context,
		Blueprint_Build clicked,
		string label,
		params Blueprint_Build[] selected
	)
	{
		Stage.Select(selected.Cast<Thing>().ToArray());
		var gizmo = MaterialGizmo(context, clicked);
		if (gizmo != null)
		{
			context.Choose(context.OpenMenu(gizmo), label);
		}
	}

	private static string Stuffs(
		IEnumerable<Blueprint_Build> blueprints
	)
	{
		return string.Join(", ", blueprints.Select(blueprint => blueprint.stuffToUse?.defName ?? "none"));
	}

	private static void WithGodMode(
		bool on,
		System.Action run
	)
	{
		var was = DebugSettings.godMode;
		DebugSettings.godMode = on;
		try
		{
			run();
		}
		finally
		{
			DebugSettings.godMode = was;
		}
	}

	// The gizmo is for build blueprints made from stuff, and for floors in a dropdown group: nothing else gets one
	private static Task WhereTheGizmoAppears(
		ScenarioContext context
	)
	{
		context.Place(Steel, 0, 3, stackCount: Stack);
		var wall = context.Blueprint(ThingDefOf.Wall, 0, 0, Wood);
		MaterialGizmo(context, wall);

		var unstuffed = DefDatabase<ThingDef>.AllDefsListForReading.FirstOrDefault(def => def.BuildableByPlayer && !def.MadeFromStuff && def.Size == IntVec2.One && def.costList?.Count > 0);
		if (unstuffed == null)
		{
			context.Fail("no buildable 1x1 thing that isn't made from stuff");
		}
		else
		{
			var blueprint = context.Blueprint(unstuffed, 3, 0);
			context.Check($"{unstuffed.defName}, not made from stuff, has no Select Material gizmo", !blueprint.GetGizmos().OfType<Command_Action_AlternativeMaterial>().Any());
		}

		var ungrouped = DefDatabase<TerrainDef>.AllDefsListForReading.FirstOrDefault(def => def.BuildableByPlayer && def.designatorDropdown == null && def.CostList?.Count > 0);
		if (ungrouped != null)
		{
			var blueprint = context.Blueprint(ungrouped, -3, 0);
			context.Check($"{ungrouped.defName}, a floor in no dropdown group, has no Select Material gizmo", !blueprint.GetGizmos().OfType<Command_Action_AlternativeMaterial>().Any());
		}

		return Task.CompletedTask;
	}

	// Keeps a wall's and a floor's gizmos on screen for some frames, so any error drawing them is logged and fails the scenario.
	// Only windowed profiles draw: headless, this passes without testing anything. The mod's style overlay once loaded an
	// Ideology texture, which logged an error in every game without Ideology.
	private static async Task GizmosDraw(
		ScenarioContext context
	)
	{
		context.Place(Steel, 0, 3, stackCount: Stack);
		var floor = FloorGroups().First()[0];
		context.Place(Material(floor), 2, 3, stackCount: Stack);
		var wall = context.Blueprint(ThingDefOf.Wall, -1, 0, Wood);
		var floorBlueprint = context.Blueprint(floor, 1, 0);
		Stage.Select(wall, floorBlueprint);
		context.Check("both blueprints show a Select Material gizmo", Stage.SelectionGizmos().OfType<Command_Action_AlternativeMaterial>().Count() == 2);
		await context.Step(60);
	}

	private static Task StuffChange(
		ScenarioContext context
	)
	{
		context.Place(Granite, 0, 3, stackCount: Stack);
		var walls = Enumerable.Range(-1, 3).Select(x => context.Blueprint(ThingDefOf.Wall, x, 0, Wood)).ToArray();
		ChangeMaterial(context, walls[0], Granite.LabelAsStuff, walls);
		context.Check($"every selected wall is now granite: [{Stuffs(walls)}]", walls.All(wall => wall.stuffToUse == Granite));
		return Task.CompletedTask;
	}

	// Granite and marble walls selected together show two gizmos, and each changes only the walls of its own material. Both
	// are stone bricks, so their gizmos have the same icon: vanilla would group gizmos with different icons apart by itself.
	private static Task OnlyTheGizmosGroup(
		ScenarioContext context
	)
	{
		var marble = DefDatabase<ThingDef>.GetNamed("BlocksMarble");
		context.Place(Steel, 0, 3, stackCount: Stack);
		var granite = new[] { context.Blueprint(ThingDefOf.Wall, -2, 0, Granite), context.Blueprint(ThingDefOf.Wall, -1, 0, Granite) };
		var marbles = new[] { context.Blueprint(ThingDefOf.Wall, 1, 0, marble), context.Blueprint(ThingDefOf.Wall, 2, 0, marble) };
		var all = granite.Concat(marbles).ToArray();
		Stage.Select(all.Cast<Thing>().ToArray());

		var shown = Stage.SelectionGizmos().OfType<Command_Action_AlternativeMaterial>().ToList();
		context.Check($"granite and marble walls show two Select Material gizmos, not {shown.Count}", shown.Count == 2);

		ChangeMaterial(context, granite[0], Steel.LabelAsStuff, all);
		context.Check($"the granite walls are now steel: [{Stuffs(granite)}]", granite.All(wall => wall.stuffToUse == Steel));
		context.Check($"the marble walls are still marble: [{Stuffs(marbles)}]", marbles.All(wall => wall.stuffToUse == marble));
		return Task.CompletedTask;
	}

	private static Task UnselectedUntouched(
		ScenarioContext context
	)
	{
		context.Place(Granite, 0, 3, stackCount: Stack);
		var selected = context.Blueprint(ThingDefOf.Wall, -1, 0, Wood);
		var unselected = context.Blueprint(ThingDefOf.Wall, 1, 0, Wood);
		ChangeMaterial(context, selected, Granite.LabelAsStuff, selected);
		context.Check($"the selected wall is now granite: {selected.stuffToUse?.defName}", selected.stuffToUse == Granite);
		context.Check($"the unselected wall is still wood: {unselected.stuffToUse?.defName}", unselected.stuffToUse == Wood);
		return Task.CompletedTask;
	}

	// A colonist already carrying wood to the wall drops that job when the wall changes to steel, and fetches steel instead
	private static async Task HaulersReplanned(
		ScenarioContext context
	)
	{
		context.Place(Wood, -8, 0, stackCount: Stack);
		context.Place(Steel, -8, 2, stackCount: Stack);
		var wall = context.Blueprint(ThingDefOf.Wall, 10, 0, Wood);
		var worker = context.Worker(-10, 0, WorkTypeDefOf.Construction);

		bool Bringing(
			ThingDef stuff
		)
		{
			return worker.CurJobDef == JobDefOf.HaulToContainer
			       && worker.CurJob.targetB.Thing == wall
			       && worker.carryTracker.CarriedThing?.def == stuff;
		}

		if (!await context.Until($"{Stage.Describe(worker)} carries wood to the wall", () => Bringing(Wood), 2500))
		{
			return;
		}

		context.Check("the wood is on its way to the wall", worker.Map.enrouteManager.GetEnroute(wall, Wood, null) > 0);

		ChangeMaterial(context, wall, Steel.LabelAsStuff, wall);
		context.Check($"the wall is now steel: {wall.stuffToUse?.defName}", wall.stuffToUse == Steel);
		await context.Until($"{Stage.Describe(worker)} stops bringing wood", () => !Bringing(Wood), 30);
		context.Check("no wood is on its way to the wall", worker.Map.enrouteManager.GetEnroute(wall, Wood, null) == 0);
		await context.Until($"{Stage.Describe(worker)} brings steel instead", () => Bringing(Steel) || wall.Destroyed, 2500);
	}

	private static Task DisabledWithoutAlternative(
		ScenarioContext context
	)
	{
		var wall = context.Blueprint(ThingDefOf.Wall, 0, 0, Wood);
		WithGodMode(
			false,
			() =>
			{
				var gizmo = MaterialGizmo(context, wall);
				var reason = "Ordo_BlueprintMaterials_OnlyOneMaterial".Translate().ToString();
				context.Check($"with nothing else on the map, the gizmo is disabled with \"{reason}\": disabled={gizmo?.Disabled} reason={gizmo?.disabledReason}", gizmo is { Disabled: true } && gizmo.disabledReason == reason);
			}
		);
		return Task.CompletedTask;
	}

	private static Task ListsWhatIsOnTheMap(
		ScenarioContext context
	)
	{
		context.Place(Steel, 0, 3, stackCount: Stack);
		var wall = context.Blueprint(ThingDefOf.Wall, 0, 0, Wood);
		WithGodMode(
			false,
			() =>
			{
				Stage.Select(wall);
				var gizmo = MaterialGizmo(context, wall);
				if (gizmo == null)
				{
					return;
				}

				var menu = context.OpenMenu(gizmo);
				var labels = Stage.MenuLabels(menu);
				var expected = new[] { Wood.LabelAsStuff, Steel.LabelAsStuff };
				context.Check($"with steel on the map, the menu lists the current wood and steel only: [{string.Join(", ", labels)}]", labels.Count == 2 && expected.All(labels.Contains));
				menu?.Close(false);
			}
		);
		return Task.CompletedTask;
	}

	private static Task GodModeListsEverything(
		ScenarioContext context
	)
	{
		var wall = context.Blueprint(ThingDefOf.Wall, 0, 0, Wood);
		WithGodMode(
			true,
			() =>
			{
				Stage.Select(wall);
				var gizmo = MaterialGizmo(context, wall);
				context.Check($"in god mode, the gizmo is enabled with nothing on the map: {gizmo?.disabledReason}", gizmo is { Disabled: false });
				if (gizmo is not { Disabled: false })
				{
					return;
				}

				var menu = context.OpenMenu(gizmo);
				var labels = Stage.MenuLabels(menu);
				var allowed = GenStuff.AllowedStuffsFor(ThingDefOf.Wall).ToList();
				context.Check($"in god mode, the menu lists all {allowed.Count} materials a wall allows, including granite: [{string.Join(", ", labels)}]", labels.Count == allowed.Count && labels.Contains(Granite.LabelAsStuff));
				menu?.Close(false);
			}
		);
		return Task.CompletedTask;
	}

	private static ThingDef Material(
		TerrainDef terrain
	)
	{
		return terrain.CostList?.FirstOrDefault()?.thingDef;
	}

	// The floors that share a terrain's dropdown and have a material, as the mod lists them
	private static List<TerrainDef> Group(
		TerrainDef terrain
	)
	{
		return DefDatabase<TerrainDef>.AllDefsListForReading.Where(def => def.designatorDropdown == terrain.designatorDropdown && Material(def) != null).ToList();
	}

	private static IEnumerable<List<TerrainDef>> FloorGroups()
	{
		return DefDatabase<TerrainDef>.AllDefsListForReading
			.Where(def => def.BuildableByPlayer && def.designatorDropdown != null && Material(def) != null)
			.GroupBy(def => def.designatorDropdown)
			.Select(group => Group(group.First()))
			.Where(group => group.Count >= 2);
	}

	// Changes two floor blueprints to a floor from the same group made of another material, which replaces them
	private static Task FloorChange(
		ScenarioContext context
	)
	{
		var pair = FloorGroups()
			.Select(group => (from: group[0], to: group.FirstOrDefault(def => Material(def) != Material(group[0]) && group.Count(other => Material(other) == Material(def)) == 1)))
			.FirstOrDefault(candidate => candidate.to != null);
		if (pair.to == null)
		{
			context.Fail("no floor dropdown group has floors of two materials");
			return Task.CompletedTask;
		}

		var (from, to) = pair;
		context.Place(Material(to), 0, 3, stackCount: Stack);
		var floors = new[] { context.Blueprint(from, -1, 0), context.Blueprint(from, 1, 0) };
		var cells = floors.Select(floor => floor.Position).ToList();
		ChangeMaterial(context, floors[0], Material(to).LabelAsStuff, floors);

		context.Check($"the old {from.defName} blueprints are gone", floors.All(floor => floor.Destroyed));
		foreach (var cell in cells)
		{
			var replaced = cell.GetThingList(context.Map).OfType<Blueprint_Build>().FirstOrDefault();
			context.Check($"{cell} has a {to.defName} blueprint: {replaced?.def.entityDefToBuild.defName ?? "none"}", replaced?.def.entityDefToBuild == to);
			context.Check($"the new blueprint at {cell} is selected", replaced != null && Find.Selector.IsSelected(replaced));
		}

		return Task.CompletedTask;
	}

	// Floors that differ only by colour, such as carpets, are offered by their own names, not all by their one material
	private static Task LabelsByTerrain(
		ScenarioContext context
	)
	{
		var group = FloorGroups().FirstOrDefault(candidate => candidate.GroupBy(Material).Any(same => same.Count() > 1));
		if (group == null)
		{
			context.Fail("no floor dropdown group has floors that share a material");
			return Task.CompletedTask;
		}

		var floor = group.GroupBy(Material).First(same => same.Count() > 1).First();
		context.Place(Material(floor), 0, 3, stackCount: Stack);
		var blueprint = context.Blueprint(floor, 0, 0);
		Stage.Select(blueprint);
		var gizmo = MaterialGizmo(context, blueprint);
		if (gizmo == null)
		{
			return Task.CompletedTask;
		}

		var menu = context.OpenMenu(gizmo);
		var labels = Stage.MenuLabels(menu);
		var sharing = group.Where(def => Material(def) == Material(floor)).Select(def => def.LabelCap.ToString()).ToList();
		context.Check($"every {Material(floor).defName} floor in {floor.designatorDropdown.defName} is offered by its own name: [{string.Join(", ", labels)}]", sharing.All(labels.Contains));
		context.Check($"no option is just the material's name, {Material(floor).LabelAsStuff}", !labels.Contains(Material(floor).LabelAsStuff));
		menu?.Close(false);
		return Task.CompletedTask;
	}
}
