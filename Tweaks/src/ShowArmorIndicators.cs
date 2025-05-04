using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.Tweaks;

[StaticConstructorOnStartup]
public static class ShowArmorIndicators {

    static ShowArmorIndicators()
    {
        var harmony = new Harmony(typeof(ShowArmorIndicators).Namespace);

        harmony.Patch(
            typeof(Thing).GetMethod(nameof(Thing.TakeDamage)),
            postfix: new HarmonyMethod(Postfix_Thing_TakeDamage)
        );
    }

    public static void Postfix_Thing_TakeDamage(
        DamageInfo dinfo,
        Thing __instance,
        DamageWorker.DamageResult __result
    ) {
        var damageBeforeMitigation = dinfo.Amount;
        var damageAfterMitigation = __result.totalDamageDealt;

        if (__instance is not Pawn || __instance.Map is null)
        {
            return;
        }

        if (__result.deflected || __result.diminished)
        {
            MoteMaker.ThrowText(
                __instance.DrawPos, 
                __instance.Map,
                new StringBuilder().AppendStriked((damageBeforeMitigation - damageAfterMitigation).ToString("F0")).ToString(),
                __result.deflectedByMetalArmor || __result.diminishedByMetalArmor ? Color.white : Color.cyan, 
                3.65f
            );
        }

        if (damageAfterMitigation < 0.01)
        {
            return;
        }

        MoteMaker.ThrowText(
            __instance.DrawPos, 
            __instance.Map,
            damageAfterMitigation.ToString("F0"), 
            Color.red, 
            3.65f
        );
    }

    private static StringBuilder AppendStriked(this StringBuilder sb, string text)
    {
        foreach (var character in text)
            sb.Append(character).Append('\u0336');

        return sb;
    }
}