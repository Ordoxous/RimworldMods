using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

// Values sampled over time while the game runs: start a watch, play on, read its series. Sampling runs in the game's
// tick, so it doesn't stop the game or hold the bridge (stepping ticks through the bridge does both). Kinds:
// - power: a building's power output and whether it's on, and its net's production, use and stored energy (a stair's
//   transfer between levels, say, or a battery bank)
// - thing: where a thing is (map, cell, spawned or held) and its designations
// - pawn: a pawn's map, cell and job
// - count: the stack count of things whose def matches a regex, on each map
public sealed class WatchTools
{
    private sealed class Watch
    {
        public int Id;
        public string Kind;
        public string Target;
        public int Every;
        public int Max;
        public int Started;
        public Func<Dictionary<string, object>> Sample;
        public readonly Queue<(int Tick, Dictionary<string, object> Values)> Samples = new();
    }

    private static readonly Dictionary<int, Watch> Watches = new();
    private static int nextId = 1;
    private static bool patched;

    [Tool("ordo/watch", Description = "Start sampling a value every few ticks while the game runs; read it with ordo/watch_read. kind: power (a building's output and on/off, and its net's production, use and storage), thing (map, cell, spawned or held, designations), pawn (map, cell, job) or count (stack count of defs matching a regex, per map). Ticks pass only while the game is unpaused.")]
    public static object Start(
        [ToolParameter(Description = "power, thing, pawn or count")] string kind,
        [ToolParameter(Description = "A thing id for power or thing, a pawn's name for pawn, a def regex for count")] string target,
        [ToolParameter(Description = "Ticks between samples (60 is a second at normal speed)")] int every = 60,
        [ToolParameter(Description = "Samples to keep; older ones are dropped")] int keep = 600
    )
    {
        var sample = Sampler(kind?.ToLowerInvariant(), target, out var error);
        if (sample == null)
        {
            return new { success = false, message = error };
        }

        EnsurePatched();
        var watch = new Watch { Id = nextId++, Kind = kind.ToLowerInvariant(), Target = target, Every = Math.Max(1, every), Max = Math.Max(10, keep), Started = Find.TickManager.TicksGame, Sample = sample };
        Watches[watch.Id] = watch;
        Record(watch);
        return new { success = true, id = watch.Id, first = watch.Samples.Last().Values };
    }

    [Tool("ordo/watch_read", Description = "A watch's samples: for each value, its minimum, maximum, how often it changed between samples, and its last value, then the samples themselves (the newest, or every one with all). Without an id, lists the watches.")]
    public static object Read(
        [ToolParameter(Description = "The watch's id from ordo/watch; leave out to list them")] int id = 0,
        [ToolParameter(Description = "How many of the newest samples to list")] int last = 30,
        [ToolParameter(Description = "Only samples where a value changed from the one before")] bool changesOnly = false
    )
    {
        if (id == 0 || !Watches.TryGetValue(id, out var watch))
        {
            return new { success = id == 0, watches = Watches.Values.Select(w => $"{w.Id}: {w.Kind} {w.Target} every {w.Every} ticks, {w.Samples.Count} samples").ToList() };
        }

        var samples = watch.Samples.ToList();
        var keys = samples.SelectMany(s => s.Values.Keys).Distinct().ToList();
        var stats = keys.ToDictionary(k => k, k =>
        {
            var values = samples.Select(s => s.Values.TryGetValue(k, out var v) ? v : null).ToList();
            var changes = values.Zip(values.Skip(1), (a, b) => !Equals(a, b)).Count(c => c);
            var numbers = values.OfType<double>().ToList();
            return (object)new
            {
                min = numbers.Count > 0 ? numbers.Min() : (double?)null,
                max = numbers.Count > 0 ? numbers.Max() : (double?)null,
                changes,
                last = values.LastOrDefault()
            };
        });
        var listed = changesOnly
            ? samples.Where((s, i) => i == 0 || s.Values.Any(v => !Equals(v.Value, samples[i - 1].Values.TryGetValue(v.Key, out var was) ? was : null))).ToList()
            : samples;
        return new
        {
            success = true,
            watch.Id,
            watch.Kind,
            watch.Target,
            watch.Every,
            from = samples.FirstOrDefault().Tick,
            to = samples.LastOrDefault().Tick,
            count = samples.Count,
            stats,
            samples = listed.Skip(Math.Max(0, listed.Count - last)).Select(s => new { tick = s.Tick, s.Values }).ToList()
        };
    }

    [Tool("ordo/watch_stop", Description = "Stop a watch, or every watch")]
    public static object Stop(
        [ToolParameter(Description = "The watch's id; leave out to stop them all")] int id = 0
    )
    {
        var stopped = id == 0 ? Watches.Keys.ToList() : Watches.ContainsKey(id) ? new List<int> { id } : new List<int>();
        foreach (var key in stopped)
        {
            Watches.Remove(key);
        }

        return new { success = true, stopped };
    }

