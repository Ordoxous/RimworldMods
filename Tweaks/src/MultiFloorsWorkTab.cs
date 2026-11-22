using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MultiFloors;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors and Fluffy's Work Tab, colonists keep Work Tab's per-task and per-hour priorities. MultiFloors'
/// prioritized work scanner (its default) swaps the work giver list <c>JobGiver_Work.TryIssueJobPackage</c> reads for
/// two of its own, high and low priority, built in <c>HarmonyPatch_ScanJobsOnOtherLevelPrioritized.CacheWorkGiversInOrder</c>
/// from each work type's priority alone. Work Tab gives a work type the priority of the current hour, and MultiFloors then
/// adds every one of its work givers at it: a task switched off in Work Tab is still done, and the tasks inside a work
/// type lose their own order. This happens on every map of a tile with more than one level.
/// A prefix replaces that method for the pawns Work Tab manages. It splits Work Tab's own list (the vanilla
/// <see cref="Pawn_WorkSettings.WorkGiversInOrderNormal"/>, which Work Tab builds per task and hour) at MultiFloors'
/// threshold, comparing each task's own priority. Both mods mark their lists dirty on the same calls, including Work
/// Tab's change of hour, so they're rebuilt together.
/// Work Tab is reached by reflection; MultiFloors' types are only touched when it's active.
/// </summary>
public static class MultiFloorsWorkTab
{
    private static Func<Pawn, WorkGiverDef, int, int> workGiverPriority;
    private static Func<Pawn, bool> managedByWorkTab;
    private static Func<int> visibleHour;

    public static bool WorkTabActive => ModsConfig.IsActive("Fluffy.WorkTab");

    public static void Patch(
        Harmony harmony
    )
    {
        const string lost = "Work Tab's per-task priorities are lost with more than one level";
        var extensions = AccessTools.TypeByName("WorkTab.Pawn_Extensions");
        var tab = AccessTools.TypeByName("WorkTab.MainTabWindow_WorkTab");
        var priority = extensions == null
            ? null
            : AccessTools.Method(extensions, "GetPriority", new[] { typeof(Pawn), typeof(WorkGiverDef), typeof(int) });
        var humanlike = extensions == null ? null : AccessTools.Method(extensions, "Humanlike", new[] { typeof(Pawn) });
        var hour = tab == null ? null : AccessTools.PropertyGetter(tab, "VisibleHour");
        if (priority == null || humanlike == null || hour == null)
        {
            Log.Warning($"[Tweaks] Couldn't find Work Tab's priorities, so {lost}.");
            return;
        }

        workGiverPriority = (Func<Pawn, WorkGiverDef, int, int>)priority.CreateDelegate(typeof(Func<Pawn, WorkGiverDef, int, int>));
        managedByWorkTab = (Func<Pawn, bool>)humanlike.CreateDelegate(typeof(Func<Pawn, bool>));
        visibleHour = (Func<int>)hour.CreateDelegate(typeof(Func<int>));
        harmony.TryPatch(
            AccessTools.Method("MultiFloors.HarmonyPatches.HarmonyPatch_ScanJobsOnOtherLevelPrioritized:CacheWorkGiversInOrder"),
            "MultiFloors' HarmonyPatch_ScanJobsOnOtherLevelPrioritized.CacheWorkGiversInOrder",
            lost,
            prefix: new HarmonyMethod(typeof(MultiFloorsWorkTab), nameof(Prefix_CacheWorkGiversInOrder))
        );
    }

    public static bool Prefix_CacheWorkGiversInOrder(
        Pawn_WorkSettings workSettings
    )
    {
        var pawn = workSettings.pawn;
        if (workGiverPriority == null || !TweaksSettings.WorkTabPrioritiesAcrossLevels || !managedByWorkTab(pawn))
        {
            return true;
        }

        try
        {
            Split(workSettings, pawn);
            return false;
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't sort Work Tab's priorities for MultiFloors, so its own order is used: {e}", 0x7E5A11C2);
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Split(
        Pawn_WorkSettings workSettings,
        Pawn pawn
    )
    {
        ref var high = ref workSettings.HighPriorityWorkGiversInOrderNormal();
        high ??= new List<WorkGiver>();
        ref var low = ref workSettings.LowPriorityWorkGiversInOrderNormal();
        low ??= new List<WorkGiver>();
        high.Clear();
        low.Clear();

        // Work Tab's list: enabled tasks in its order, with emergency ones left out as vanilla does
        var threshold = MultiFloorsModHandler.Settings.WorkGiverHighPriorityThreshold;
        var hour = visibleHour();
        foreach (var giver in workSettings.WorkGiversInOrderNormal)
        {
            (workGiverPriority(pawn, giver.def, hour) <= threshold ? high : low).Add(giver);
        }

        workSettings.WorkGiversOrderDirty() = false;
    }
}
