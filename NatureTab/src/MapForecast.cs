using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.NatureTab;

// The map's next 7 days, hour by hour: outdoor temperature, sun glow and whether plants rest. From it, when frost kills a
// plant and when a growing plant will be ready.
//
// Temperature is the world tile's (its seasonal curve, daily swing and random variation) plus whatever the map adds now, such
// as a cold snap, assumed to last the whole forecast. Glow is the sun's for the tile in clear weather; weather and artificial
// light are left out.
public class MapForecast
{
    public const int Days = 7;
    public const int Hours = 24 * Days;

    private static MapForecast cached;
    private static Map cachedMap;
    private static int cachedHour = -1;

    private readonly float[] temps;
    private readonly float[] glows;
    private readonly bool[] resting;

    // runningMin[h]: the coldest of hours 0 to h from now
    private readonly float[] runningMin;

    // Per plant kind, cumulative growth by the end of each hour for a plant whose other factors (fertility, haze, drought)
    // are 1. Monotonic, so it can be searched.
    private readonly Dictionary<ThingDef, float[]> growthCurves = new();

    public MapForecast(
        float[] temps,
        float[] glows = null,
        bool[] resting = null)
    {
        this.temps = temps;
        this.glows = glows ?? Filled(temps.Length, 1f);
        this.resting = resting ?? new bool[temps.Length];
        runningMin = new float[temps.Length];
        float min = float.MaxValue;
        for (int i = 0; i < temps.Length; i++)
        {
            min = Mathf.Min(min, temps[i]);
            runningMin[i] = min;
        }
    }

    private static float[] Filled(
        int length,
        float value)
    {
        var array = new float[length];
        for (int i = 0; i < length; i++)
            array[i] = value;
        return array;
    }

    // Recomputed once an in-game hour per map
    public static MapForecast For(Map map)
    {
        int now = Find.TickManager.TicksAbs;
        int hour = now / GenDate.TicksPerHour;
        if (cached != null && cachedMap == map && cachedHour == hour)
            return cached;

        var temps = new float[Hours];
        var glows = new float[Hours];
        var resting = new bool[Hours];
        float longitude = Find.WorldGrid.LongLatOf(map.Tile).x;
        float mapOffset = map.mapTemperature.OutdoorTemp - Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, now);
        for (int h = 0; h < Hours; h++)
        {
            // The middle of the hour, as growth is applied through it
            int tick = now + h * GenDate.TicksPerHour + GenDate.TicksPerHour / 2;
            temps[h] = Find.World.tileTemperatures.OutdoorTemperatureAt(map.Tile, tick) + mapOffset;
            glows[h] = GenCelestial.CelestialSunGlow(map.Tile, tick);
            // Plant.Resting
            float dayPercent = GenDate.DayPercent(tick, longitude);
            resting[h] = dayPercent < 0.25f || dayPercent > 0.8f;
        }

        cached = new MapForecast(temps, glows, resting);
        cachedMap = map;
        cachedHour = hour;
        return cached;
    }

    // The first hour from now colder than the given temperature, or -1 if none in the forecast
    public int FirstHourBelow(float temperature)
    {
        if (runningMin.Length == 0 || runningMin[runningMin.Length - 1] >= temperature)
            return -1;
        int lo = 0, hi = runningMin.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (runningMin[mid] < temperature)
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo;
    }

    private static bool Outdoors(Plant plant)
    {
        Room room = plant.GetRoom();
        return room != null && room.UsesOutdoorTemperature;
    }

    // Days until frost kills the plant, or infinity if it won't within the forecast, isn't outdoors or survives being
    // leafless. Outdoors, Plant.CheckMakeLeafless makes a plant leafless below its own LeaflessTemperatureThresh (its minimum
    // growth temperature minus 10 to 18), and only a plant with dieIfLeafless dies of it.
    public float DaysUntilKilled(Plant plant)
    {
        if (!plant.def.plant.dieIfLeafless || !Outdoors(plant))
            return float.PositiveInfinity;
        int hour = FirstHourBelow(plant.LeaflessTemperatureThresh);
        return hour < 0 ? float.PositiveInfinity : hour / 24f;
    }

    // Days until HarvestableNow: 0 when ready, infinity when it won't grow (blighted, no harvest, no growth in the forecast).
    // Growth follows Plant.TickLong: 1 / (60000 * growDays) * GrowthRate per tick while not resting. Outdoors, light and
    // temperature are taken hour by hour; past the forecast, growth goes on at its average. Indoors and under roofs, where the
    // light is steady, today's rate is used for the hours that aren't resting.
    public float DaysUntilReady(Plant plant)
    {
        if (plant.HarvestableNow)
            return 0f;
        PlantProperties props = plant.def.plant;
        if (!props.Harvestable || props.growDays <= 0f || plant.Blighted)
            return float.PositiveInfinity;
        float remaining = Mathf.Max(0f, props.harvestMinGrowth - plant.Growth) + 0.0001f;

        if (!Outdoors(plant) || plant.Position.Roofed(plant.Map))
        {
            float rate = plant.GrowthRate;
            return rate <= 0f ? float.PositiveInfinity : remaining / (rate / props.growDays * RestingFreeFraction());
        }

        float others = plant.GrowthRateFactor_Fertility * plant.GrowthRateFactor_NoxiousHaze * plant.GrowthRateFactor_Drought;
        if (others <= 0f)
            return float.PositiveInfinity;
        float[] curve = GrowthCurve(plant.def);
        float target = remaining / others;
        float total = curve[curve.Length - 1];
        if (total < target)
            return total <= 0f ? float.PositiveInfinity : Days + (target - total) / (total / Days);

        int lo = 0, hi = curve.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (curve[mid] >= target)
                hi = mid;
            else
                lo = mid + 1;
        }
        // Within the hour it crosses, by how far into it
        float before = lo > 0 ? curve[lo - 1] : 0f;
        float fraction = curve[lo] > before ? (target - before) / (curve[lo] - before) : 1f;
        return (lo + fraction) / 24f;
    }

    // How much an outdoor, unroofed plant is expected to grow in the next given hours (within the forecast)
    public float GrowthOver(
        Plant plant,
        int hours)
    {
        float[] curve = GrowthCurve(plant.def);
        float others = plant.GrowthRateFactor_Fertility * plant.GrowthRateFactor_NoxiousHaze * plant.GrowthRateFactor_Drought;
        return curve[Mathf.Clamp(hours, 1, curve.Length) - 1] * others;
    }

    private float RestingFreeFraction()
    {
        int free = 0;
        foreach (bool rest in resting)
            if (!rest)
                free++;
        return resting.Length == 0 ? 1f : (float)free / resting.Length;
    }

    private float[] GrowthCurve(ThingDef def)
    {
        if (growthCurves.TryGetValue(def, out float[] curve))
            return curve;
        curve = new float[temps.Length];
        float perHour = 1f / (24f * def.plant.growDays);
        float sum = 0f;
        for (int h = 0; h < temps.Length; h++)
        {
            // GrowthSeasonNow: no growth outside the plant's temperature range
            bool season = temps[h] > def.plant.minGrowthTemperature && temps[h] < def.plant.maxGrowthTemperature;
            if (!resting[h] && season)
                sum += perHour * PlantUtility.GrowthRateFactorFor_Light(def, glows[h])
                    * PlantUtility.GrowthRateFactorFor_Temperature(def, temps[h]);
            curve[h] = sum;
        }
        growthCurves[def] = curve;
        return curve;
    }
}
