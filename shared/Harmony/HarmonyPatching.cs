using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Ordo;

// Compiled into every mod whose About.xml depends on Harmony (see Directory.Build.targets). Internal, so each mod has
// its own copy and no mod depends on another's.
internal static class HarmonyPatching
{
    private static readonly string ModName = typeof(HarmonyPatching).Assembly.GetName().Name;

    // Patches original, or logs why not and returns false, so one missing target costs only its own feature. A
    // [StaticConstructorOnStartup] constructor that throws skips every patch after the throw, and leaves its class
    // unusable for the rest of the game.
    //   target: what original was looked up as, such as "Pawn.Kill", for the log line
    //   lost: what stops working without the patch, such as "riders don't drop their saddles on death"
    public static bool TryPatch(
        this Harmony harmony,
        MethodBase original,
        string target,
        string lost,
        HarmonyMethod prefix = null,
        HarmonyMethod postfix = null,
        HarmonyMethod transpiler = null,
        HarmonyMethod finalizer = null
    )
    {
        if (original == null)
        {
            Log.Warning($"[{ModName}] Couldn't find {target}, so {lost}.");
            return false;
        }

        try
        {
            harmony.Patch(original, prefix, postfix, transpiler, finalizer);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"[{ModName}] Couldn't patch {target}, so {lost}: {e}");
            return false;
        }
    }
}