    private static void EnsurePatched()
    {
        if (patched)
        {
            return;
        }

        patched = true;
        new Harmony("Ordo.BridgeTools.Watch").Patch(AccessTools.Method(typeof(TickManager), nameof(TickManager.DoSingleTick)),
            postfix: new HarmonyMethod(typeof(WatchTools), nameof(Postfix_DoSingleTick)));
    }

    public static void Postfix_DoSingleTick()
    {
        if (Watches.Count == 0)
        {
            return;
        }

        var tick = Find.TickManager.TicksGame;
        foreach (var watch in Watches.Values)
        {
            if ((tick - watch.Started) % watch.Every == 0)
            {
                Record(watch);
            }
        }
    }

    private static void Record(
        Watch watch
    )
    {
        Dictionary<string, object> values;
        try
        {
            values = watch.Sample();
        }
        catch (Exception e)
        {
            values = new Dictionary<string, object> { ["error"] = e.Message };
        }

        watch.Samples.Enqueue((Find.TickManager.TicksGame, values));
        while (watch.Samples.Count > watch.Max)
        {
            watch.Samples.Dequeue();
        }
    }

    private static Func<Dictionary<string, object>> Sampler(
        string kind,
        string target,
        out string error
    )
    {
        error = null;
        switch (kind)
        {
            case "power":
            case "thing":
            {
                var thing = FindThing(target);
                if (thing == null)
                {
                    error = $"No thing with id '{target}' on any map";
                    return null;
                }

                return kind == "power" ? () => Power(thing) : () => Whereabouts(thing);
            }
            case "pawn":
            {
                var pawn = Find.Maps.SelectMany(m => m.mapPawns.AllPawns).FirstOrDefault(p =>
                    p.LabelShort.Equals(target, StringComparison.OrdinalIgnoreCase) || p.ThingID.Equals(target, StringComparison.OrdinalIgnoreCase)
                    || (p.Name?.ToStringFull.IndexOf(target, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
                if (pawn == null)
                {
                    error = $"No pawn '{target}' on any map";
                    return null;
                }

                return () => new Dictionary<string, object>
                {
                    ["map"] = pawn.MapHeld?.ToString(),
                    ["cell"] = pawn.PositionHeld.ToString(),
                    ["job"] = pawn.CurJobDef?.defName,
                    ["target"] = pawn.CurJob?.targetA.Thing?.ThingID ?? (pawn.CurJob?.targetA.IsValid == true ? pawn.CurJob.targetA.Cell.ToString() : null)
                };
            }
            case "count":
            {
                Regex regex;
                try
                {
                    regex = new Regex(target, RegexOptions.IgnoreCase);
                }
                catch (Exception e)
                {
                    error = e.Message;
                    return null;
                }

                return () => Find.Maps.ToDictionary(m => m.ToString(), m => (object)(double)m.listerThings.AllThings.Where(t => regex.IsMatch(t.def.defName)).Sum(t => t.stackCount));
            }
            default:
                error = "kind is power, thing, pawn or count";
                return null;
        }
    }

    private static Dictionary<string, object> Power(
        Thing thing
    )
    {
        var trader = thing.TryGetComp<CompPowerTrader>();
        var net = thing.TryGetComp<CompPower>()?.PowerNet;
        var values = new Dictionary<string, object>
        {
            ["output"] = trader == null ? null : (object)Math.Round((double)trader.PowerOutput),
            ["on"] = trader?.PowerOn
        };
        if (net != null)
        {
            var on = net.powerComps.Where(c => c.PowerOn).ToList();
            values["netProducing"] = Math.Round((double)on.Where(c => c.PowerOutput > 0f).Sum(c => c.PowerOutput));
            values["netUsing"] = Math.Round((double)-on.Where(c => c.PowerOutput < 0f).Sum(c => c.PowerOutput));
            values["netOff"] = (double)net.powerComps.Count(c => !c.PowerOn);
            values["netStored"] = Math.Round((double)net.CurrentStoredEnergy());
        }

        return values;
    }

    private static Dictionary<string, object> Whereabouts(
        Thing thing
    ) => new()
    {
        ["map"] = thing.MapHeld?.ToString(),
        ["cell"] = thing.PositionHeld.ToString(),
        ["spawned"] = thing.Spawned,
        ["heldBy"] = thing.Spawned ? null : thing.ParentHolder?.ToString(),
        ["destroyed"] = thing.Destroyed,
        ["designations"] = thing.MapHeld == null ? null : string.Join(", ", thing.MapHeld.designationManager.AllDesignationsOn(thing).Select(d => d.def.defName))
    };

    private static Thing FindThing(
        string id
    )
    {
        id = id?.StartsWith("Thing_", StringComparison.OrdinalIgnoreCase) == true ? id.Substring(6) : id;
        foreach (var map in Find.Maps)
        {
            var held = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), held, allowUnreal: false);
            var found = held.FirstOrDefault(t => string.Equals(t.ThingID, id, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
