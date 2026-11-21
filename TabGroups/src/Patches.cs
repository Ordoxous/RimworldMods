using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.TabGroups;

[StaticConstructorOnStartup]
public static class Patches
{
    public static readonly List<Type> Patched = new();

    static Patches()
    {
        TabGroups.Rebuild();

        // Workers may override Visible without calling base (KeyzReferenceTools, mech tabs), so every getter declared on
        // a worker class gets the postfix, not only MainButtonWorker's
        var harmony = new Harmony(typeof(Patches).Namespace);
        var postfix = new HarmonyMethod(typeof(Patches), nameof(Visible_Postfix));
        var workers = new[] { typeof(MainButtonWorker) }
            .Concat(typeof(MainButtonWorker).AllSubclasses())
            .Where(type => type != typeof(MainButtonWorker_TabGroup));
        foreach (var worker in workers)
        {
            var getter = AccessTools.DeclaredPropertyGetter(worker, nameof(MainButtonWorker.Visible));
            if (getter == null || getter.IsAbstract)
                continue;
            if (harmony.TryPatch(getter, $"{worker.Name}.Visible", $"{worker.Name}'s buttons stay on the bar when grouped", postfix: postfix))
                Patched.Add(worker);
        }
    }

    public static void Visible_Postfix(
        MainButtonWorker __instance,
        ref bool __result
    )
    {
        if (__result && TabGroups.IsHidden(__instance.def))
            __result = false;
    }
}
