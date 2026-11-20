using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ordo.FloraTab;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// The survey and the row actions are the tab's whole logic; the window only draws their results, which is checked by eye in a
// windowed profile. Counts are compared against a survey taken before placing anything, so plants already on the map don't
// matter.
public static class FloraTabScenarios
{
    // Suites: "setup" for the button def, "survey" for what the rows count, "filter" for the toolbar's toggles, "designate"
    // for the Harvest and Cancel buttons
    public static readonly List<Scenario> All = new()
    {
        new("setup_main_button", "setup", MainButton),
        new("survey_counts_ready_soon_and_yield", "survey", CountsReadySoonAndYield),
        new("survey_skips_unharvestable_and_fogged", "survey", SkipsUnharvestableAndFogged),
        new("filter_trees", "filter", Trees),
        new("filter_cultivated", "filter", Cultivated),
        new("filter_only_ready", "filter", OnlyReady),
        new("designate_ready_and_cancel", "designate", DesignateReadyAndCancel),
        new("designate_trees", "designate", DesignateTrees),
        new("risk_old_age_and_dying", "risk", OldAgeAndDying),
        new("risk_forecast_first_hour_below", "risk", ForecastFirstHourBelow),
        new("risk_frost_kills_only_die_if_leafless", "risk", FrostKillsOnlyDieIfLeafless),
        new("growth_days_until_ready_matches_a_day", "growth", DaysUntilReadyMatchesADay),
        new("filter_area", "filter area", AreaFilter),
        new("filter_distance_and_nearest", "filter area", DistanceAndNearest)
    };

    private static readonly FloraFilter Wild = new();

    private static ThingDef Def(
        string defName
    ) => DefDatabase<ThingDef>.GetNamed(defName);

    private static FloraRow Row(
        ScenarioContext context,
        ThingDef def,
        FloraFilter filter
    ) => FloraSurvey.Build(context.Map, filter).FirstOrDefault(row => row.def == def);

    private static int Count(
        FloraRow row
    ) => row?.plants.Count ?? 0;

    private static Plant PlacePlant(
        ScenarioContext context,
        ThingDef def,
        int x,
        int z,
        float growth
    )
    {
        var plant = (Plant)context.Place(def, x, z);
        plant.Growth = growth;
        return plant;
    }

    private static Task MainButton(
        ScenarioContext context
    )
    {
        var def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Ordo_Flora");
        context.Check("the Ordo_Flora main button loaded", def != null);
        context.Check($"it opens the Flora window: {def?.tabWindowClass}", def?.tabWindowClass == typeof(MainTabWindow_Flora));
        context.Check($"it sits just after Wildlife: {def?.order}", def != null && def.order > DefDatabase<MainButtonDef>.GetNamed("Wildlife").order);
        context.Check("the window is made", def?.TabWindow is MainTabWindow_Flora);
        return Task.CompletedTask;
    }

    private static Task CountsReadySoonAndYield(
        ScenarioContext context
    )
    {
        var berry = Def("Plant_Berry");
        var before = Row(context, berry, Wild);

        PlacePlant(context, berry, 0, 0, 1f);
        PlacePlant(context, berry, 1, 0, 1f);
        PlacePlant(context, berry, 2, 0, 0.05f);

        var after = Row(context, berry, Wild);
        context.Check($"three more berry bushes: {Count(before)} -> {Count(after)}", Count(after) - Count(before) == 3);
        context.Check($"two more ready: {before?.ready ?? 0} -> {after?.ready}", (after?.ready ?? 0) - (before?.ready ?? 0) == 2);

        // Full growth and health give the def's yield, scaled by difficulty
        float expected = 2 * berry.plant.harvestYield * Find.Storyteller.difficulty.cropYieldFactor;
        float gained = (after?.yieldNow ?? 0f) - (before?.yieldNow ?? 0f);
        context.Check($"yield now grew by two full harvests: {gained:F2}, expected {expected:F2}", System.Math.Abs(gained - expected) < 0.01f);
        context.Check($"the product is berries: {after?.Product}", after?.Product == berry.plant.harvestedThingDef);
        return Task.CompletedTask;
    }

