using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools;

// Why something isn't happening in a running game, in one call each: why a colonist won't work on a thing, who patches a
// method, which of several mods' same-named DLLs actually loaded, and a colonist by name on any map. Built after a
// session that pieced each of these together by hand. Safe on the player's own game, except that asking a work giver may
// do what it does when it finds a job (MultiFloors' cross-level givers can drop what the pawn carries).
public sealed class DiagnoseTools
{
    [Tool("ordo/why_not", Description = "Why a colonist won't work on a thing: every work giver that could apply to it, with the work type's priority for the pawn, ShouldSkip, HasJobOnThing (normal and forced) and the game's own fail reason, the job it would give, and for a bill giver each bill's ShouldDoNow and whether the pawn may start it. With the pawn on another map (a MultiFloors level), it's asked as MultiFloors' cross-level scan asks: moved there for the look and back.")]
    public static object WhyNot(
        [ToolParameter(Description = "The colonist's name (nickname, first or last name, accents ignored)")] string pawn,
        [ToolParameter(Description = "The thing's id (as ordo/find_things gives, with or without Thing_), or a regex on its def name: the first match")] string thing
    )
    {
        var (worker, pawnError) = FindPawn(pawn);
        if (worker == null)
        {
            return new { success = false, message = pawnError };
        }

        var target = FindThing(thing);
        if (target == null)
        {
            return new { success = false, message = $"No thing matching '{thing}' on any map" };
        }

        var targetMap = target.MapHeld;
        var visit = worker.Map != targetMap ? EnterLevel(worker, targetMap, target) : null;
        if (worker.Map != targetMap && visit == null)
        {
            return new { success = false, message = $"{worker.LabelShort} is on {worker.Map} and {target} on {targetMap}, and MultiFloors couldn't move the pawn there for a look" };
        }

        var previous = FloatMenuMakerMap.makingFor;
        try
        {
            // The game only fills in fail reasons for the pawn a float menu is being made for
            FloatMenuMakerMap.makingFor = worker;
            var givers = DefDatabase<WorkGiverDef>.AllDefsListForReading
                .Where(def => def.Worker is WorkGiver_Scanner scanner && Applies(scanner, def, worker, target))
                .Select(def => Ask((WorkGiver_Scanner)def.Worker, def, worker, target))
                .ToList();
            var bills = (target as IBillGiver)?.BillStack?.Bills.Select(bill =>
            {
                JobFailReason.Clear();
                var allowed = bill.PawnAllowedToStartAnew(worker);
                return new { bill = bill.LabelCap.ToString(), shouldDoNow = bill.ShouldDoNow(), bill.suspended, pawnAllowed = allowed, reason = JobFailReason.Reason };
            }).ToList();
            return new
            {
                success = true,
                pawn = worker.LabelShort,
                pawnMap = (visit != null ? $"{visit.Value.Home} (looked from {targetMap})" : worker.Map.ToString()),
                thing = target.ToString(),
                thingMap = targetMap?.ToString(),
                canReserveAndReach = worker.CanReserveAndReach(target, PathEndMode.Touch, Danger.Deadly),
                forbidden = target.IsForbidden(worker),
                givers,
                bills
            };
        }
        finally
        {
            FloatMenuMakerMap.makingFor = previous;
            visit?.Leave();
        }
    }

    // A work giver applies if its thing request takes the thing, it's one of its fixed bill givers, or (with no request)
    // its global list holds it
    private static bool Applies(
        WorkGiver_Scanner scanner,
        WorkGiverDef def,
        Pawn pawn,
        Thing thing
    )
    {
        try
        {
            if (def.fixedBillGiverDefs != null && def.fixedBillGiverDefs.Contains(thing.def))
            {
                return true;
            }

            var request = scanner.PotentialWorkThingRequest;
            if (request.group != ThingRequestGroup.Undefined || request.singleDef != null)
            {
                return request.Accepts(thing) && def.fixedBillGiverDefs == null;
            }

            return scanner.PotentialWorkThingsGlobal(pawn)?.Contains(thing) == true;
        }
        catch
        {
            return false;
        }
    }

