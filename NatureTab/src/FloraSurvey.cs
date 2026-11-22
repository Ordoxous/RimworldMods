using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.NatureTab;

// One row of the tab: every listed plant of one kind on the map
public class FloraRow
{
    public ThingDef def;
    public readonly List<Plant> plants = new();
    public int ready;
    public int soon;
    public int marked;

    // Of the plant's own harvestedThingDef, from the ready plants; yields also holds any secondary outputs
    public float yieldNow;
    public readonly List<FloraYield> yields = new();

    // Growing plants: the soonest one to be ready, in days (infinity when none will be), and how many aren't growing now
    public float nextReadyDays = float.PositiveInfinity;
    public int stalled;

    // At risk: dying now (blight, old age, poison, light, vacuum, terrain), reaching old age within the forecast, or killed
    // by frost within it. Frost only kills plants with dieIfLeafless, which no vanilla wild plant has: wild plants go leafless
    // and stop growing, and old age is what kills them.
    public int dying;
    public int oldAge;
    public float oldAgeDays = float.PositiveInfinity;
    public int frost;
    public float frostDays = float.PositiveInfinity;
    public readonly HashSet<Plant> atRisk = new();

    // Growth stops (outdoors, below minGrowthTemperature) within the forecast, in days; infinity if not
    public float growthStopsDays = float.PositiveInfinity;

    // Straight-line cells from the base to the nearest plant
    public float nearest = float.PositiveInfinity;

    public bool IsTree => def.plant.IsTree;
    public ThingDef Product => def.plant.harvestedThingDef;
    public int AtRisk => dying + oldAge + frost;

    // What the ready plants' yields would sell for, all together, so rows giving different things can be sorted
    public float YieldValue
    {
        get
        {
            float value = 0f;
            foreach (FloraYield y in yields)
                value += y.now * y.Thing.BaseMarketValue;
            return value;
        }
    }
}

public struct FloraFilter
{
    public bool includeTrees;

    // Trees that give something besides wood, listed even with includeTrees off
    public bool includeTreesWithExtras;
    public bool includeCultivated;
    public bool onlyReady;

    // Hide kinds that give nothing when harvested: no harvestedThingDef and no secondary output
    public bool hideYieldless;

    // Only plants in this area, when set. It must belong to the surveyed map.
    public Area area;

    // Only plants within this many cells of the base, when above 0
    public int maxDistance;
}

// Builds the tab's rows from the map's HarvestablePlant lister group, which the game keeps up to date as plants
// spawn and despawn, so a survey walks only plants with a harvest product and never the whole map
public static class FloraSurvey
{
    private static readonly Dictionary<ThingDef, FloraRow> RowsByDef = new();