    private static Task SkipsUnharvestableAndFogged(
        ScenarioContext context
    )
    {
        var grass = Def("Plant_Grass");
        PlacePlant(context, grass, 0, 0, 1f);
        context.Check("grass gives nothing, so it has no row", Row(context, grass, Wild) == null);

        var healroot = Def("Plant_HealrootWild");
        var before = Count(Row(context, healroot, Wild));
        var plant = PlacePlant(context, healroot, 1, 0, 1f);
        context.Check("visible wild healroot is listed", Count(Row(context, healroot, Wild)) == before + 1);

        bool godMode = DebugSettings.godMode;
        DebugSettings.godMode = false;
        context.Map.fogGrid.Refog(CellRect.SingleCell(plant.Position));
        int fogged = Count(Row(context, healroot, Wild));
        context.Map.fogGrid.Unfog(plant.Position);
        DebugSettings.godMode = godMode;
        context.Check($"fogged healroot is hidden: {before} -> {fogged}", fogged == before);
        return Task.CompletedTask;
    }

    private static Task Trees(
        ScenarioContext context
    )
    {
        var oak = Def("Plant_TreeOak");
        var before = Count(Row(context, oak, new FloraFilter { includeTrees = true }));
        PlacePlant(context, oak, 0, 0, 1f);
        context.Check("trees are left out by default", Row(context, oak, Wild) == null);
        context.Check("trees are listed with the toggle",
            Count(Row(context, oak, new FloraFilter { includeTrees = true })) == before + 1);
        return Task.CompletedTask;
    }

    private static Task Cultivated(
        ScenarioContext context
    )
    {
        var potato = Def("Plant_Potato");
        var before = Count(Row(context, potato, Wild));
        var plant = PlacePlant(context, potato, 0, 0, 1f);
        context.Check("a potato outside a growing zone counts as wild", Count(Row(context, potato, Wild)) == before + 1);

        var zone = new Zone_Growing(context.Map.zoneManager);
        context.Map.zoneManager.RegisterZone(zone);
        try
        {
            zone.AddCell(plant.Position);
            zone.SetPlantDefToGrow(potato);
            context.Check("in a potato zone it is cultivated and left out", Count(Row(context, potato, Wild)) == before);
            context.Check("listed with the Cultivated toggle",
                Count(Row(context, potato, new FloraFilter { includeCultivated = true })) == before + 1);
        }
        finally
        {
            zone.Delete();
        }
        return Task.CompletedTask;
    }

    private static Task OnlyReady(
        ScenarioContext context
    )
    {
        var healroot = Def("Plant_HealrootWild");
        var onlyReady = new FloraFilter { onlyReady = true };
        if (Row(context, healroot, Wild)?.ready > 0)
        {
            context.Check("the map already has ready wild healroot, so this can't be tested here", false);
            return Task.CompletedTask;
        }

        var plant = PlacePlant(context, healroot, 0, 0, 0.05f);
        context.Check("a kind with nothing ready is hidden", Row(context, healroot, onlyReady) == null);
        plant.Growth = 1f;
        context.Check("and shown once one is ready", Row(context, healroot, onlyReady)?.ready > 0);
        return Task.CompletedTask;
    }

    private static Task DesignateReadyAndCancel(
        ScenarioContext context
    )
    {
        var berry = Def("Plant_Berry");
        var ready = PlacePlant(context, berry, 0, 0, 1f);
        var young = PlacePlant(context, berry, 1, 0, 0.05f);
        var designations = context.Map.designationManager;

        // A cut designation is replaced by harvest, as with vanilla's designator
        designations.AddDesignation(new Designation(ready, DesignationDefOf.CutPlant));
        int marked = FloraDesignations.HarvestReady(Row(context, berry, Wild), context.Map);
        context.Check($"marked at least the ready bush: {marked}", marked >= 1);
        context.Check("the ready bush is marked for harvest", designations.DesignationOn(ready, DesignationDefOf.HarvestPlant) != null);
        context.Check("its cut designation is gone", designations.DesignationOn(ready, DesignationDefOf.CutPlant) == null);
        context.Check("the young bush isn't marked", designations.DesignationOn(young, DesignationDefOf.HarvestPlant) == null);
        context.Check("the row counts it as marked", Row(context, berry, Wild)?.marked >= 1);

        int again = FloraDesignations.HarvestReady(Row(context, berry, Wild), context.Map);
        context.Check($"a second click marks nothing new: {again}", again == 0);

        int cancelled = FloraDesignations.Cancel(Row(context, berry, Wild), context.Map);
        context.Check($"cancel removed them: {cancelled}", cancelled >= 1);
        context.Check("the ready bush is no longer marked", designations.DesignationOn(ready, DesignationDefOf.HarvestPlant) == null);
        return Task.CompletedTask;
    }

