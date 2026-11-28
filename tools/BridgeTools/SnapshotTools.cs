using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

// A game's state at a moment, and what changed between two: buildings (by id, with map and position), designations, item
// counts by def, areas and the player's pawns, for every map. Saved to ~/.gabs/rimworld/snapshots/<name>.json, so a
// snapshot outlives the game and two games' can be compared. "My furniture moved and everything got marked" is a diff.
public sealed class SnapshotTools
{
    public sealed class Snapshot
    {
        public string Name { get; set; }
        public int Tick { get; set; }
        public DateTime Taken { get; set; }
        public Dictionary<string, MapState> Maps { get; set; } = new();
    }

    public sealed class MapState
    {
        // id -> "def@(x, 0, z)"
        public Dictionary<string, string> Buildings { get; set; } = new();
        // "def target" -> count
        public Dictionary<string, int> Designations { get; set; } = new();
        // def -> total stack count, items spawned or stored
        public Dictionary<string, int> Items { get; set; } = new();
        // label -> cells
        public Dictionary<string, int> Areas { get; set; } = new();
        // pawn -> "(x, 0, z) job"
        public Dictionary<string, string> Pawns { get; set; } = new();
    }

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gabs", "rimworld", "snapshots");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    [Tool("ordo/snapshot", Description = "Save the game's state now (every map's buildings, designations, item counts, areas and player pawns) as a named snapshot in ~/.gabs/rimworld/snapshots, to compare later with ordo/diff. Lists the saved snapshots when no name is given.")]
    public static object Take(
        [ToolParameter(Description = "The snapshot's name, such as before-restart; leave out to list the saved ones")] string name = null
    )
    {
        System.IO.Directory.CreateDirectory(Directory);
        if (string.IsNullOrWhiteSpace(name))
        {
            return new
            {
                success = true,
                snapshots = new DirectoryInfo(Directory).GetFiles("*.json").OrderByDescending(f => f.LastWriteTime)
                    .Select(f => $"{Path.GetFileNameWithoutExtension(f.Name)} ({f.LastWriteTime:yyyy-MM-dd HH:mm}, {f.Length / 1024} KB)").ToList()
            };
        }

        var snapshot = Capture(name);
        File.WriteAllText(PathOf(name), JsonSerializer.Serialize(snapshot, Json));
        return new
        {
            success = true,
            file = PathOf(name),
            snapshot.Tick,
            maps = snapshot.Maps.ToDictionary(m => m.Key, m => $"{m.Value.Buildings.Count} buildings, {m.Value.Designations.Values.Sum()} designations, {m.Value.Items.Count} item defs, {m.Value.Pawns.Count} pawns")
        };
    }

    [Tool("ordo/diff", Description = "What changed between two snapshots, or a snapshot and the game now: buildings added, removed or moved (another map or cell), designations added or removed by def, item counts by def, area sizes and where the player's pawns went. Moves between MultiFloors levels show as a building changing map.")]
    public static object Diff(
        [ToolParameter(Description = "The earlier snapshot's name")] string from,
        [ToolParameter(Description = "The later snapshot's name; leave out for the game now")] string to = null,
        [ToolParameter(Description = "How many entries per section")] int limit = 40,
        [ToolParameter(Description = "Leave out item count changes smaller than this")] int minimumItemChange = 1
    )
    {
        var before = Load(from);
        if (before == null)
        {
            return new { success = false, message = $"No snapshot '{from}' in {Directory}" };
        }

        var after = to == null ? Capture("now") : Load(to);
        if (after == null)
        {
            return new { success = false, message = $"No snapshot '{to}' in {Directory}" };
        }

        // Buildings by id across every map, since a move between levels changes the map
        Dictionary<string, (string Map, string What)> Buildings(Snapshot s) => s.Maps.SelectMany(m => m.Value.Buildings.Select(b => (Id: b.Key, Map: m.Key, What: b.Value)))
            .GroupBy(b => b.Id).ToDictionary(g => g.Key, g => (g.First().Map, g.First().What));
        var oldBuildings = Buildings(before);
        var newBuildings = Buildings(after);
        var added = newBuildings.Where(b => !oldBuildings.ContainsKey(b.Key)).Select(b => $"{b.Key} {b.Value.What} on {b.Value.Map}").ToList();
        var removed = oldBuildings.Where(b => !newBuildings.ContainsKey(b.Key)).Select(b => $"{b.Key} {b.Value.What} on {b.Value.Map}").ToList();
        var moved = oldBuildings.Where(b => newBuildings.TryGetValue(b.Key, out var now) && (now.Map != b.Value.Map || now.What != b.Value.What))
            .Select(b => $"{b.Key}: {b.Value.What} on {b.Value.Map} → {newBuildings[b.Key].What} on {newBuildings[b.Key].Map}").ToList();

        var maps = before.Maps.Keys.Union(after.Maps.Keys).ToList();
        var perMap = maps.Select(map =>
        {
            var a = before.Maps.TryGetValue(map, out var x) ? x : new MapState();
            var b = after.Maps.TryGetValue(map, out var y) ? y : new MapState();
            return new
            {
                map,
                designations = Changes(a.Designations, b.Designations, 1).Take(limit).ToList(),
                items = Changes(a.Items, b.Items, minimumItemChange).Take(limit).ToList(),
                areas = Changes(a.Areas, b.Areas, 1).Take(limit).ToList(),
                pawns = a.Pawns.Keys.Union(b.Pawns.Keys)
                    .Where(p => !a.Pawns.TryGetValue(p, out var was) || !b.Pawns.TryGetValue(p, out var now) || was.Split(' ')[0] != now.Split(' ')[0])
                    .Select(p => $"{p}: {(a.Pawns.TryGetValue(p, out var was) ? was : "not here")} → {(b.Pawns.TryGetValue(p, out var now) ? now : "not here")}")
                    .Take(limit).ToList()
            };
        }).ToList();

        return new
        {
            success = true,
            from = $"{before.Name} (tick {before.Tick}, {before.Taken:yyyy-MM-dd HH:mm})",
            to = $"{after.Name} (tick {after.Tick})",
            buildings = new { added = added.Count, removed = removed.Count, moved = moved.Count, addedList = added.Take(limit), removedList = removed.Take(limit), movedList = moved.Take(limit) },
            maps = perMap
        };
    }

    // Keys whose count changed by at least the minimum, biggest change first: "def: before → after (+n)"
    private static IEnumerable<string> Changes(
        Dictionary<string, int> before,
        Dictionary<string, int> after,
        int minimum
    ) => before.Keys.Union(after.Keys)
        .Select(k => (Key: k, Was: before.TryGetValue(k, out var w) ? w : 0, Now: after.TryGetValue(k, out var n) ? n : 0))
        .Where(c => Math.Abs(c.Now - c.Was) >= minimum)
        .OrderByDescending(c => Math.Abs(c.Now - c.Was))
        .Select(c => $"{c.Key}: {c.Was} → {c.Now} ({c.Now - c.Was:+0;-0})");

    private static Snapshot Capture(
        string name
    )
    {
        var snapshot = new Snapshot { Name = name, Tick = Find.TickManager.TicksGame, Taken = DateTime.Now };
        foreach (var map in Find.Maps)
        {
            var state = new MapState();
            foreach (var thing in map.listerThings.AllThings)
            {
                if (thing.def.category == ThingCategory.Building && !thing.def.IsBlueprint && !thing.def.IsFrame && thing.Faction == Faction.OfPlayerSilentFail)
                {
                    state.Buildings[thing.ThingID] = $"{thing.def.defName}@{thing.Position}";
                }
                else if (thing.def.category == ThingCategory.Item)
                {
                    state.Items[thing.def.defName] = (state.Items.TryGetValue(thing.def.defName, out var n) ? n : 0) + thing.stackCount;
                }
            }

            foreach (var designation in map.designationManager.AllDesignations)
            {
                var key = designation.def.defName + (designation.target.HasThing ? " on " + designation.target.Thing.def.defName : " on cells");
                state.Designations[key] = (state.Designations.TryGetValue(key, out var n) ? n : 0) + 1;
            }

            foreach (var area in map.areaManager.AllAreas)
            {
                state.Areas[$"{area.Label} ({area.GetType().Name})"] = area.TrueCount;
            }

            foreach (var pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayerSilentFail && (p.RaceProps.Humanlike || p.RaceProps.IsMechanoid)))
            {
                state.Pawns[pawn.LabelShort] = $"{pawn.Position} {pawn.CurJobDef?.defName}";
            }

            snapshot.Maps[map.ToString()] = state;
        }

        return snapshot;
    }

    private static string PathOf(
        string name
    ) => Path.Combine(Directory, string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)) + ".json");

    private static Snapshot Load(
        string name
    ) => File.Exists(PathOf(name)) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(PathOf(name))) : null;
}
