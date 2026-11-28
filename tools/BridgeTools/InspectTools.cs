using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

// Looking at a running game across every map: the log's own queue (the bridge keeps only its recent lines), things by def
// or id wherever they are, designations, areas and power nets. Built after a session that read save files to answer
// "where is it now" and "which level is it on". Read-only, so safe on the player's own game.
public sealed class InspectTools
{
    [Tool("ordo/log", Description = "The game's own log queue (its last 1000 entries, repeats merged), newest first: warnings and errors by default. ErrorMirror keeps every warning and error of the session in ~/.gabs/rimworld/logs/<pid>.log as well.")]
    public static object Log(
        [ToolParameter(Description = "message, warning or error")] string minimumLevel = "warning",
        [ToolParameter(Description = "Only entries whose text contains this, ignoring case")] string contains = null,
        [ToolParameter(Description = "How many entries")] int limit = 30,
        [ToolParameter(Description = "Include each entry's stack trace")] bool stackTraces = false,
        [ToolParameter(Description = "Log this text as a warning first, to mark a point in the log or check the mirror is writing")] string writeWarning = null
    )
    {
        if (!string.IsNullOrEmpty(writeWarning))
        {
            Verse.Log.Warning($"[BridgeTools] {writeWarning}");
        }

        var minimum = minimumLevel?.ToLowerInvariant() switch
        {
            "message" or "info" => LogMessageType.Message,
            "error" => LogMessageType.Error,
            _ => LogMessageType.Warning
        };
        var entries = Verse.Log.Messages.Where(m => m.type >= minimum && (contains == null || m.text.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0))
            .Reverse().Take(Math.Max(1, limit))
            .Select(m => new { level = m.type.ToString(), m.repeats, m.timestamp, text = m.text, stack = stackTraces ? m.StackTrace : null })
            .ToList();
        return new { success = true, count = entries.Count, mirror = ErrorMirror.FilePath, entries };
    }

    [Tool("ordo/find_things", Description = "Things on every map (MultiFloors levels included) whose def name or id matches a regex: where each is, what holds it, whether it's forbidden, and its designations. Also finds things held in containers and inventories.")]
    public static object FindThings(
        [ToolParameter(Description = "Regex matched against the def name and the thing id, ignoring case, such as ^Steel$ or Corpse_Mech_ or MechGestator770243")] string match,
        [ToolParameter(Description = "How many things")] int limit = 50
    )
    {
        var regex = new Regex(match, RegexOptions.IgnoreCase);
        var found = new List<object>();
        var total = 0;
        foreach (var map in Find.Maps)
        {
            foreach (var thing in AllThingsOn(map))
            {
                if (!regex.IsMatch(thing.def.defName) && !regex.IsMatch(thing.ThingID))
                {
                    continue;
                }

                total += thing.stackCount;
                if (found.Count >= limit)
                {
                    continue;
                }

                found.Add(new
                {
                    id = thing.ThingID,
                    def = thing.def.defName,
                    map = map.ToString(),
                    position = thing.PositionHeld.ToString(),
                    count = thing.stackCount,
                    spawned = thing.Spawned,
                    heldBy = thing.Spawned ? null : thing.ParentHolder?.ToString(),
                    forbidden = thing.IsForbidden(Faction.OfPlayer),
                    faction = thing.Faction?.Name,
                    designations = map.designationManager.AllDesignationsOn(thing).Select(d => d.def.defName).ToList()
                });
            }
        }

        return new { success = true, matched = found.Count, totalCount = total, things = found };
    }

    // Spawned things and those held inside them (storage, containers, pawns' inventories and carried things)
    private static IEnumerable<Thing> AllThingsOn(
        Map map
    )
    {
        var held = new List<Thing>();
        ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), held, allowUnreal: false);
        return held.Distinct();
    }

    [Tool("ordo/designations", Description = "Designations on every map, counted by def, with each one's target when a def is given. MultiFloors' haul-to-level designations are better read with ordo/level_bill_check, which shows their destination and requester.")]
    public static object Designations(
        [ToolParameter(Description = "Only this designation def, such as Haul, Uninstall or MF_HaulToOtherLevelDesignation; leave out for counts of every def")] string def = null,
        [ToolParameter(Description = "How many designations to list")] int limit = 50
    )
    {
        var maps = Find.Maps.Select(map =>
        {
            var all = map.designationManager.AllDesignations.ToList();
            return new
            {
                map = map.ToString(),
                counts = all.GroupBy(d => d.def.defName).OrderByDescending(g => g.Count()).ToDictionary(g => g.Key, g => g.Count()),
                designations = def == null
                    ? null
                    : all.Where(d => d.def.defName.Equals(def, StringComparison.OrdinalIgnoreCase)).Take(limit)
                        .Select(d => d.target.HasThing ? $"{d.target.Thing.ThingID} at {d.target.Thing.PositionHeld}" : d.target.Cell.ToString()).ToList()
            };
        }).ToList();
        return new { success = true, maps };
    }

    [Tool("ordo/areas", Description = "Every map's areas (home, allowed areas, roofs, mod areas such as a cleaning area) with their cell counts. rimworld/list_areas only sees the map shown.")]
    public static object Areas()
    {
        var maps = Find.Maps.Select(map => new
        {
            map = map.ToString(),
            areas = map.areaManager.AllAreas.Select(a => new { a.Label, type = a.GetType().Name, cells = a.TrueCount }).ToList()
        }).ToList();
        return new { success = true, maps };
    }

    [Tool("ordo/power_nets", Description = "Every map's power nets: what they make and use now, stored energy, batteries, and their biggest producers and consumers. Buildings that link nets across MultiFloors levels (stairs, elevators) show with their transfer as output.")]
    public static object PowerNets(
        [ToolParameter(Description = "Only nets with at least this many parts")] int minimumParts = 2
    )
    {
        var maps = Find.Maps.Select(map => new
        {
            map = map.ToString(),
            nets = map.powerNetManager.AllNetsListForReading
                .Where(net => net.powerComps.Count + net.batteryComps.Count >= minimumParts)
                .Select(net =>
                {
                    var on = net.powerComps.Where(c => c.PowerOn).ToList();
                    return new
                    {
                        parts = net.powerComps.Count + net.batteryComps.Count,
                        producingWatts = (int)on.Where(c => c.PowerOutput > 0f).Sum(c => c.PowerOutput),
                        usingWatts = (int)-on.Where(c => c.PowerOutput < 0f).Sum(c => c.PowerOutput),
                        switchedOff = net.powerComps.Count(c => !c.PowerOn),
                        storedWattDays = (int)net.CurrentStoredEnergy(),
                        batteries = net.batteryComps.Count,
                        top = net.powerComps.OrderByDescending(c => Math.Abs(c.PowerOutput)).Take(6)
                            .Select(c => $"{c.parent.LabelCap} {(int)c.PowerOutput} W{(c.PowerOn ? "" : " (off)")}").ToList()
                    };
                }).ToList()
        }).ToList();
        return new { success = true, maps };
    }
}
