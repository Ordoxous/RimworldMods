using System.Linq;
using System.Runtime.CompilerServices;
using BiotechPatch;
using BiotechPatch.HemogenFarmAnyone;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

[StaticConstructorOnStartup]
public class HemogenTilSet
{
    private static ThingDef hemogenPackDef;

    static HemogenTilSet()
    {
        if (!ModsConfig.IsActive("biotechpatch.1trickPwnyta"))
            return;

        Patch(new Harmony(typeof(HemogenTilSet).Namespace));

        hemogenPackDef = ThingDef.Named("HemogenPack");
    }

    // Kept out of the static constructor so BiotechPatch types are only resolved when the mod is active
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Patch(Harmony harmony)
    {
        harmony.Patch(
            AccessTools.Method(typeof(Comp_HemogenFarm), nameof(Comp_HemogenFarm.CompTick)),
            prefix: new HarmonyMethod(Prefix_Comp_HemogenFarm_CompTick)
        );
    }

    public static bool Prefix_Comp_HemogenFarm_CompTick(Comp_HemogenFarm __instance)
    {
        if (
            !BiotechPatchSettings.HemogenFarmAnyone
            || !__instance.HemogenFarmEnabled
            || __instance.Pawn.IsPrisoner
            || !__instance.Pawn.IsHashIntervalTick(15000)
            || !SanguophageUtility.CanSafelyBeQueuedForHemogenExtraction(__instance.Pawn)
        )
        {
            return false;
        }
        
        var thingsOfDef = __instance.Pawn.Map.listerThings.ThingsOfDef(hemogenPackDef);
        var aggregate = thingsOfDef.Aggregate(0, (acc, curr) => acc + curr.stackCount);
        if (aggregate >= 30) {
            return false;
        }

        HealthCardUtility.CreateSurgeryBill(__instance.Pawn, RecipeDefOf.ExtractHemogenPack, null, sendMessages: false);
        return false;
    }
}
