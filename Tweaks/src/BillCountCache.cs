using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// Reuses a bill's "Do until you have X" count for a quarter of a second when the count is asked for outside a game tick,
/// which is the bill tab and the bill dialog drawing their "54/60" labels. Those redraw every frame, and a bill tab like
/// Nice Bill Tab asks for each bill's count several times a frame.
/// - A count is cheap in vanilla, but mods add to it. Better Workbench Management counts every extra product a bill's
///   "count additional items" filter allows, one full count each (60 for a meal bill that counts every meal), and Tweaks'
///   BillsAcrossLevels counts each MultiFloors level again. Four such meal bills took the bill tab from 72 to 12 FPS.
/// - Counts asked for during a tick (pawns deciding whether to work a bill) are always fresh, so the colony's behaviour
///   doesn't change. Only what's drawn can be up to 0.25 s old.
/// - A cached count is dropped when the bill's own counting options change (equipped, tainted, hit points, quality,
///   stuff, stockpile), so the bill dialog shows the new count at once.
/// - It covers every counter's <c>CountProducts</c>, vanilla and modded. Only the outermost call is cached: a counter
///   calling its base, or BillsAcrossLevels counting another level, goes straight through.
/// - On a cache answer the original and the patches inside it are skipped. Better Workbench Management's postfix and
///   BillsAcrossLevels' are skipped too, and a last postfix puts the cached count back, so another mod's postfix that adds
///   to the count can't add twice.
/// </summary>
[StaticConstructorOnStartup]
public static class BillCountCache
{
    private const float MaxAgeSeconds = 0.25f;

    private sealed class Entry
    {
        public int Count;
        public float Time;
        public CountOptions Options;
    }

    // What a bill counts by. A change here changes the count, so a cached count with other options isn't used
    private readonly struct CountOptions
    {
        private readonly bool equipped;
        private readonly bool tainted;
        private readonly bool stuff;
        private readonly bool acrossLevels;
        private readonly FloatRange hp;
        private readonly QualityRange quality;
        private readonly ISlotGroup group;

        public CountOptions(
            Bill_Production bill
        )
        {
            equipped = bill.includeEquipped;
            tainted = bill.includeTainted;
            stuff = bill.limitToAllowedStuff;
            acrossLevels = TweaksSettings.BillsAcrossLevels;
            hp = bill.hpRange;
            quality = bill.qualityRange;
            group = bill.GetIncludeSlotGroup();
        }

        public bool SameAs(
            in CountOptions other
        ) => equipped == other.equipped && tainted == other.tainted && stuff == other.stuff && acrossLevels == other.acrossLevels
             && hp.min == other.hp.min && hp.max == other.hp.max && quality.min == other.quality.min && quality.max == other.quality.max
             && group == other.group;
    }

    private static readonly ConditionalWeakTable<Bill_Production, Entry> Counts = new();

    // CountProducts calls in progress on this thread, so only the outermost one is cached
    [ThreadStatic] private static int depth;

    // The outermost call in progress was answered from the cache, or will store what it counts
    [ThreadStatic] private static bool answered;
    [ThreadStatic] private static bool storing;
    [ThreadStatic] private static int answer;

    // Inside TickManager.DoSingleTick, where counts must be fresh
    private static int ticking;

    /// <summary>True while a count is being answered from the cache, for postfixes that would otherwise add to it.</summary>
    public static bool Answered => answered;

    static BillCountCache()
    {
        var harmony = new Harmony(typeof(BillCountCache).Namespace);
        const string lost = "bill counts aren't cached for the bill tab";
        var prefix = new HarmonyMethod(typeof(BillCountCache), nameof(Prefix_CountProducts)) { priority = Priority.First };
        var postfix = new HarmonyMethod(typeof(BillCountCache), nameof(Postfix_CountProducts)) { priority = Priority.Last };
        var finalizer = new HarmonyMethod(typeof(BillCountCache), nameof(Finalizer_CountProducts));
        foreach (var type in typeof(RecipeWorkerCounter).AllSubclasses().Prepend(typeof(RecipeWorkerCounter)))
        {
            var method = AccessTools.DeclaredMethod(type, nameof(RecipeWorkerCounter.CountProducts), [typeof(Bill_Production)]);
            if (method != null && !method.IsAbstract)
            {
                harmony.TryPatch(method, $"{type.Name}.CountProducts", lost, prefix: prefix, postfix: postfix, finalizer: finalizer);
            }
        }

        tickPatched = harmony.TryPatch(
            AccessTools.Method(typeof(TickManager), nameof(TickManager.DoSingleTick)),
            "TickManager.DoSingleTick",
            "bill counts are only cached while the game is paused, so pawns can't be given a stale one",
            prefix: new HarmonyMethod(typeof(BillCountCache), nameof(Prefix_DoSingleTick)),
            finalizer: new HarmonyMethod(typeof(BillCountCache), nameof(Finalizer_DoSingleTick))
        );

        // Better Workbench Management's postfix is the expensive part; skip it when the cache answered
        var betterWorkbenches = GenTypes.GetTypeInAnyAssembly(MultiFloorsBills.BetterWorkbenchesCounter);
        if (betterWorkbenches != null)
        {
            harmony.TryPatch(
                AccessTools.DeclaredMethod(betterWorkbenches, "Postfix"),
                $"{MultiFloorsBills.BetterWorkbenchesCounter}.Postfix",
                "Better Workbench Management still counts in full each time the bill tab asks",
                prefix: new HarmonyMethod(typeof(BillCountCache), nameof(Prefix_SkipWhenAnswered))
            );
        }
    }

    // Without the tick patch, ticks can't be told apart, so counts are only cached while the game is paused
    private static readonly bool tickPatched;

    public static bool Prefix_CountProducts(
        Bill_Production bill,
        ref int __result
    )
    {
        depth++;
        if (depth != 1 || !Cacheable(bill))
        {
            return true;
        }

        var options = new CountOptions(bill);
        if (Counts.TryGetValue(bill, out var entry) && Time.realtimeSinceStartup - entry.Time < MaxAgeSeconds && entry.Options.SameAs(options))
        {
            answer = entry.Count;
            answered = true;
            __result = answer;
            return false;
        }

        storing = true;
        return true;
    }

    public static void Postfix_CountProducts(
        Bill_Production bill,
        ref int __result
    )
    {
        if (depth != 1)
        {
            return;
        }

        if (answered)
        {
            __result = answer;
            return;
        }

        if (!storing)
        {
            return;
        }

        var entry = Counts.GetValue(bill, _ => new Entry());
        entry.Count = __result;
        entry.Time = Time.realtimeSinceStartup;
        entry.Options = new CountOptions(bill);
    }

    public static void Finalizer_CountProducts()
    {
        if (--depth <= 0)
        {
            depth = 0;
            answered = false;
            storing = false;
        }
    }

    public static bool Prefix_SkipWhenAnswered() => !answered;

    public static void Prefix_DoSingleTick() => ticking++;

    public static void Finalizer_DoSingleTick() => ticking--;

    private static bool Cacheable(
        Bill_Production bill
    ) => bill != null && ticking == 0 && UnityData.IsInMainThread && TweaksSettings.CacheBillCounts
         && (tickPatched || Find.TickManager is { Paused: true });

    /// <summary>Drops every cached count, for tests that change what's on the map and count again at once.</summary>
    public static void Clear() => Counts.Clear();
}
