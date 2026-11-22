using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MultiFloors;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// With MultiFloors, the Architect's costs count stored resources on every level of the stack, not just the one shown.
/// Each level is its own map with its own resource counter, so vanilla marks a cost red ("not enough stored") when the
/// steel is in a stockpile one floor down, even though MultiFloors' haulers will fetch it from there
/// (<c>WorkGiver_ConstructDeliverResourcesAcrossLevel</c>). MultiFloors already lists other levels' materials in the
/// stuff menu (<c>HarmonyPatch_AllowDesignatorBuildWithoutResourcesOnCurrentMap</c>); this does the same for the counts.
/// A transpiler swaps <see cref="ResourceCounter.GetCount"/> for <see cref="GetCount"/> in:
/// - <c>Designator_Build.DrawPlaceMouseAttachments</c>: the cost next to the cursor while placing
/// - <c>Designator_Build.DrawPanelReadout</c>: the cost in the Architect's info panel
/// - <c>Designator_Build.StuffDef</c>: whether the default material is in stock, before vanilla picks another one
/// - <c>Designator_Paint.DrawMouseAttachments</c>: the dye next to the cursor while painting
/// The top-left resource readout still shows only the current level.
/// I Clearly Have Enough (Mlie.IClearlyHaveEnough) transpiles the first two methods too: it drops the
/// <c>ldfld Map.resourceCounter</c> and calls its own <c>GetPresentOnMap(Map, ThingDef)</c>, which counts everything on
/// the map, stored or not. This transpiler runs after it, so it sees that call and swaps it for
/// <see cref="PresentAcrossLevels"/>, which adds up that mod's count on every level. Run before it, ours would have
/// replaced the call it looks for while it still removed the counter from the stack.
/// Everything here touches MultiFloors' types, so it's only patched when MultiFloors is active.
/// </summary>
public static class MultiFloorsResources
{
    private const string ClearlyHaveEnoughHarmonyId = "com.chippedchap.iclearlyhaveenough";
    private const string ClearlyHaveEnoughNamespace = "IClearlyHaveEnough";

    private static readonly MethodInfo VanillaGetCount = AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCount));
    private static readonly MethodInfo AcrossLevelsGetCount = AccessTools.Method(typeof(MultiFloorsResources), nameof(GetCount));
    private static readonly MethodInfo AcrossLevelsPresent = AccessTools.Method(typeof(MultiFloorsResources), nameof(PresentAcrossLevels));

    // I Clearly Have Enough's count for one map, taken from the call its transpiler left
    private static Func<Map, ThingDef, int> presentOnMap;

    public static void Patch(
        Harmony harmony
    )
    {
        var transpiler = new HarmonyMethod(typeof(MultiFloorsResources), nameof(Transpiler))
        {
            priority = Priority.Last,
            after = new[] { ClearlyHaveEnoughHarmonyId }
        };
        const string lost = "costs on other levels aren't counted";
        harmony.TryPatch(
            AccessTools.Method(typeof(Designator_Build), "DrawPlaceMouseAttachments"),
            "Designator_Build.DrawPlaceMouseAttachments",
            lost,
            transpiler: transpiler
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(Designator_Build), nameof(Designator_Build.DrawPanelReadout)),
            "Designator_Build.DrawPanelReadout",
            lost,
            transpiler: transpiler
        );
        harmony.TryPatch(
            AccessTools.PropertyGetter(typeof(Designator_Build), nameof(Designator_Build.StuffDef)),
            "Designator_Build.StuffDef",
            lost,
            transpiler: transpiler
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(Designator_Paint), nameof(Designator_Paint.DrawMouseAttachments)),
            "Designator_Paint.DrawMouseAttachments",
            lost,
            transpiler: transpiler
        );
    }

    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase original
    )
    {
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(VanillaGetCount))
            {
                // Same stack: the counter, then the def
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = AcrossLevelsGetCount;
                replaced++;
            }
            else if (IsClearlyHaveEnoughCount(instruction.operand as MethodInfo) is { } present)
            {
                // Same stack: the map, then the def
                presentOnMap ??= (Func<Map, ThingDef, int>)present.CreateDelegate(typeof(Func<Map, ThingDef, int>));
                instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                instruction.operand = AcrossLevelsPresent;
                replaced++;
            }

            yield return instruction;
        }

        if (replaced == 0)
            Log.Warning($"[Tweaks] Found no ResourceCounter.GetCount call in {original.DeclaringType?.Name}.{original.Name}, "
                + "so its costs on other levels aren't counted.");
    }

    // I Clearly Have Enough's static GetPresentOnMap(Map, ThingDef), found by name since Tweaks has no reference to it
    private static MethodInfo IsClearlyHaveEnoughCount(
        MethodInfo method
    )
    {
        if (method is not { IsStatic: true, Name: "GetPresentOnMap" }
            || method.DeclaringType?.Namespace != ClearlyHaveEnoughNamespace
            || method.ReturnType != typeof(int))
            return null;

        var parameters = method.GetParameters();
        return parameters.Length == 2 && parameters[0].ParameterType == typeof(Map) && parameters[1].ParameterType == typeof(ThingDef)
            ? method
            : null;
    }

    // I Clearly Have Enough's count of everything on the map, plus the same on every other level of the tile
    public static int PresentAcrossLevels(
        Map map,
        ThingDef def
    )
    {
        var count = presentOnMap(map, def);
        if (map == null || !TweaksSettings.ResourcesAcrossLevels)
            return count;

        try
        {
            if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
                return count;

            foreach (var level in controller.GetAllLevelMaps(except: map))
                count += presentOnMap(level, def);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't count resources on other levels: {e}", 0x7E5A11C1);
        }

        return count;
    }

    // The counter's own count, plus every other level's on the same tile
    public static int GetCount(
        ResourceCounter counter,
        ThingDef def
    )
    {
        var count = counter.GetCount(def);
        var map = counter.map;
        if (map == null || !TweaksSettings.ResourcesAcrossLevels || def.resourceReadoutPriority == ResourceCountPriority.Uncounted)
            return count;

        try
        {
            if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
                return count;

            foreach (var level in controller.GetAllLevelMaps(except: map))
                count += level.resourceCounter.GetCount(def);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[Tweaks] Couldn't count resources on other levels: {e}", 0x7E5A11C0);
        }

        return count;
    }
}
