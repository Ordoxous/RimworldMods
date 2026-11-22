using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// What a scenario changed outside the things it spawned, which the stage's cleanup doesn't undo and the next scenario
// would see: areas (the game adds home area around player buildings and zones), zones, designations, terrain, roofs, fog,
// game conditions, weather, research, and the settings and saved components of the Ordo mods. Taken before a scenario and
// after its cleanup.
public sealed class LeakCheck
{
    private readonly Map _map;
    private readonly Dictionary<string, string> _values = new();

    // Saved settings and components, which are long, so only their change is reported
    private readonly HashSet<string> _saved = new();
    private readonly TerrainDef[] _terrain;
    private readonly RoofDef[] _roofs;
    private readonly bool[] _fog;

    private LeakCheck(
        Map map
    )
    {
        _map = map;
        var cells = map.cellIndices.NumGridCells;
        _terrain = new TerrainDef[cells];
        _roofs = new RoofDef[cells];
        _fog = new bool[cells];
        for (var i = 0; i < cells; i++)
        {
            _terrain[i] = map.terrainGrid.TerrainAt(i);
            _roofs[i] = map.roofGrid.RoofAt(i);
            _fog[i] = map.fogGrid.IsFogged(i);
        }

        foreach (var area in map.areaManager.AllAreas)
        {
            _values[$"area '{area.Label}'"] = $"{area.TrueCount} cells";
        }

        _values["areas"] = map.areaManager.AllAreas.Count.ToString();
        _values["zones"] = $"{map.zoneManager.AllZones.Count} with {map.zoneManager.AllZones.Sum(zone => zone.CellCount)} cells";
        _values["designations"] = string.Join(", ", map.designationManager.AllDesignations
            .GroupBy(designation => designation.def.defName).OrderBy(group => group.Key).Select(group => $"{group.Key} {group.Count()}"));
        _values["colony buildings"] = map.listerBuildings.allBuildingsColonist.Count.ToString();
        _values["pawns"] = map.mapPawns.AllPawnsSpawnedCount.ToString();
        _values["map conditions"] = string.Join(", ", map.gameConditionManager.ActiveConditions.Select(condition => condition.def.defName));
        _values["world conditions"] = string.Join(", ", Find.World.gameConditionManager.ActiveConditions.Select(condition => condition.def.defName));
        _values["weather"] = map.weatherManager.curWeather?.defName;
        _values["research"] = DefDatabase<ResearchProjectDef>.AllDefsListForReading.Sum(project => Find.ResearchManager.GetProgress(project)).ToString("F0");

        foreach (var mod in LoadedModManager.RunningModsListForReading.Where(IsOrdo))
        {
            foreach (var handle in LoadedModManager.ModHandles.Where(handle => handle.Content == mod))
            {
                if (AccessTools.Field(typeof(Mod), "modSettings").GetValue(handle) is ModSettings settings)
                {
                    _values[$"{mod.Name} settings"] = Saved(settings);
                    _saved.Add($"{mod.Name} settings");
                }
            }
        }

        var components = Current.Game.components.Cast<IExposable>()
            .Concat(Find.World.components)
            .Concat(map.components);
        foreach (var component in components.Where(component => IsOrdo(component.GetType())))
        {
            _values[component.GetType().Name] = Saved(component);
            _saved.Add(component.GetType().Name);
        }
    }

    public static LeakCheck Take(
        Map map
    ) => new(map);

    // What differs from the earlier snapshot
    public List<string> ChangedSince(
        LeakCheck before
    )
    {
        var after = this;
        var changes = new List<string>();
        foreach (var key in before._values.Keys.Union(after._values.Keys))
        {
            before._values.TryGetValue(key, out var was);
            after._values.TryGetValue(key, out var now);
            if (was == now)
            {
                continue;
            }

            changes.Add(before._saved.Contains(key) || after._saved.Contains(key) ? $"{key} changed" : $"{key}: {Show(was)} -> {Show(now)}");
        }

        CompareGrid(changes, "terrain", before._terrain, after._terrain, def => def?.defName);
        CompareGrid(changes, "roof", before._roofs, after._roofs, def => def?.defName ?? "none");
        CompareGrid(changes, "fog", before._fog, after._fog, fogged => fogged ? "fogged" : "clear");
        return changes;
    }

    private void CompareGrid<T>(
        List<string> changes,
        string label,
        T[] was,
        T[] now,
        Func<T, string> show
    )
    {
        var changed = Enumerable.Range(0, was.Length).Where(i => !EqualityComparer<T>.Default.Equals(was[i], now[i])).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var first = changed[0];
        changes.Add($"{label} changed in {changed.Count} cell(s), first at {_map.cellIndices.IndexToCell(first)}: {show(was[first])} -> {show(now[first])}");
    }

    private static string Show(
        string value
    ) => value == null ? "none" : value.Length == 0 ? "nothing" : value;

    private static bool IsOrdo(
        ModContentPack mod
    ) => mod.PackageId.StartsWith("ordo.", StringComparison.OrdinalIgnoreCase);

    private static bool IsOrdo(
        Type type
    ) => type.Namespace != null && type.Namespace.StartsWith("Ordo.") && !type.Namespace.StartsWith("Ordo.BridgeTools");

    private static string Saved(
        IExposable exposable
    )
    {
        try
        {
            return Scribe.saver.DebugOutputFor(exposable);
        }
        catch (Exception exception)
        {
            return $"(couldn't save: {exception.Message})";
        }
    }
}