    private static Task DesignateTrees(
        ScenarioContext context
    )
    {
        var oak = Def("Plant_TreeOak");
        var tree = PlacePlant(context, oak, 0, 0, 1f);
        var trees = new FloraFilter { includeTrees = true };
        FloraDesignations.HarvestReady(Row(context, oak, trees), context.Map);
        context.Check("a grown tree is marked to chop for wood",
            context.Map.designationManager.DesignationOn(tree, DesignationDefOf.HarvestPlant) != null);
        FloraDesignations.Cancel(Row(context, oak, trees), context.Map);
        return Task.CompletedTask;
    }

    // Wild plants die of old age, not frost: one about to, one already rotting
    private static Task OldAgeAndDying(
        ScenarioContext context
    )
    {
        var berry = Def("Plant_Berry");
        int lifespan = berry.plant.LifespanTicks;
        var aging = PlacePlant(context, berry, 0, 0, 1f);
        aging.ageInt = lifespan - 2 * GenDate.TicksPerDay;
        var old = PlacePlant(context, berry, 1, 0, 1f);
        old.ageInt = lifespan + 1;

        var row = Row(context, berry, Wild);
        context.Check($"the old bush is dying: {old.Dying}", old.Dying);
        context.Check("both are at risk", row != null && row.atRisk.Contains(aging) && row.atRisk.Contains(old));
        context.Check($"counted as dying {row?.dying} and old age {row?.oldAge}", row?.dying >= 1 && row.oldAge >= 1);
        context.Check($"old age in about 2 days: {row?.oldAgeDays:F2}", row != null && row.oldAgeDays <= 2.01f);
        context.Check($"days until old age is 2: {FloraSurvey.DaysUntilOldAge(aging):F2}",
            System.Math.Abs(FloraSurvey.DaysUntilOldAge(aging) - 2f) < 0.01f);
        return Task.CompletedTask;
    }

    private static Task ForecastFirstHourBelow(
        ScenarioContext context
    )
    {
        var forecast = new MapForecast(new[] { 10f, 5f, -3f, 8f, -20f });
        context.Check("first below 6 is hour 1", forecast.FirstHourBelow(6f) == 1);
        context.Check("first below 0 is hour 2", forecast.FirstHourBelow(0f) == 2);
        context.Check("first below -10 is hour 4", forecast.FirstHourBelow(-10f) == 4);
        context.Check("nothing below -30", forecast.FirstHourBelow(-30f) == -1);
        context.Check("hour 0 when it's already colder", forecast.FirstHourBelow(20f) == 0);

        var map = MapForecast.For(context.Map);
        context.Check("the map's forecast has no hour below -500", map.FirstHourBelow(-500f) == -1);
        return Task.CompletedTask;
    }

    // A potato (dieIfLeafless) outdoors dies at the first hour colder than its leafless threshold; a berry bush doesn't
    private static Task FrostKillsOnlyDieIfLeafless(
        ScenarioContext context
    )
    {
        var hourly = Enumerable.Repeat(20f, MapForecast.Hours).ToArray();
        hourly[12] = -100f;
        var forecast = new MapForecast(hourly);

        var potato = PlacePlant(context, Def("Plant_Potato"), 0, 0, 0.5f);
        var berry = PlacePlant(context, Def("Plant_Berry"), 1, 0, 0.5f);
        context.Check($"a potato dies of frost: {potato.def.plant.dieIfLeafless}", potato.def.plant.dieIfLeafless);
        context.Check($"a berry bush doesn't: {berry.def.plant.dieIfLeafless}", !berry.def.plant.dieIfLeafless);
        context.Check($"the outdoor potato is killed at hour 12: {forecast.DaysUntilKilled(potato):F2} days",
            System.Math.Abs(forecast.DaysUntilKilled(potato) - 0.5f) < 0.01f);
        context.Check("the berry bush isn't", float.IsPositiveInfinity(forecast.DaysUntilKilled(berry)));

        var warm = new MapForecast(Enumerable.Repeat(20f, MapForecast.Hours).ToArray());
        context.Check("no frost, no death", float.IsPositiveInfinity(warm.DaysUntilKilled(potato)));
        return Task.CompletedTask;
    }

