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
            AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage)),
            postfix: new HarmonyMethod(Postfix_Thing_TakeDamage)
        );
    }

    public static void Postfix_Thing_TakeDamage(
        DamageInfo dinfo,
        Thing __instance,
        DamageWorker.DamageResult __result
    ) {
        if (!TweaksSettings.ArmorIndicators)
        {
            return;
        }

        var damageBeforeMitigation = dinfo.Amount;
        var damageAfterMitigation = __result.totalDamageDealt;

        // A killing blow despawns the pawn, so fall back to where its corpse is held
        var map = __instance.MapHeld;
        if (__instance is not Pawn || map is null)
        {
            return;
        }

        var position = __instance.Spawned ? __instance.DrawPos : __instance.PositionHeld.ToVector3Shifted();

        if (__result.deflected || __result.diminished)
        {
            MoteMaker.ThrowText(
                position,
                map,
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
            position,
            map,
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