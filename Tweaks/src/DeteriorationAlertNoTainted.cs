using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// Leaves tainted apparel out of TD Enhancement Pack's "deteriorating things" alert. The apparel still deteriorates
/// as normal; only the alert stops counting it.
/// </summary>
[StaticConstructorOnStartup]
public static class DeteriorationAlertNoTainted
{
    private const string AlertTypeName = "TD_Enhancement_Pack.Alert_Deterioration";

    static DeteriorationAlertNoTainted()
    {
        if (!ModsConfig.IsActive("MemeGoddess.TDPack"))
            return;

        // By name, so Tweaks needs no reference to TD Enhancement Pack's assembly
        new Harmony(typeof(DeteriorationAlertNoTainted).Namespace).TryPatch(
            AccessTools.Method(AccessTools.TypeByName(AlertTypeName), nameof(Alert.GetReport)),
            $"{AlertTypeName}.GetReport",
            "tainted apparel stays in the deterioration alert",
            postfix: new HarmonyMethod(Postfix_GetReport)
        );
    }

    public static void Postfix_GetReport(ref AlertReport __result)
    {
        var culprits = __result.culpritsThings;
        if (!__result.active || culprits == null || !TweaksSettings.DeteriorationAlertSkipTainted)
            return;

        if (culprits.RemoveAll(t => t is Apparel { WornByCorpse: true }) > 0 && culprits.Count == 0)
            __result = AlertReport.Inactive;
    }
}
