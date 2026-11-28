using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools;

// What happened, recorded while the game runs, for questions asked afterwards:
// - Job history: each job the player's pawns (colonists, slaves, colony mechs) start and end, with what gave it and how it
//   ended, the last 200 per pawn. "Did anyone try the gestator, and why did they stop?"
// - Origins: who added or removed a designation, and who spawned, despawned or destroyed a building or minified thing,
//   with the mod code on the stack, the last 3000. "What marked the furniture, and what carried it downstairs?"
// Started by BridgeConnection when the bridge discovers these tools. Each hook is guarded, so a failure costs only itself.
public sealed class HistoryTools
{
    private const int JobsPerPawn = 200;
    private const int OriginEntries = 3000;

    private sealed record JobEntry(int Tick, string Event, string Job, string Target, string Map, string Giver, string Detail);

    private sealed record OriginEntry(int Tick, string Action, string ThingId, string What, string Map, string Where, List<string> Mods, string Game);

    private static readonly Dictionary<int, (Pawn Pawn, Queue<JobEntry> Jobs)> Jobs = new();
    private static readonly Queue<OriginEntry> Origins = new();
    private static readonly object Sync = new();
    private static readonly List<string> Failed = new();
    private static bool started;

    public static void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        var harmony = new Harmony("Ordo.BridgeTools.History");
        Patch(harmony, AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)), postfix: nameof(Postfix_StartJob));
        Patch(harmony, AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob)), prefix: nameof(Prefix_EndCurrentJob));
        Patch(harmony, AccessTools.Method(typeof(DesignationManager), nameof(DesignationManager.AddDesignation)), postfix: nameof(Postfix_AddDesignation));
        Patch(harmony, AccessTools.Method(typeof(DesignationManager), nameof(DesignationManager.RemoveDesignation)), prefix: nameof(Prefix_RemoveDesignation));
        Patch(harmony, AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)), postfix: nameof(Postfix_SpawnSetup));
        Patch(harmony, AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)), prefix: nameof(Prefix_DeSpawn));
        Patch(harmony, AccessTools.Method(typeof(Thing), nameof(Thing.Destroy)), prefix: nameof(Prefix_Destroy));
    }

    private static void Patch(
        Harmony harmony,
        System.Reflection.MethodBase original,
        string prefix = null,
        string postfix = null
    )
    {
        try
        {
            harmony.Patch(original,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(HistoryTools), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(HistoryTools), postfix));
        }
        catch (Exception e)
        {
            Failed.Add($"{original?.DeclaringType?.Name}.{original?.Name}: {e.Message}");
            Log.Warning($"[BridgeTools] History couldn't patch {original?.DeclaringType?.Name}.{original?.Name}: {e.Message}");
        }
    }

    private static int Tick => Find.TickManager?.TicksGame ?? 0;

    // --- Jobs ---

    private static bool Tracked(
        Pawn pawn
    ) => pawn != null && pawn.Faction == Faction.OfPlayerSilentFail && (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid);

    private static void AddJob(
        Pawn pawn,
        JobEntry entry
    )
    {
        lock (Sync)
        {
            if (!Jobs.TryGetValue(pawn.thingIDNumber, out var record))
            {
                record = (pawn, new Queue<JobEntry>());
                Jobs[pawn.thingIDNumber] = record;
            }

            record.Jobs.Enqueue(entry);
            while (record.Jobs.Count > JobsPerPawn)
            {
                record.Jobs.Dequeue();
            }
        }
    }

    private static string Target(
        LocalTargetInfo target
    ) => !target.IsValid ? null : target.HasThing ? $"{target.Thing.LabelShort} ({target.Thing.ThingID})" : target.Cell.ToString();

    public static void Postfix_StartJob(
        Pawn ___pawn,
        Job newJob,
        ThinkNode jobGiver,
        bool fromQueue
    )
    {
        try
        {
            if (!Tracked(___pawn) || newJob == null)
            {
                return;
            }

            var giver = newJob.workGiverDef?.defName ?? jobGiver?.GetType().Name;
            AddJob(___pawn, new JobEntry(Tick, "start", newJob.def.defName, Target(newJob.targetA) ?? Target(newJob.targetB), ___pawn.Map?.ToString(),
                giver, fromQueue ? "from queue" : null));
        }
        catch
        {
            // Never break the job tracker
        }
    }

    public static void Prefix_EndCurrentJob(
        Pawn ___pawn,
        Job ___curJob,
        JobCondition condition
    )
    {
        try
        {
            if (!Tracked(___pawn) || ___curJob == null)
            {
                return;
            }

            AddJob(___pawn, new JobEntry(Tick, "end", ___curJob.def.defName, Target(___curJob.targetA) ?? Target(___curJob.targetB), ___pawn.Map?.ToString(),
                ___curJob.workGiverDef?.defName, condition.ToString()));
        }
        catch
        {
        }
    }

    // --- Origins ---

    private static void AddOrigin(
        string action,
        Thing thing,
        string what,
        Map map,
        string where
    )
    {
        var (mods, game) = Attribution.FromStack(2);
        lock (Sync)
        {
            Origins.Enqueue(new OriginEntry(Tick, action, thing?.ThingID, what, map?.ToString(), where, mods, game));
            while (Origins.Count > OriginEntries)
            {
                Origins.Dequeue();
            }
        }
    }

    private static bool Recorded(
        Thing thing
    ) => thing != null && !(thing is Pawn) && !thing.def.IsBlueprint && !thing.def.IsFrame
        && (thing.def.category == ThingCategory.Building || thing is MinifiedThing);

    public static void Postfix_AddDesignation(
        DesignationManager __instance,
        Designation newDes
    )
    {
        try
        {
            AddOrigin("designated", newDes.target.Thing, $"{newDes.def.defName} on {(newDes.target.HasThing ? newDes.target.Thing.LabelShort : newDes.target.Cell.ToString())}",
                __instance.map, newDes.target.HasThing ? newDes.target.Thing.PositionHeld.ToString() : newDes.target.Cell.ToString());
        }
        catch
        {
        }
    }

    public static void Prefix_RemoveDesignation(
        DesignationManager __instance,
        Designation des
    )
    {
        try
        {
            if (des == null)
            {
                return;
            }

            AddOrigin("undesignated", des.target.Thing, $"{des.def.defName} on {(des.target.HasThing ? des.target.Thing.LabelShort : des.target.Cell.ToString())}",
                __instance.map, des.target.HasThing ? des.target.Thing.PositionHeld.ToString() : des.target.Cell.ToString());
        }
        catch
        {
        }
    }

    public static void Postfix_SpawnSetup(
        Thing __instance,
        Map map,
        bool respawningAfterLoad
    )
    {
        try
        {
            if (!respawningAfterLoad && Recorded(__instance))
            {
                AddOrigin("spawned", __instance, __instance.LabelShort, map, __instance.Position.ToString());
            }
        }
        catch
        {
        }
    }

    public static void Prefix_DeSpawn(
        Thing __instance,
        DestroyMode mode
    )
    {
        try
        {
            if (__instance.Spawned && Recorded(__instance))
            {
                AddOrigin("despawned", __instance, $"{__instance.LabelShort} ({mode})", __instance.Map, __instance.Position.ToString());
            }
        }
        catch
        {
        }
    }

    public static void Prefix_Destroy(
        Thing __instance,
        DestroyMode mode
    )
    {
        try
        {
            if (!__instance.Destroyed && Recorded(__instance))
            {
                AddOrigin("destroyed", __instance, $"{__instance.LabelShort} ({mode})", __instance.MapHeld, __instance.PositionHeld.ToString());
            }
        }
        catch
        {
        }
    }

    // --- Tools ---

    [Tool("ordo/job_history", Description = "The jobs a player pawn (colonist, slave or colony mech) started and ended since the game started, newest first: the job, its target and map, the work giver or think node that gave it, and how each ended (Succeeded, Incompletable, InterruptForced...). The last 200 per pawn.")]
    public static object JobHistory(
        [ToolParameter(Description = "The pawn's name or id (accents and case ignored); leave out for every tracked pawn's last few")] string pawn = null,
        [ToolParameter(Description = "Only entries whose job, target or giver contains this, ignoring case")] string contains = null,
        [ToolParameter(Description = "How many entries")] int limit = 40
    )
    {
        List<(Pawn Pawn, List<JobEntry> Jobs)> records;
        lock (Sync)
        {
            records = Jobs.Values.Select(r => (r.Pawn, r.Jobs.ToList())).ToList();
        }

        bool Matches(JobEntry e) => contains == null || new[] { e.Job, e.Target, e.Giver }.Any(s => s?.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);
        if (pawn != null)
        {
            var wanted = Plain(pawn);
            var record = records.FirstOrDefault(r => new[] { r.Pawn.LabelShort, r.Pawn.Name?.ToStringFull, r.Pawn.ThingID }.Any(n => n != null && Plain(n).Contains(wanted)));
            if (record.Pawn == null)
            {
                return new { success = false, message = $"No history for '{pawn}'", tracked = records.Select(r => r.Pawn.LabelShort).ToList(), failedPatches = Failed };
            }

            return new
            {
                success = true,
                pawn = record.Pawn.LabelShort,
                now = Tick,
                jobs = record.Jobs.Where(Matches).Reverse().Take(limit).Select(Describe).ToList(),
                failedPatches = Failed
            };
        }

        return new
        {
            success = true,
            now = Tick,
            pawns = records.Select(r => new { pawn = r.Pawn.LabelShort, jobs = r.Jobs.Where(Matches).Reverse().Take(Math.Max(1, limit / Math.Max(1, records.Count))).Select(Describe).ToList() }).ToList(),
            failedPatches = Failed
        };
    }

    private static string Describe(
        JobEntry e
    ) => $"{e.Tick} {e.Event} {e.Job}{(e.Target == null ? "" : " → " + e.Target)} on {e.Map}{(e.Giver == null ? "" : ", by " + e.Giver)}{(e.Detail == null ? "" : ", " + e.Detail)}";

    [Tool("ordo/origin", Description = "Who added or removed designations, and who spawned, despawned or destroyed buildings and minified things, newest first: the tick, what happened, where, and the first mod frames on the stack (with the nearest game method). Matches a thing id, a def or designation name, or a map. The last 3000 events since the game started.")]
    public static object Origin(
        [ToolParameter(Description = "Regex on the thing id, the description (def label or designation def) or the map, ignoring case: MechGestator770243, MF_HaulToOtherLevel, Supercharger, Map-17")] string match = null,
        [ToolParameter(Description = "Only this action: designated, undesignated, spawned, despawned, destroyed")] string action = null,
        [ToolParameter(Description = "How many events")] int limit = 40
    )
    {
        List<OriginEntry> entries;
        lock (Sync)
        {
            entries = Origins.ToList();
        }

        var regex = match == null ? null : new Regex(match, RegexOptions.IgnoreCase);
        var found = entries
            .Where(e => (action == null || e.Action.Equals(action, StringComparison.OrdinalIgnoreCase))
                && (regex == null || new[] { e.ThingId, e.What, e.Map }.Any(s => s != null && regex.IsMatch(s))))
            .Reverse().ToList();
        return new
        {
            success = true,
            now = Tick,
            matched = found.Count,
            bySource = found.GroupBy(e => e.Mods.FirstOrDefault() ?? $"the game ({e.Game})").ToDictionary(g => g.Key, g => g.Count()),
            events = found.Take(limit).Select(e => new { e.Tick, e.Action, e.ThingId, e.What, e.Map, e.Where, by = e.Mods, game = e.Game }).ToList(),
            failedPatches = Failed
        };
    }

    private static string Plain(
        string text
    ) => new string(text.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
        .Select(char.ToLowerInvariant).ToArray());
}
