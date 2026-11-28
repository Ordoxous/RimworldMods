using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace Ordo.BridgeTools;

// Which mod some code belongs to, from a live stack or a logged stack trace's text: the first frames whose assembly is a
// mod's, skipping the game, the runtime, Harmony and these tools. Shared by the history and the error grouping.
public static class Attribution
{
    private static readonly ConcurrentDictionary<Assembly, string> ModByAssembly = new();
    private static readonly ConcurrentDictionary<string, string> ModByTypeName = new();

    // "at Namespace.Type.Method(" or "at Namespace.Type+Nested.<Method>b__0(" in a stack trace's text
    private static readonly Regex Frame = new(@"^\s*at\s+(?<type>[\w.+`<>]+)\.(?<method>[\w<>`|$.]+?)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);

    // "- PREFIX owner: Original <- Patch" lines Harmony adds to a stack trace
    private static readonly Regex PatchLine = new(@"^\s*-\s*(PREFIX|POSTFIX|TRANSPILER|FINALIZER)\s+(?<owner>[^:]+):", RegexOptions.Compiled | RegexOptions.Multiline);

    // A mod's name and package id, or null for the game, the runtime, Harmony and these tools
    public static string ModOf(
        Assembly assembly
    )
    {
        if (assembly == null)
        {
            return null;
        }

        return ModByAssembly.GetOrAdd(assembly, a =>
        {
            if (a == typeof(Attribution).Assembly)
            {
                return null;
            }

            var mod = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(a));
            // The bridge is on the stack of everything it steps or calls, so it says nothing about who did it
            if (mod == null || mod.IsCoreMod || mod.IsOfficialMod || mod.PackageId.Equals("brrainz.harmony", StringComparison.OrdinalIgnoreCase)
                || mod.PackageId.Equals("brrainz.rimbridgeserver", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"{mod.Name} [{mod.PackageId}]";
        });
    }

    /// <summary>The first mod frames of the calling stack, outermost last, and the nearest game frame for context.</summary>
    public static (List<string> Mods, string Game) FromStack(
        int skip,
        int take = 3
    )
    {
        var mods = new List<string>();
        string game = null;
        foreach (var frame in new StackTrace(skip + 1, false).GetFrames())
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (type == null)
            {
                continue;
            }

            var mod = ModOf(type.Assembly);
            if (mod != null)
            {
                if (mods.Count < take)
                {
                    mods.Add($"{mod}: {type.Name}.{method.Name}");
                }
            }
            else if (game == null && type.Assembly.GetName().Name == "Assembly-CSharp")
            {
                game = $"{type.Name}.{method.Name}";
            }

            if (mods.Count >= take && game != null)
            {
                break;
            }
        }

        return (mods, game);
    }

    /// <summary>The mod a logged message most likely comes from: its first mod frame, else a "[Tag]" its text starts with,
    /// else the game, naming the Harmony owners that patch the frames it went through.</summary>
    public static string FromLogged(
        string text,
        string stackTrace
    )
    {
        var trace = stackTrace ?? "";
        foreach (Match match in Frame.Matches(trace))
        {
            var mod = ModOfTypeName(match.Groups["type"].Value);
            if (mod != null)
            {
                return mod;
            }
        }

        var tag = Regex.Match(text ?? "", @"^\s*\[(?<tag>[^\]]{2,40})\]");
        if (tag.Success)
        {
            return $"[{tag.Groups["tag"].Value}] (by its log prefix)";
        }

        var owners = PatchLine.Matches(trace).Select(m => m.Groups["owner"].Value.Trim()).Distinct().ToList();
        return owners.Count == 0 ? "the game (no mod frame)" : $"the game, through patches by {string.Join(", ", owners.Take(4))}";
    }

    private static string ModOfTypeName(
        string name
    ) => ModByTypeName.GetOrAdd(name, n =>
    {
        // Compiler-generated nested types (<>c, <Method>d__1) belong to their outer type
        var outer = Regex.Replace(n, @"\+<.*$|\+<>.*$", "");
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(outer, false);
            }
            catch
            {
                continue;
            }

            if (type != null)
            {
                return ModOf(assembly);
            }
        }

        return null;
    });
}