    private static object Ask(
        WorkGiver_Scanner scanner,
        WorkGiverDef def,
        Pawn pawn,
        Thing thing
    )
    {
        string Try(Func<string> ask)
        {
            try
            {
                return ask();
            }
            catch (Exception e)
            {
                return $"threw {e.GetType().Name}: {e.Message}";
            }
        }

        var priority = pawn.workSettings?.GetPriority(def.workType) ?? 0;
        var skip = Try(() => scanner.ShouldSkip(pawn).ToString());
        JobFailReason.Clear();
        var has = Try(() => scanner.HasJobOnThing(pawn, thing).ToString());
        var reason = JobFailReason.Reason;
        JobFailReason.Clear();
        var forced = Try(() => scanner.HasJobOnThing(pawn, thing, forced: true).ToString());
        var forcedReason = JobFailReason.Reason;
        var job = has == "True" ? Try(() => scanner.JobOnThing(pawn, thing)?.def.defName ?? "none") : null;
        return new
        {
            giver = def.defName,
            workType = def.workType?.defName,
            priority,
            disabled = def.workType != null && pawn.WorkTypeIsDisabled(def.workType),
            shouldSkip = skip,
            hasJob = has,
            reason,
            hasJobForced = forced,
            forcedReason,
            job
        };
    }

    // MultiFloors' LevelVisit, by reflection: these tools build without the fork
    private readonly struct Visit
    {
        public readonly object Handle;
        public readonly Map Home;

        public Visit(
            object handle,
            Map home
        )
        {
            Handle = handle;
            Home = home;
        }

        public void Leave() => (Handle as IDisposable)?.Dispose();
    }

    private static Visit? EnterLevel(
        Pawn pawn,
        Map map,
        Thing near
    )
    {
        var enter = AccessTools.Method("MultiFloors.Ordo.LevelVisit:Enter");
        if (enter == null || map == null)
        {
            return null;
        }

        var home = pawn.Map;
        var cell = near.def.hasInteractionCell ? near.InteractionCell : near.PositionHeld;
        var handle = enter.Invoke(null, new object[] { pawn, map, cell, "ordo/why_not" });
        return handle.GetType().GetProperty("Entered")?.GetValue(handle) is true ? new Visit(handle, home) : null;
    }

