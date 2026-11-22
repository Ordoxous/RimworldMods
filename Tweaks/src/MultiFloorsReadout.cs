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
/// With MultiFloors, the resource list at the top left shows the totals of every level of the stack, not just the level
/// shown. Each level is its own map with its own <c>ResourceCounter</c>, and the readout reads <c>Find.CurrentMap</c>'s.
/// Transpilers swap the counter's calls in the readout for the ones here, which add every other level's count:
/// - <c>ResourceReadout.DoReadoutSimple</c>: <c>AllCountedAmounts</c>, the list it draws (a resource only upstairs would
///   otherwise be skipped as 0)
/// - <c>ResourceReadout.DrawResourceSimple</c> and <c>Listing_ResourceReadout.DoThingDef</c>: <c>GetCount</c>
/// - <c>Listing_ResourceReadout.DoCategory</c>: <c>GetCountIn(ThingCategoryDef)</c>, the categorized view's totals
/// Mods that replace the readout aren't covered. Everything here touches MultiFloors' types, so it's only patched when
/// MultiFloors is active.
/// </summary>
public static class MultiFloorsReadout
{
    private static readonly MethodInfo VanillaAllCounted = AccessTools.PropertyGetter(typeof(ResourceCounter), nameof(ResourceCounter.AllCountedAmounts));
    private static readonly MethodInfo VanillaGetCount = AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCount));
    private static readonly MethodInfo VanillaGetCountIn = AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCountIn), [typeof(ThingCategoryDef)]);

    private static readonly Dictionary<MethodInfo, MethodInfo> Replacements = new()
    {
        [VanillaAllCounted] = AccessTools.Method(typeof(MultiFloorsReadout), nameof(AllCountedAmounts)),
        [VanillaGetCount] = AccessTools.Method(typeof(MultiFloorsReadout), nameof(GetCount)),
        [VanillaGetCountIn] = AccessTools.Method(typeof(MultiFloorsReadout), nameof(GetCountIn))
    };

    // The summed list, refilled on each call rather than made anew, since the readout draws every frame
    private static readonly Dictionary<ThingDef, int> Summed = new();

    public static void Patch(
        Harmony harmony
    )
    {
        const string lost = "the resource list doesn't count other levels";
        var transpiler = new HarmonyMethod(typeof(MultiFloorsReadout), nameof(Transpiler));
        harmony.TryPatch(AccessTools.Method(typeof(ResourceReadout), "DoReadoutSimple"), "ResourceReadout.DoReadoutSimple", lost, transpiler: transpiler);
        harmony.TryPatch(AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.DrawResourceSimple)), "ResourceReadout.DrawResourceSimple", lost,
            transpiler: transpiler);
        harmony.TryPatch(AccessTools.Method(typeof(Listing_ResourceReadout), nameof(Listing_ResourceReadout.DoCategory)), "Listing_ResourceReadout.DoCategory",
            lost, transpiler: transpiler);
        harmony.TryPatch(AccessTools.Method(typeof(Listing_ResourceReadout), "DoThingDef"), "Listing_ResourceReadout.DoThingDef", lost, transpiler: transpiler);
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original
    )
    {
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && Replacements.TryGetValue(method, out var replacement))
            {
                // Same stack: the counter, then the def if the call takes one
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = replacement;
                replaced++;
            }

            yield return instruction;
        }

        if (replaced == 0)
        {
            Log.Warning($"[Tweaks] Found no resource count in {original.DeclaringType?.Name}.{original.Name}, so the resource list doesn't count other levels there.");
        }
    }

    public static Dictionary<ThingDef, int> AllCountedAmounts(
        ResourceCounter counter
    )
    {
        var own = counter.AllCountedAmounts;
        var map = counter.map;
        if (map == null || !TweaksSettings.ReadoutAcrossLevels)
        {
            return own;
        }

        try
        {
            Summed.Clear();
            foreach (var (def, count) in own)
            {
                Summed[def] = count;
            }

            AddOtherLevels(map);
            return Summed;
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't count the resource list on other levels: {e}", 0x7E5A11C3);
            return own;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddOtherLevels(
        Map map
    )
    {
        if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return;
        }

        foreach (var level in controller.GetAllLevelMaps(except: map))
        {
            foreach (var (def, count) in level.resourceCounter.AllCountedAmounts)
            {
                Summed[def] = Summed.TryGetValue(def, out var sum) ? sum + count : count;
            }
        }
    }

    public static int GetCount(
        ResourceCounter counter,
        ThingDef def
    ) => counter.GetCount(def) + OnOtherLevels(counter, other => other.GetCount(def));

    public static int GetCountIn(
        ResourceCounter counter,
        ThingCategoryDef category
    ) => counter.GetCountIn(category) + OnOtherLevels(counter, other => other.GetCountIn(category));

    private static int OnOtherLevels(
        ResourceCounter counter,
        Func<ResourceCounter, int> count
    )
    {
        if (counter.map == null || !TweaksSettings.ReadoutAcrossLevels)
        {
            return 0;
        }

        try
        {
            return SumOtherLevels(counter.map, count);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't count the resource list on other levels: {e}", 0x7E5A11C4);
            return 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int SumOtherLevels(
        Map map,
        Func<ResourceCounter, int> count
    )
    {
        if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return 0;
        }

        var sum = 0;
        foreach (var level in controller.GetAllLevelMaps(except: map))
        {
            sum += count(level.resourceCounter);
        }

        return sum;
    }
}