    // The estimate's growth per day against a real day of ticks
    private static async Task DaysUntilReadyMatchesADay(
        ScenarioContext context
    )
    {
        var berry = Def("Plant_Berry");
        var ready = PlacePlant(context, berry, 0, 0, 1f);
        context.Check("a ready bush is ready in 0 days", FloraSurvey.DaysUntilReady(ready) == 0f);

        var young = PlacePlant(context, berry, 1, 0, 0.2f);
        float days = FloraSurvey.DaysUntilReady(young);
        if (float.IsPositiveInfinity(days))
        {
            context.Check($"the young bush isn't growing here (growth rate {young.GrowthRate:F2}), so this can't be tested", false);
            return;
        }

        // Growth is applied every 2000 ticks with that moment's rate, and the weather dims the sun, so this allows 25% either way
        float predicted = MapForecast.For(context.Map).GrowthOver(young, 24);
        float before = young.Growth;
        // In plant-tick steps: one Step of a whole day grew the bush only half as much (see the README)
        for (int i = 0; i < GenDate.TicksPerDay / 2000; i++)
            await context.Step(2000);
        float actual = young.Growth - before;
        float ratio = actual / predicted;
        context.Check($"a day's growth {actual:F4} is close to the forecast's {predicted:F4} (ratio {ratio:F2}); "
            + $"ready in {days:F1} days", ratio > 0.75f && ratio < 1.25f);
    }

    private static Task AreaFilter(
        ScenarioContext context
    )
    {
        var berry = Def("Plant_Berry");
        var inside = PlacePlant(context, berry, 0, 0, 1f);
        var outside = PlacePlant(context, berry, 2, 0, 1f);
        Area home = context.Map.areaManager.Home;
        bool insideWas = home[inside.Position], outsideWas = home[outside.Position];
        try
        {
            home[inside.Position] = true;
            home[outside.Position] = false;
            var row = Row(context, berry, new FloraFilter { area = home });
            context.Check("the bush in the home area is listed", row != null && row.plants.Contains(inside));
            context.Check("the one outside isn't", row == null || !row.plants.Contains(outside));

            int marked = FloraDesignations.HarvestReady(row, context.Map);
            context.Check($"Harvest marks only the one inside: {marked}",
                context.Map.designationManager.DesignationOn(outside, DesignationDefOf.HarvestPlant) == null);
            FloraDesignations.Cancel(row, context.Map);
        }
        finally
        {
            home[inside.Position] = insideWas;
            home[outside.Position] = outsideWas;
        }
        return Task.CompletedTask;
    }

    private static Task DistanceAndNearest(
        ScenarioContext context
    )
    {
        var map = context.Map;
        IntVec3 center = FloraSurvey.BaseCenter(map);
        IntVec3 nearCell = center + new IntVec3(1, 0, 0);
        IntVec3 farCell = center + new IntVec3(12, 0, 0);
        if (!nearCell.InBounds(map) || !farCell.InBounds(map))
        {
            context.Check($"the map is too small around the base at {center}", false);
            return Task.CompletedTask;
        }

        // Place takes offsets from the scenario's origin
        IntVec3 origin = context.Cell(0, 0);
        var agave = Def("Plant_Agave");
        var near = PlacePlant(context, agave, nearCell.x - origin.x, nearCell.z - origin.z, 1f);
        var far = PlacePlant(context, agave, farCell.x - origin.x, farCell.z - origin.z, 1f);

        var all = Row(context, agave, Wild);
        context.Check($"nearest is 1 cell away: {all?.nearest:F2}", all != null && all.nearest <= 1.01f);

        var close = Row(context, agave, new FloraFilter { maxDistance = 5 });
        context.Check("within 5 cells lists the near agave", close != null && close.plants.Contains(near));
        context.Check("but not the far one", close == null || !close.plants.Contains(far));
        return Task.CompletedTask;
    }
}