    [Tool("ordo/patches", Description = "Every Harmony patch on a method (all its overloads): prefixes, postfixes, transpilers and finalizers in order, with priority, the patch method and the mod it comes from. Or, with owner, every method a Harmony id patches.")]
    public static object Patches(
        [ToolParameter(Description = "The method, as Namespace.Type:Method or Namespace.Type.Method, such as Verse.ListerThings:ThingsOfDef")] string method = null,
        [ToolParameter(Description = "Instead: a Harmony id (or part of one), such as Telardo.MultiFloors, for every method it patches")] string owner = null,
        [ToolParameter(Description = "How many methods to list for an owner")] int limit = 100
    )
    {
        if (!string.IsNullOrEmpty(owner))
        {
            var patched = Harmony.GetAllPatchedMethods()
                .Select(m => (Method: m, Info: Harmony.GetPatchInfo(m)))
                .Where(p => p.Info.Owners.Any(o => o.IndexOf(owner, StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(p => Describe(p.Method))
                .OrderBy(name => name)
                .ToList();
            return new { success = true, owner, count = patched.Count, methods = patched.Take(limit).ToList() };
        }

        if (string.IsNullOrEmpty(method))
        {
            return new { success = false, message = "Give a method, or an owner" };
        }

        var split = method.Contains(':') ? method.LastIndexOf(':') : method.LastIndexOf('.');
        var type = split > 0 ? AccessTools.TypeByName(method.Substring(0, split)) : null;
        if (type == null)
        {
            return new { success = false, message = $"No type for '{method}'" };
        }

        var name = method.Substring(split + 1);
        var overloads = AccessTools.GetDeclaredMethods(type).Cast<MethodBase>().Concat(AccessTools.GetDeclaredConstructors(type))
            .Where(m => m.Name == name || (name is "ctor" or ".ctor" && m.IsConstructor))
            .ToList();
        if (overloads.Count == 0)
        {
            return new { success = false, message = $"{type.FullName} has no method {name}" };
        }

        return new
        {
            success = true,
            overloads = overloads.Select(m =>
            {
                var info = Harmony.GetPatchInfo(m);
                return new
                {
                    method = Describe(m),
                    prefixes = Patches(info?.Prefixes),
                    postfixes = Patches(info?.Postfixes),
                    transpilers = Patches(info?.Transpilers),
                    finalizers = Patches(info?.Finalizers)
                };
            }).ToList()
        };
    }

    private static List<string> Patches(
        IEnumerable<Patch> patches
    ) => patches?.OrderBy(p => p.index).Select(p =>
            $"{p.owner} (priority {p.priority}): {p.PatchMethod.DeclaringType?.FullName}.{p.PatchMethod.Name}, from {ModOf(p.PatchMethod.DeclaringType?.Assembly)}")
        .ToList() ?? new List<string>();

    private static string Describe(
        MethodBase method
    ) => $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

    private static string ModOf(
        Assembly assembly
    )
    {
        if (assembly == null)
        {
            return "?";
        }

        var mod = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(assembly));
        return mod != null ? $"{mod.Name} [{mod.PackageId}]" : assembly.GetName().Name;
    }

    [Tool("ordo/assembly_conflicts", Description = "DLLs that more than one active mod ships under the same assembly name (a library bundled by several mods, or one mod's copy shadowing another's): each copy's mod, file, version and date, and which copy the game loaded. Only one assembly of a name loads, so the others' code never runs.")]
    public static object AssemblyConflicts(
        [ToolParameter(Description = "Include Harmony (0Harmony), which many mods bundle and the Harmony mod's copy always wins")] bool includeHarmony = false
    )
    {
        var copies = new List<(string Name, Version Version, ModContentPack Mod, FileInfo File)>();
        foreach (var mod in LoadedModManager.RunningModsListForReading)
        {
            foreach (var folder in mod.foldersToLoadDescendingOrder)
            {
                var assemblies = Path.Combine(folder, "Assemblies");
                if (!Directory.Exists(assemblies))
                {
                    continue;
                }

                foreach (var file in new DirectoryInfo(assemblies).GetFiles("*.dll"))
                {
                    try
                    {
                        var name = AssemblyName.GetAssemblyName(file.FullName);
                        copies.Add((name.Name, name.Version, mod, file));
                    }
                    catch
                    {
                        // A native DLL, not an assembly
                    }
                }
            }
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies().GroupBy(a => a.GetName().Name).ToDictionary(g => g.Key, g => g.ToList());
        var conflicts = copies
            .GroupBy(c => c.Name)
            .Where(g => g.Select(c => c.Mod).Distinct().Count() > 1 && (includeHarmony || g.Key != "0Harmony"))
            .Select(g => new
            {
                assembly = g.Key,
                loadedFrom = loaded.TryGetValue(g.Key, out var list)
                    ? list.Select(a => ModOf(a) + (string.IsNullOrEmpty(a.Location) ? "" : $" ({a.Location})")).ToList()
                    : new List<string> { "none loaded" },
                copies = g.Select(c => $"{c.Mod.Name} [{c.Mod.PackageId}]: {c.File.FullName}, v{c.Version}, {c.File.LastWriteTime:yyyy-MM-dd}, {c.File.Length} bytes").ToList()
            })
            .OrderBy(c => c.assembly)
            .ToList();
        return new { success = true, count = conflicts.Count, conflicts };
    }

    [Tool("ordo/pawn", Description = "A pawn by name on any map or in the world: where it is, its job and queue, drafted and mental state, work priorities, a mechanitor's bandwidth and mechs, hediffs, low needs, and what it carries.")]
    public static object PawnInfo(
        [ToolParameter(Description = "Nickname, first or last name, or the full name; accents and case are ignored")] string name
    )
    {
        var (pawn, error) = FindPawn(name);
        if (pawn == null)
        {
            return new { success = false, message = error };
        }

        var mechanitor = pawn.mechanitor;
        return new
        {
            success = true,
            name = pawn.Name?.ToStringFull ?? pawn.LabelShort,
            id = pawn.ThingID,
            kind = pawn.kindDef?.defName,
            faction = pawn.Faction?.Name,
            map = pawn.MapHeld?.ToString() ?? "world",
            position = pawn.PositionHeld.ToString(),
            job = pawn.CurJob == null ? null : $"{pawn.CurJob.def.defName}: {pawn.jobs.curDriver?.GetReport()}",
            queued = pawn.jobs?.jobQueue?.Select(q => q.job.def.defName).ToList(),
            drafted = pawn.Drafted,
            mentalState = pawn.MentalStateDef?.defName,
            downed = pawn.Downed,
            work = pawn.workSettings == null || !pawn.workSettings.EverWork
                ? null
                : DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => pawn.workSettings.GetPriority(w) > 0).OrderBy(w => pawn.workSettings.GetPriority(w))
                    .Select(w => $"{w.defName} {pawn.workSettings.GetPriority(w)}").ToList(),
            mechanitor = mechanitor == null
                ? null
                : new
                {
                    bandwidth = $"{mechanitor.UsedBandwidth} of {mechanitor.TotalBandwidth} used",
                    fromGestation = mechanitor.UsedBandwidthFromGestation,
                    mechs = mechanitor.OverseenPawns.Select(m => $"{m.LabelShort} ({m.kindDef.defName}, {m.MapHeld?.ToString() ?? "away"})").ToList()
                },
            hediffs = pawn.health?.hediffSet?.hediffs.Select(h => h.LabelCap.ToString()).ToList(),
            lowNeeds = pawn.needs?.AllNeeds.Where(n => n.CurLevelPercentage < 0.3f).Select(n => $"{n.def.defName} {n.CurLevelPercentage:P0}").ToList(),
            carrying = pawn.carryTracker?.CarriedThing?.LabelCap.ToString(),
            inventory = pawn.inventory?.innerContainer.Select(t => t.LabelCap.ToString()).ToList()
        };
    }

    // Colonists first, then every other pawn on the maps and in the world. Names are compared without accents or case
    private static (Pawn Pawn, string Error) FindPawn(
        string name
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "Give a pawn's name");
        }

        var wanted = Plain(name);
        var candidates = Find.Maps.SelectMany(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAlive).Distinct()
            .OrderByDescending(p => p.IsColonist).ToList();
        bool Is(Pawn pawn, Func<string, bool> test) =>
            new[] { pawn.LabelShort, (pawn.Name as NameTriple)?.Nick, (pawn.Name as NameTriple)?.First, (pawn.Name as NameTriple)?.Last, pawn.Name?.ToStringFull, pawn.ThingID }
                .Where(n => !string.IsNullOrEmpty(n)).Any(n => test(Plain(n)));
        var match = candidates.FirstOrDefault(p => Is(p, n => n == wanted)) ?? candidates.FirstOrDefault(p => Is(p, n => n.Contains(wanted)));
        return match != null ? (match, null) : (null, $"No pawn called '{name}'");
    }

    private static string Plain(
        string text
    )
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            plain.Append(char.ToLowerInvariant(c));
        }

        return plain.ToString();
    }

    private static Thing FindThing(
        string query
    )
    {
        var id = query.StartsWith("Thing_", StringComparison.OrdinalIgnoreCase) ? query.Substring(6) : query;
        var all = Find.Maps.SelectMany(map =>
        {
            var held = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), held, allowUnreal: false);
            return held;
        }).ToList();
        var byId = all.FirstOrDefault(t => string.Equals(t.ThingID, id, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
        {
            return byId;
        }

        var regex = new System.Text.RegularExpressions.Regex(query, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return all.FirstOrDefault(t => regex.IsMatch(t.def.defName));
    }
}
