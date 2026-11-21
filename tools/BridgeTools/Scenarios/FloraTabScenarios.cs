using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ordo.FloraTab;
using RimWorld;
using UnityEngine;
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
        new("filter_distance_and_nearest", "filter area", DistanceAndNearest),
        new("perf_survey_and_forecast", "perf", SurveyAndForecastCost),
        new("minerals_ore_kinds", "minerals", MineralsSetup),
        new("minerals_counts_veins_yield_and_overhead", "minerals", MineralsCountsVeinsYieldAndOverhead),
        new("minerals_fogged_hidden", "minerals", MineralsFoggedHidden),
        new("minerals_designate_and_cancel", "minerals", MineralsDesignateAndCancel),
        new("minerals_area", "minerals", MineralsArea),
        new("minerals_distance_and_nearest", "minerals", MineralsDistanceAndNearest),
        new("perf_mineral_survey", "perf", MineralSurveyCost),
        new("animals_lists_colony_animals_and_searches", "animals", AnimalsListAndSearch),
        new("animals_hotkey_opens_the_page", "animals", AnimalsHotkey),
        new("animals_hosts_animal_tab", "animals", HostsAnimalTab, requiresMod: "fluffy.animaltab"),
        new("wildlife_lists_wild_animals_and_searches", "animals", WildlifeListAndSearch),
        new("wildlife_hotkey_opens_the_page", "animals", WildlifeHotkey)
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
        context.Check($"it opens the Nature window: {def?.tabWindowClass}", def?.tabWindowClass == typeof(MainTabWindow_Flora));
        var animals = DefDatabase<MainButtonDef>.GetNamed("Animals");
        context.Check($"it takes the Animals button's place: {def?.order} vs {animals.order}", def != null && def.order == animals.order);
        foreach (var moved in new[] { animals, DefDatabase<MainButtonDef>.GetNamed("Wildlife") })
        {
            context.Check($"the {moved.defName} button is hidden", !moved.buttonVisible);
            context.Check($"the {moved.defName} hotkey opens its page: {moved.workerClass}", moved.Worker is MainButtonWorker_NaturePage);
        }
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

        // Hidden even in god mode, which shows the fogged map
        bool godMode = DebugSettings.godMode;
        DebugSettings.godMode = true;
        context.Map.fogGrid.Refog(CellRect.SingleCell(plant.Position));
        int fogged = Count(Row(context, healroot, Wild));
        context.Map.fogGrid.Unfog(plant.Position);
        DebugSettings.godMode = godMode;
        context.Check($"fogged healroot is hidden, even in god mode: {before} -> {fogged}", fogged == before);
        return Task.CompletedTask;
    }

    private static readonly MineralFilter AllMinerals = new();

    private static MineralRow MineralRow(
        ScenarioContext context,
        ThingDef def,
        MineralFilter filter
    ) => MineralSurvey.Build(context.Map, filter).FirstOrDefault(row => row.def == def);

    private static Task MineralsSetup(
        ScenarioContext context
    )
    {
        var defs = MineralSurvey.OreDefs.Select(d => d.defName).ToList();
        foreach (var expected in new[] { "MineableSteel", "MineableGold", "MineablePlasteel", "MineableComponentsIndustrial" })
            context.Check($"{expected} is an ore kind", defs.Contains(expected));
        context.Check("granite isn't", !defs.Contains("Granite"));
        return Task.CompletedTask;
    }

    // Two touching cells and one apart make two veins; full-health cells give the def's whole yield; one is under overhead
    // mountain
    private static Task MineralsCountsVeinsYieldAndOverhead(
        ScenarioContext context
    )
    {
        var steel = Def("MineableSteel");
        var before = MineralRow(context, steel, AllMinerals);
        var a = context.Place(steel, 0, 0);
        context.Place(steel, 1, 0);
        context.Place(steel, 4, 0);
        var roofGrid = context.Map.roofGrid;
        RoofDef roofWas = roofGrid.RoofAt(a.Position);
        roofGrid.SetRoof(a.Position, RoofDefOf.RoofRockThick);
        try
        {
            var after = MineralRow(context, steel, AllMinerals);
            int cells = (after?.cells.Count ?? 0) - (before?.cells.Count ?? 0);
            int veins = (after?.veins.Count ?? 0) - (before?.veins.Count ?? 0);
            context.Check($"three more cells: {cells}", cells == 3);
            context.Check($"two more veins: {veins}", veins == 2);

            float expected = 3 * Mathf.Max(1, steel.building.EffectiveMineableYield) * steel.building.mineableDropChance;
            float gained = (after?.yield ?? 0f) - (before?.yield ?? 0f);
            context.Check($"yield grew by three cells' worth: {gained:F2}, expected {expected:F2}", Mathf.Abs(gained - expected) < 0.01f);
            context.Check($"the product is steel: {after?.Product}", after?.Product == ThingDefOf.Steel);
            context.Check($"one more under overhead mountain: {before?.overhead ?? 0} -> {after?.overhead}",
                (after?.overhead ?? 0) - (before?.overhead ?? 0) == 1 && after.overheadCells.Contains((Mineable)a));

            // A cell half mined by a 100% miner has half its yield banked and half still in the rock
            var mineable = (Mineable)a;
            mineable.HitPoints = mineable.MaxHitPoints / 2;
            mineable.yieldPct = 0.5f;
            context.Check($"a half-mined cell still expects its whole yield: {MineralSurvey.ExpectedYield(mineable):F2}",
                Mathf.Abs(MineralSurvey.ExpectedYield(mineable) - expected / 3) < 0.5f);
        }
        finally
        {
            roofGrid.SetRoof(a.Position, roofWas);
        }
        return Task.CompletedTask;
    }

    private static Task MineralsFoggedHidden(
        ScenarioContext context
    )
    {
        var gold = Def("MineableGold");
        int before = MineralRow(context, gold, AllMinerals)?.cells.Count ?? 0;
        var cell = context.Place(gold, 0, 0);
        context.Check("visible gold is listed", MineralRow(context, gold, AllMinerals)?.cells.Count == before + 1);

        bool godMode = DebugSettings.godMode;
        DebugSettings.godMode = true;
        context.Map.fogGrid.Refog(CellRect.SingleCell(cell.Position));
        int fogged = MineralRow(context, gold, AllMinerals)?.cells.Count ?? 0;
        context.Map.fogGrid.Unfog(cell.Position);
        DebugSettings.godMode = godMode;
        context.Check($"fogged gold is hidden, even in god mode: {before} -> {fogged}", fogged == before);
        return Task.CompletedTask;
    }

    private static Task MineralsDesignateAndCancel(
        ScenarioContext context
    )
    {
        var silver = Def("MineableSilver");
        var a = context.Place(silver, 0, 0);
        var b = context.Place(silver, 1, 0);
        var designations = context.Map.designationManager;
        // A cell marked for vein mining already counts as marked, and isn't marked again
        designations.AddDesignation(new Designation(b.Position, DesignationDefOf.MineVein));

        bool godMode = DebugSettings.godMode;
        DebugSettings.godMode = false;
        int marked = MineralDesignations.MineAll(MineralRow(context, silver, AllMinerals), context.Map);
        DebugSettings.godMode = godMode;
        context.Check($"marked at least the unmarked cell: {marked}", marked >= 1);
        context.Check("it is marked to mine", designations.DesignationAt(a.Position, DesignationDefOf.Mine) != null);
        context.Check("the vein-marked cell keeps its vein designation only",
            designations.DesignationAt(b.Position, DesignationDefOf.MineVein) != null
            && designations.DesignationAt(b.Position, DesignationDefOf.Mine) == null);
        context.Check("the cells are still there", a.Spawned && b.Spawned);
        var row = MineralRow(context, silver, AllMinerals);
        context.Check($"the row counts both as marked: {row?.marked}", row?.marked >= 2);

        int again = MineralDesignations.MineAll(row, context.Map);
        context.Check($"a second click marks nothing new: {again}", again == 0);

        int cancelled = MineralDesignations.Cancel(row, context.Map);
        context.Check($"cancel removed both kinds: {cancelled}", cancelled >= 2);
        context.Check("nothing left marked", !MineralSurvey.IsMarked(designations, a.Position) && !MineralSurvey.IsMarked(designations, b.Position));
        return Task.CompletedTask;
    }

    private static Task MineralsArea(
        ScenarioContext context
    )
    {
        var jade = Def("MineableJade");
        var inside = context.Place(jade, 0, 0);
        var outside = context.Place(jade, 3, 0);
        Area home = context.Map.areaManager.Home;
        bool insideWas = home[inside.Position], outsideWas = home[outside.Position];
        try
        {
            home[inside.Position] = true;
            home[outside.Position] = false;
            var row = MineralRow(context, jade, new MineralFilter { area = home });
            context.Check("the jade in the home area is listed", row != null && row.cells.Contains((Mineable)inside));
            context.Check("the jade outside isn't", row == null || !row.cells.Contains((Mineable)outside));
        }
        finally
        {
            home[inside.Position] = insideWas;
            home[outside.Position] = outsideWas;
        }
        return Task.CompletedTask;
    }

    private static Task MineralsDistanceAndNearest(
        ScenarioContext context
    )
    {
        var jade = Def("MineableJade");

        // Place takes offsets from the scenario's origin; these go 1 and 12 cells from the base
        var map = context.Map;
        IntVec3 center = FloraSurvey.BaseCenter(map);
        IntVec3 nearCell = center + new IntVec3(1, 0, 0);
        IntVec3 farCell = center + new IntVec3(12, 0, 0);
        if (!nearCell.InBounds(map) || !farCell.InBounds(map) || nearCell.GetEdifice(map) != null || farCell.GetEdifice(map) != null)
        {
            context.Check($"the cells 1 and 12 east of the base at {center} aren't free", false);
            return Task.CompletedTask;
        }
        IntVec3 origin = context.Cell(0, 0);
        var near = (Mineable)context.Place(jade, nearCell.x - origin.x, nearCell.z - origin.z);
        var far = (Mineable)context.Place(jade, farCell.x - origin.x, farCell.z - origin.z);

        // Rock spawned beside a colonist pushes them aside, and with no home area the base is the colonists' middle, so
        // distances are taken from where the base is now
        IntVec3 now = FloraSurvey.BaseCenter(map);
        float nearDistance = (near.Position - now).LengthHorizontal;
        var all = MineralRow(context, jade, AllMinerals);
        context.Check($"nearest is the near jade's {nearDistance:F2} cells: {all?.nearest:F2}",
            all != null && all.nearest <= nearDistance + 0.01f);
        var close = MineralRow(context, jade, new MineralFilter { maxDistance = Mathf.CeilToInt(nearDistance) + 2 });
        context.Check("within 5 cells lists the near jade", close != null && close.cells.Contains(near));
        context.Check("but not the far one", close == null || !close.cells.Contains(far));
        return Task.CompletedTask;
    }

    // Like the plant survey's timing: the mineral survey runs once a second while its page is open
    private static Task MineralSurveyCost(
        ScenarioContext context
    )
    {
        const int runs = 20;
        var rows = MineralSurvey.Build(context.Map, AllMinerals);
        int cells = rows.Sum(row => row.cells.Count);
        double avg = 0, max = 0;
        var watch = new Stopwatch();
        for (var i = 0; i < runs; i++)
        {
            watch.Restart();
            MineralSurvey.Build(context.Map, AllMinerals);
            var ms = watch.Elapsed.TotalMilliseconds;
            avg += ms / runs;
            max = System.Math.Max(max, ms);
        }
        string line = $"mineral survey of {cells} visible ore cells in {rows.Count} kinds: avg {avg:F2} ms, max {max:F2} ms";
        Log.Message("[FloraTab perf] " + line);
        context.Check(line, avg < 10);
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

    // Times the open tab's work on whatever map the scenario runs on, so run it on a map with plenty of plants. DPA can't time
    // it: all of it runs from the window's OnGUI, which DPA's Update and Tick categories don't record. The tab surveys once a
    // second while open, and the forecast is rebuilt once an in-game hour.
    private static Task SurveyAndForecastCost(
        ScenarioContext context
    )
    {
        const int runs = 20;
        var all = new FloraFilter { includeTrees = true, includeCultivated = true };
        var cachedHour = typeof(MapForecast).GetField("cachedHour", BindingFlags.NonPublic | BindingFlags.Static);

        var rows = FloraSurvey.Build(context.Map, all);
        var plants = rows.Sum(row => row.plants.Count);

        double forecastAvg = 0, forecastMax = 0, coldAvg = 0, coldMax = 0, warmAvg = 0, warmMax = 0;
        var watch = new Stopwatch();
        for (var i = 0; i < runs; i++)
        {
            cachedHour.SetValue(null, -1);
            watch.Restart();
            MapForecast.For(context.Map);
            var ms = watch.Elapsed.TotalMilliseconds;
            forecastAvg += ms / runs;
            forecastMax = System.Math.Max(forecastMax, ms);

            cachedHour.SetValue(null, -1);
            watch.Restart();
            FloraSurvey.Build(context.Map, all);
            ms = watch.Elapsed.TotalMilliseconds;
            coldAvg += ms / runs;
            coldMax = System.Math.Max(coldMax, ms);

            watch.Restart();
            FloraSurvey.Build(context.Map, all);
            ms = watch.Elapsed.TotalMilliseconds;
            warmAvg += ms / runs;
            warmMax = System.Math.Max(warmMax, ms);
        }

        // Passing checks aren't reported, so the timings also go to the log
        string[] lines =
        {
            $"surveyed {plants} plants of {rows.Count} kinds, trees and cultivated included",
            $"forecast rebuild, once an in-game hour: avg {forecastAvg:F2} ms, max {forecastMax:F2} ms",
            $"survey with a new forecast, the first each hour: avg {coldAvg:F2} ms, max {coldMax:F2} ms",
            $"survey with the hour's forecast, once a second: avg {warmAvg:F2} ms, max {warmMax:F2} ms"
        };
        Log.Message("[FloraTab perf] " + string.Join("; ", lines));
        context.Check(lines[0], plants > 0);
        context.Check(lines[1], forecastAvg < 5);
        context.Check(lines[2], coldAvg < 20);
        context.Check(lines[3], warmAvg < 10);
        return Task.CompletedTask;
    }

    // The page lists what the vanilla Animals tab lists: the map's colony animals, not wild ones
    private static Task AnimalsListAndSearch(
        ScenarioContext context
    )
    {
        var page = new AnimalsPage();
        page.Survey(context.Map, null, 0);
        context.Check($"hosts a mod's window only when Animal Tab is loaded: {page.HostedWindow != null}",
            page.HostedWindow != null == ModsConfig.IsActive("fluffy.animaltab"));
        int before = page.Listed.Count;

        var tame = context.Spawn("Muffalo", 0, 0, PawnMode.Tamed);
        var wild = context.Spawn("Muffalo", 2, 0, PawnMode.Wild);
        var hen = context.Spawn("Chicken", 4, 0, PawnMode.Tamed);
        page.Survey(context.Map, null, 0);
        context.Check($"lists the two tame animals: {before} -> {page.Listed.Count}", page.Listed.Count - before == 2);
        context.Check("lists the tame muffalo", page.Listed.Contains(tame));
        context.Check("not the wild one", !page.Listed.Contains(wild));

        var search = new QuickSearchFilter { Text = "chicken" };
        page.SearchFor(search);
        context.Check($"searching \"chicken\" lists the hen and not the muffalo: {page.Listed.Count}",
            page.Listed.Contains(hen) && !page.Listed.Contains(tame));

        tame.Name = new NameSingle("Bessiezq");
        search.Text = "bessiezq";
        page.SearchFor(search);
        context.Check($"searching by name finds the muffalo: {page.Listed.Count}", page.Listed.Count == 1 && page.Listed[0] == tame);

        search.Text = "";
        page.SearchFor(search);
        context.Check($"clearing the search lists them all again: {page.Listed.Count}", page.Listed.Count - before == 2);
        page.Clear();
        return Task.CompletedTask;
    }

    // F4 goes to the hidden Animals button, whose worker opens the Nature tab on the Animals page and closes it again
    private static Task AnimalsHotkey(
        ScenarioContext context
    )
    {
        var nature = DefDatabase<MainButtonDef>.GetNamed("Ordo_Flora");
        var animals = DefDatabase<MainButtonDef>.GetNamed("Animals");
        var tabs = Find.MainTabsRoot;
        tabs.EscapeCurrentTab(false);

        animals.Worker.Activate();
        context.Check($"opens the Nature tab: {tabs.OpenTab?.defName}", tabs.OpenTab == nature);
        context.Check($"on the Animals page: {MainTabWindow_Flora.Page.GetType().Name}", MainTabWindow_Flora.Page is AnimalsPage);

        animals.Worker.Activate();
        context.Check($"pressing it again closes it: {tabs.OpenTab?.defName}", tabs.OpenTab == null);

        // Open on another page, the hotkey switches to Animals rather than closing
        tabs.SetCurrentTab(nature, false);
        ((MainTabWindow_Flora)nature.TabWindow).SwitchTo(0);
        context.Check($"another page shows first: {MainTabWindow_Flora.Page.GetType().Name}", MainTabWindow_Flora.Page is PlantsPage);
        animals.Worker.Activate();
        context.Check($"from another page it switches to Animals: {MainTabWindow_Flora.Page.GetType().Name}, open {tabs.OpenTab?.defName}",
            tabs.OpenTab == nature && MainTabWindow_Flora.Page is AnimalsPage);

        tabs.EscapeCurrentTab(false);
        return Task.CompletedTask;
    }

    // With Animal Tab, the page hosts its window: the button def's own instance, which its filters reach through
    // MainTabWindow_Animals.Instance. Turning its filter bar on makes the window build a new table, which must still get
    // the page's search.
    private static Task HostsAnimalTab(
        ScenarioContext context
    )
    {
        var page = new AnimalsPage();
        var hosted = page.HostedWindow;
        context.Check($"hosts Animal Tab's window: {hosted?.GetType().FullName}", page.HostedWindow != null
            && hosted?.GetType().FullName == "AnimalTab.MainTabWindow_Animals");
        var instance = hosted?.GetType().GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        context.Check("it is the window Animal Tab's filters use", instance != null && instance == hosted);

        var tame = context.Spawn("Muffalo", 0, 0, PawnMode.Tamed);
        page.Survey(context.Map, null, 0);
        context.Check($"lists the tame muffalo: {page.Listed.Count}", page.Listed.Contains(tame));
        // A headless game has no real screen size, so the size is checked only with a window
        if (UI.screenHeight >= 480)
            context.Check($"its window is sized: {page.Width:F0} x {page.Height:F0}", page.Width > 200f && page.Height > 50f);

        var filter = hosted!.GetType().GetProperty("Filter")!;
        var search = new QuickSearchFilter();
        try
        {
            filter.SetValue(hosted, true);
            page.Survey(context.Map, null, 0);
            context.Check($"with its filter bar on, the muffalo is still listed: {page.Listed.Count}", page.Listed.Contains(tame));

            search.Text = "zzzqx";
            page.SearchFor(search);
            context.Check($"the search still applies to its new table: {page.Listed.Count}", page.Listed.Count == 0);
        }
        finally
        {
            search.Text = "";
            page.SearchFor(search);
            filter.SetValue(hosted, false);
        }
        context.Check($"with the search cleared it's listed again: {page.Listed.Count}", page.Listed.Contains(tame));
        page.Clear();
        return Task.CompletedTask;
    }

    // The page lists what the vanilla Wildlife tab lists: wild animals in view, not the colony's
    private static Task WildlifeListAndSearch(
        ScenarioContext context
    )
    {
        var page = new WildlifePage();
        page.Survey(context.Map, null, 0);
        int before = page.Listed.Count;

        var wild = context.Spawn("Muffalo", 0, 0, PawnMode.Wild);
        var hare = context.Spawn("Hare", 2, 0, PawnMode.Wild);
        var tame = context.Spawn("Muffalo", 4, 0, PawnMode.Tamed);
        page.Survey(context.Map, null, 0);
        context.Check($"lists the two wild animals: {before} -> {page.Listed.Count}", page.Listed.Count - before == 2);
        context.Check("lists the wild muffalo", page.Listed.Contains(wild));
        context.Check("not the tame one", !page.Listed.Contains(tame));

        var search = new QuickSearchFilter { Text = "hare" };
        page.SearchFor(search);
        context.Check($"searching \"hare\" lists the hare and not the muffalo: {page.Listed.Count}",
            page.Listed.Contains(hare) && !page.Listed.Contains(wild));
        search.Text = "";
        page.SearchFor(search);
        page.Clear();
        return Task.CompletedTask;
    }

    // F5 goes to the hidden Wildlife button, which opens the Nature tab on the Wildlife page, and from Animals switches
    private static Task WildlifeHotkey(
        ScenarioContext context
    )
    {
        var nature = DefDatabase<MainButtonDef>.GetNamed("Ordo_Flora");
        var animals = DefDatabase<MainButtonDef>.GetNamed("Animals");
        var wildlife = DefDatabase<MainButtonDef>.GetNamed("Wildlife");
        var tabs = Find.MainTabsRoot;
        tabs.EscapeCurrentTab(false);

        wildlife.Worker.Activate();
        context.Check($"opens the Wildlife page: {tabs.OpenTab?.defName}, {MainTabWindow_Flora.Page.GetType().Name}",
            tabs.OpenTab == nature && MainTabWindow_Flora.Page is WildlifePage);
        animals.Worker.Activate();
        context.Check($"the Animals hotkey switches to Animals: {MainTabWindow_Flora.Page.GetType().Name}",
            tabs.OpenTab == nature && MainTabWindow_Flora.Page is AnimalsPage);
        wildlife.Worker.Activate();
        context.Check($"and F5 back to Wildlife: {MainTabWindow_Flora.Page.GetType().Name}",
            tabs.OpenTab == nature && MainTabWindow_Flora.Page is WildlifePage);
        wildlife.Worker.Activate();
        context.Check($"pressing it again closes it: {tabs.OpenTab?.defName}", tabs.OpenTab == null);
        return Task.CompletedTask;
    }
}
