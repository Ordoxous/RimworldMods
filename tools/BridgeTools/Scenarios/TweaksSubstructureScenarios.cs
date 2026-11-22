using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// ReplaceSubstructure: Transparent Substructure's substructure built over vanilla's, swapped in place with the old one's
// materials dropped. Needs a grav engine for the footprint; the substructure is laid directly and removed afterwards, since
// stage cleanup only restores the top terrain.
public static partial class TweaksScenarios
{
    private const string TransparentSubstructureId = "Aelanna.TransparentSubstructure";

    private static async Task ReplaceSubstructureScenario(
        ScenarioContext context,
        bool on
    )
    {
        var map = context.Map;
        var transparent = DefDatabase<TerrainDef>.GetNamed("TransparentFoundation_Substructure");
        var vanilla = TerrainDefOf.Substructure;
        var cells = Enumerable.Range(-6, 9).SelectMany(x => Enumerable.Range(-2, 5).Select(z => context.Cell(x, z))).ToList();
        foreach (var cell in cells)
        {
            map.terrainGrid.SetFoundation(cell, vanilla);
        }

        try
        {
            context.Build(ThingDefOf.GravEngine, -4, 0);
            var wall = context.Build(ThingDefOf.Wall, 2, 0);
            var open = context.Cell(1, 0);
            var walled = context.Cell(2, 0);

            await WithSetting("ReplaceSubstructure", on ? "true" : "false", async () =>
            {
                var openReport = GenConstruct.CanPlaceBlueprintAt(transparent, open, Rot4.North, map);
                var walledReport = GenConstruct.CanPlaceBlueprintAt(transparent, walled, Rot4.North, map);
                var sameReport = GenConstruct.CanPlaceBlueprintAt(vanilla, open, Rot4.North, map);
                context.Check($"vanilla over vanilla is still refused: {sameReport.Reason}", !sameReport.Accepted);
                if (!on)
                {
                    context.Check($"with the setting off, transparent over vanilla is refused: {openReport.Reason}", !openReport.Accepted);
                    return;
                }

                context.Check($"transparent can go over vanilla substructure: {openReport.Reason}", openReport.Accepted);
                context.Check($"transparent can go under a wall on vanilla substructure: {walledReport.Reason}", walledReport.Accepted);
                if (!openReport.Accepted || !walledReport.Accepted)
                    return;

                context.Blueprint(transparent, 1, 0);
                context.Blueprint(transparent, 2, 0);
                context.Place(ThingDefOf.Steel, 0, 2, stackCount: 8);
                context.Place(ThingDefOf.GravlitePanel, 1, 2, stackCount: 2);
                var worker = context.Worker(-1, 1, WorkTypeDefOf.Construction);
                worker.skills.GetSkill(SkillDefOf.Construction).Level = 10;

                await context.Until("both cells are transparent substructure",
                    () => map.terrainGrid.FoundationAt(open) == transparent && map.terrainGrid.FoundationAt(walled) == transparent, 20000);

                context.Check("the wall still stands", wall.Spawned && !wall.Destroyed);
                // The materials delivered went into the frames; what's on the map now is the old substructure's, dropped
                var panels = map.listerThings.ThingsOfDef(ThingDefOf.GravlitePanel).Where(t => t.Spawned).Sum(t => t.stackCount);
                var steel = map.listerThings.ThingsOfDef(ThingDefOf.Steel).Where(t => t.Spawned).Sum(t => t.stackCount);
                context.Check($"the old substructure's materials came back: {panels} gravlite panels (want 2), {steel} steel (want 8)", panels == 2 && steel == 8);
                var vanillaLeft = cells.Count(cell => map.terrainGrid.FoundationAt(cell) == vanilla);
                context.Check($"only the two cells changed: {vanillaLeft} vanilla cells left of {cells.Count}", vanillaLeft == cells.Count - 2);
            });
        }
        finally
        {
            foreach (var cell in cells.Where(cell => map.terrainGrid.FoundationAt(cell) != null))
            {
                map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
            }
        }
    }
}
