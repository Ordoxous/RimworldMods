using HarmonyLib;
using RimWorld;
using Unity.Collections;
using Verse;

namespace Ordo.DisallowedZone;

[StaticConstructorOnStartup]
public static class Patches
{
    static Patches()
    {
        var harmony = new Harmony("Ordo.DisallowedZone");

        harmony.Patch(
            AccessTools.PropertyGetter(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.EffectiveAreaRestrictionInPawnCurrentMap)),
            postfix: new HarmonyMethod(EffectiveAreaRestriction_Postfix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(AreaSource), nameof(AreaSource.DataForArea)),
            prefix: new HarmonyMethod(DataForArea_Prefix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(Area), nameof(Area.MarkDirty)),
            postfix: new HarmonyMethod(AreaMarkDirty_Postfix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(Area), nameof(Area.Invert)),
            postfix: new HarmonyMethod(AreaChanged_Postfix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(Area), nameof(Area.Clear)),
            postfix: new HarmonyMethod(AreaChanged_Postfix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(AreaManager), nameof(AreaManager.Remove)),
            postfix: new HarmonyMethod(AreaRemoved_Postfix)
        );
        harmony.Patch(
            AccessTools.Method(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls)),
            postfix: new HarmonyMethod(PlaySettings_Postfix)
        );
    }

    /// <summary>
    /// While a zone is on, a pawn that respects allowed areas gets its area minus the zone. Every
    /// vanilla allowed-area check reads this property, so they all see the zone.
    /// </summary>
    private static void EffectiveAreaRestriction_Postfix(
        Pawn_PlayerSettings __instance,
        ref Area __result
    )
    {
        if (DisallowedZone.ActiveZones.Count == 0)
            return;
        var map = __instance.pawn.MapHeld;
        if (map == null)
            return;
        var zone = DisallowedZone.ActiveOn(map);
        if (zone == null)
            return;
        // A null result is either an unrestricted pawn or one that ignores allowed areas.
        if (__result == null && !__instance.RespectsAllowedArea)
            return;
        __result = zone.EffectiveAreaFor(__result);
    }

    /// <summary>The path finder's bit grids cover only the area manager's areas.</summary>
    private static bool DataForArea_Prefix(
        Area area,
        ref NativeBitArray __result
    )
    {
        if (area is not EffectiveArea effective)
            return true;
        __result = effective.Bits;
        return false;
    }

    private static void AreaMarkDirty_Postfix(
        Area __instance,
        IntVec3 c
    )
    {
        if (__instance is not EffectiveArea)
            DisallowedZone.For(__instance.Map)?.Notify_BaseAreaCellChanged(__instance, c);
    }

    private static void AreaChanged_Postfix(Area __instance)
    {
        if (__instance is not EffectiveArea)
            DisallowedZone.For(__instance.Map)?.Notify_BaseAreaChanged(__instance);
    }

    private static void AreaRemoved_Postfix(
        AreaManager __instance,
        Area area
    )
    {
        DisallowedZone.For(__instance.map)?.Notify_BaseAreaRemoved(area);
    }

    /// <summary>A show/hide icon for the zone among the bottom-right toggles, once the map has a zone.</summary>
    private static void PlaySettings_Postfix(
        WidgetRow row,
        bool worldView
    )
    {
        if (worldView || Find.CurrentMap == null)
            return;
        var zone = DisallowedZone.For(Find.CurrentMap);
        if (zone == null || (zone.CellCount == 0 && !zone.Active))
            return;
        var value = zone.Visible;
        row.ToggleableIcon(ref value, Textures.Toggle, "Ordo_DisallowedZone_VisibilityTip".Translate(), SoundDefOf.Mouseover_ButtonToggle);
        zone.Visible = value;
    }
}
