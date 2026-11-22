using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MultiFloors;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors, a "Do until you have X" bill counts the product on every level of the stack, not just the bill
/// giver's. Each level is its own map, so vanilla's count looks only at the workbench's level: a kitchen upstairs keeps
/// cooking while the freezer downstairs is full. MultiFloors fetches ingredients across levels
/// (<c>GlobalBillGiverMonitor</c>) but leaves the product count alone.
/// - A transpiler swaps every <c>bill.Map</c> in the counters for <see cref="MapOf"/>, which is the bill's map, or the
///   level being counted while <see cref="counting"/> is set. That covers vanilla's
///   <c>RecipeWorkerCounter.CountProducts</c> and its private <c>GetCarriedCount</c>, and the butchering and stone block
///   counters, which override <c>CountProducts</c>.
/// - A postfix on each <c>CountProducts</c> then runs the same counter once for every other level and adds what it finds,
///   so every rule the bill sets (hit points, quality, tainted, equipped) applies there too.
/// - Better Workbench Management adds to the count in its own postfix (additional products, things outside stockpiles,
///   things pawns carry), reading <c>bill.Map</c> itself. Without the same swap there, each extra level added the bill's
///   own level's share again. Its helper, <c>RecipeWorkerCounter_CountProducts_Detour.CountProducts</c>, gets the
///   transpiler too; its away-pawn count then matches no pawn on another level, so caravans count once.
/// A bill counting only one stockpile (<c>GetIncludeSlotGroup</c>) is left alone: that stockpile is on one level.
/// Modded counters that override <c>CountProducts</c> still count only the bill's level.
/// Everything here touches MultiFloors' types, so it's only patched when MultiFloors is active.
/// </summary>
public static class MultiFloorsBills
{
    private static readonly MethodInfo BillMap = AccessTools.PropertyGetter(typeof(Bill), nameof(Bill.Map));
    private static readonly MethodInfo MapOfMethod = AccessTools.Method(typeof(MultiFloorsBills), nameof(MapOf));

    // The level being counted, or null to count the bill's own map
    private static Map counting;

    public static void Patch(
        Harmony harmony
    )
    {
        const string lost = "bills don't count products on other levels";
        var transpiler = new HarmonyMethod(typeof(MultiFloorsBills), nameof(Transpiler));
        var postfix = new HarmonyMethod(typeof(MultiFloorsBills), nameof(Postfix_CountProducts));
        foreach (var type in new[] { typeof(RecipeWorkerCounter), typeof(RecipeWorkerCounter_ButcherAnimals), typeof(RecipeWorkerCounter_MakeStoneBlocks) })
        {
            harmony.TryPatch(
                AccessTools.DeclaredMethod(type, nameof(RecipeWorkerCounter.CountProducts)),
                $"{type.Name}.CountProducts",
                lost,
                postfix: postfix,
                transpiler: transpiler
            );
        }

        harmony.TryPatch(
            AccessTools.Method(typeof(RecipeWorkerCounter), "GetCarriedCount"),
            "RecipeWorkerCounter.GetCarriedCount",
            "bills don't count products carried on other levels",
            transpiler: transpiler
        );

        // Better Workbench Management, or its unofficial 1.7 port: same type and method in both
        var betterWorkbenches = GenTypes.GetTypeInAnyAssembly(BetterWorkbenchesCounter);
        if (betterWorkbenches != null)
        {
            harmony.TryPatch(
                AccessTools.DeclaredMethod(betterWorkbenches, "CountProducts"),
                $"{BetterWorkbenchesCounter}.CountProducts",
                "Better Workbench Management's extra counts are added once more for each other level",
                transpiler: transpiler
            );
        }
    }

    public const string BetterWorkbenchesCounter = "ImprovedWorkbenches.RecipeWorkerCounter_CountProducts_Detour";

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original
    )
    {
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(BillMap))
            {
                // Same stack: the bill
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = MapOfMethod;
                replaced++;
            }

            yield return instruction;
        }

        if (replaced == 0)
        {
            Log.Warning($"[Tweaks] Found no Bill.Map in {original.DeclaringType?.Name}.{original.Name}, so its bills don't count other levels.");
        }
    }

    public static Map MapOf(
        Bill bill
    ) => counting ?? bill.Map;

    public static void Postfix_CountProducts(
        RecipeWorkerCounter __instance,
        Bill_Production bill,
        ref int __result
    )
    {
        // While counting another level, the counter calls itself through this postfix again. A count BillCountCache
        // answered already includes the other levels
        if (counting != null || BillCountCache.Answered || !TweaksSettings.BillsAcrossLevels || bill?.Map == null || bill.GetIncludeSlotGroup() != null)
        {
            return;
        }

        try
        {
            __result += CountOtherLevels(__instance, bill);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't count a bill's products on other levels: {e}", 0x7E5A11C2);
        }
        finally
        {
            counting = null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CountOtherLevels(
        RecipeWorkerCounter counter,
        Bill_Production bill
    )
    {
        var map = bill.Map;
        if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return 0;
        }

        var count = 0;
        foreach (var level in controller.GetAllLevelMaps(except: map))
        {
            counting = level;
            count += counter.CountProducts(bill);
        }

        return count;
    }
}