    public static List<FloraRow> Build(
        Map map,
        FloraFilter filter)
    {
        RowsByDef.Clear();
        DesignationManager designations = map.designationManager;
        Area area = filter.area?.Map == map ? filter.area : null;
        IntVec3 baseCenter = BaseCenter(map);
        float maxDistanceSquared = filter.maxDistance > 0 ? filter.maxDistance * filter.maxDistance : float.MaxValue;
        MapForecast forecast = MapForecast.For(map);

        foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HarvestablePlant))
        {
            if (thing is not Plant plant || !plant.Spawned)
                continue;
            ThingDef def = plant.def;
            if (def.plant.isStump || !def.selectable)
                continue;
            if (def.plant.IsTree && !filter.includeTrees && !(filter.includeTreesWithExtras && FloraYields.GivesMoreThanWood(def)))
                continue;
            if (filter.hideYieldless && FloraYields.For(def).Count == 0)
                continue;
            IntVec3 cell = plant.Position;
            // Never list what the player can't see, god mode or not
            if (cell.Fogged(map))
                continue;
            if (area != null && !area[cell])
                continue;
            float distanceSquared = (cell - baseCenter).LengthHorizontalSquared;
            if (distanceSquared > maxDistanceSquared)
                continue;
            if (!filter.includeCultivated && plant.DeliberatelyCultivated())
                continue;

            if (!RowsByDef.TryGetValue(def, out FloraRow row))
            {
                row = new FloraRow { def = def };
                foreach (YieldSource source in FloraYields.For(def))
                    row.yields.Add(new FloraYield { source = source });
                int stopHour = forecast.FirstHourBelow(def.plant.minGrowthTemperature);
                if (stopHour >= 0)
                    row.growthStopsDays = stopHour / 24f;
                RowsByDef[def] = row;
            }

            row.plants.Add(plant);
            row.nearest = Mathf.Min(row.nearest, Mathf.Sqrt(distanceSquared));
            if (designations.DesignationOn(plant, DesignationDefOf.HarvestPlant) != null)
                row.marked++;

            if (plant.HarvestableNow)
            {
                row.ready++;
                foreach (FloraYield y in row.yields)
                {
                    float amount = FloraYields.Expected(plant, y.source);
                    y.now += amount;
                    if (y.source.primary)
                        row.yieldNow += amount;
                }
            }
            else
            {
                float days = forecast.DaysUntilReady(plant);
                if (float.IsPositiveInfinity(days))
                    row.stalled++;
                else
                    row.nextReadyDays = Mathf.Min(row.nextReadyDays, days);
                if (plant.HarvestableSoon)
                    row.soon++;
            }

            if (plant.Dying || plant.Blighted)
            {
                row.dying++;
                row.atRisk.Add(plant);
            }
            else if (DaysUntilOldAge(plant) is var ageDays && ageDays <= MapForecast.Days)
            {
                row.oldAge++;
                row.oldAgeDays = Mathf.Min(row.oldAgeDays, ageDays);
                row.atRisk.Add(plant);
            }
            else if (forecast.DaysUntilKilled(plant) is var frostDays && !float.IsPositiveInfinity(frostDays))
            {
                row.frost++;
                row.frostDays = Mathf.Min(row.frostDays, frostDays);
                row.atRisk.Add(plant);
            }
        }

        var rows = new List<FloraRow>(RowsByDef.Count);
        foreach (FloraRow row in RowsByDef.Values)
        {
            if (filter.onlyReady && row.ready == 0)
                continue;
            rows.Add(row);
        }
        RowsByDef.Clear();
        return rows;
    }

    // Plant.YieldNow without its random rounding, so the total doesn't flicker between refreshes
    public static float ExpectedYield(Plant plant)
    {
        if (!plant.CanYieldNow())
            return 0f;
        PlantProperties props = plant.def.plant;
        float growth = 0.5f + 0.5f * Mathf.InverseLerp(props.harvestMinGrowth, 1f, plant.Growth);
        float health = Mathf.Lerp(0.5f, 1f, (float)plant.HitPoints / plant.MaxHitPoints);
        float difficulty = props.harvestYieldAffectedByDifficulty ? Find.Storyteller.difficulty.cropYieldFactor : 1f;
        return props.harvestYield * growth * health * difficulty;
    }

    // Days until HarvestableNow, from the map's hour-by-hour forecast; see MapForecast.DaysUntilReady
    public static float DaysUntilReady(Plant plant) => MapForecast.For(plant.Map).DaysUntilReady(plant);

    // Days until the plant starts dying of old age (Plant.CurrentDyingDamagePerTick), or infinity if its kind lives forever
    public static float DaysUntilOldAge(Plant plant)
    {
        PlantProperties props = plant.def.plant;
        if (!props.LimitedLifespan)
            return float.PositiveInfinity;
        return Mathf.Max(0f, (float)(props.LifespanTicks - plant.Age) / GenDate.TicksPerDay);
    }

    // The middle of the home area, else of the free colonists, else of the map
    public static IntVec3 BaseCenter(Map map)
    {
        Area home = map.areaManager.Home;
        if (home != null && home.TrueCount > 0)
            return Centroid(home.ActiveCells);
        var colonists = new List<IntVec3>();
        foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            colonists.Add(pawn.Position);
        return colonists.Count > 0 ? Centroid(colonists) : map.Center;
    }

    private static IntVec3 Centroid(IEnumerable<IntVec3> cells)
    {
        long x = 0, z = 0;
        int count = 0;
        foreach (IntVec3 cell in cells)
        {
            x += cell.x;
            z += cell.z;
            count++;
        }
        return count == 0 ? IntVec3.Invalid : new IntVec3((int)(x / count), 0, (int)(z / count));
    }
}
