using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// Lets one gravship substructure be built over another of a different kind, such as Transparent Substructure's over
/// vanilla's, or back. Vanilla refuses any foundation over a foundation ("FoundationAlreadyExists").
/// - A transpiler on <c>GenConstruct.CanPlaceBlueprintAt_NewTemp</c> swaps the first <c>TerrainGrid.FoundationAt</c>
///   call, the one behind that check, for <see cref="FoundationBlocking"/>, which hides the old substructure from it.
///   The method's later <c>FoundationAt</c> call, which lets buildings on the cell keep the foundation's affordances,
///   is left alone.
/// - The frame's completion already swaps the foundation in place (<c>SetFoundation</c> overwrites it), so the cell
///   stays substructure throughout and what stands on it stays put. A prefix and postfix on
///   <c>Frame.CompleteConstruction</c> drop the old substructure's materials, as removing it would.
/// </summary>
[StaticConstructorOnStartup]
public static class ReplaceSubstructure
{
    private static readonly MethodInfo VanillaFoundationAt = AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.FoundationAt), [typeof(IntVec3)]);

    static ReplaceSubstructure()
    {
        if (!ModsConfig.OdysseyActive)
            return;

        var harmony = new Harmony(typeof(ReplaceSubstructure).Namespace);
        const string lost = "substructure can't be built over another kind of substructure";
        harmony.TryPatch(
            AccessTools.Method(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp)),
            "GenConstruct.CanPlaceBlueprintAt_NewTemp",
            lost,
            transpiler: new HarmonyMethod(typeof(ReplaceSubstructure), nameof(Transpiler_CanPlaceBlueprintAt))
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(Frame), nameof(Frame.CompleteConstruction)),
            "Frame.CompleteConstruction",
            "replacing substructure doesn't give back the old substructure's materials",
            prefix: new HarmonyMethod(typeof(ReplaceSubstructure), nameof(Prefix_CompleteConstruction)),
            postfix: new HarmonyMethod(typeof(ReplaceSubstructure), nameof(Postfix_CompleteConstruction))
        );
    }

    /// <summary>Whether <paramref name="placing"/> may replace the foundation <paramref name="existing"/>.</summary>
    public static bool CanReplace(BuildableDef placing, TerrainDef existing) =>
        existing != null
        && placing is TerrainDef { isFoundation: true } terrain
        && terrain != existing
        && terrain.IsSubstructure
        && existing.IsSubstructure
        && TweaksSettings.ReplaceSubstructure;

    public static IEnumerable<CodeInstruction> Transpiler_CanPlaceBlueprintAt(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (!replaced && instruction.Calls(VanillaFoundationAt))
            {
                // Stack: the grid, the cell. Add the def being placed
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ReplaceSubstructure), nameof(FoundationBlocking)))
                    .MoveLabelsFrom(instruction);
                replaced = true;
                continue;
            }

            yield return instruction;
        }

        if (!replaced)
            Log.Warning("[Tweaks] Found no FoundationAt call in GenConstruct.CanPlaceBlueprintAt_NewTemp, so substructure can't be built over another kind of substructure.");
    }

    // The foundation that stops entDef being placed at c: the real one, or none when entDef may replace it
    public static TerrainDef FoundationBlocking(TerrainGrid grid, IntVec3 c, BuildableDef entDef)
    {
        var foundation = grid.FoundationAt(c);
        return CanReplace(entDef, foundation) ? null : foundation;
    }

    public static void Prefix_CompleteConstruction(Frame __instance, out (TerrainDef old, IntVec3 cell, Map map) __state)
    {
        __state = default;
        var map = __instance.Map;
        if (map == null || __instance.def.entityDefToBuild is not TerrainDef { isFoundation: true })
            return;

        var old = map.terrainGrid.FoundationAt(__instance.Position);
        if (old != null && old != __instance.def.entityDefToBuild)
            __state = (old, __instance.Position, map);
    }

    public static void Postfix_CompleteConstruction((TerrainDef old, IntVec3 cell, Map map) __state)
    {
        var (old, cell, map) = __state;
        // Only when the new foundation actually went in, so a failed completion doesn't hand out materials
        if (old == null || map.terrainGrid.FoundationAt(cell) == old)
            return;

        GenLeaving.DoLeavingsFor(old, cell, map);
    }
}
