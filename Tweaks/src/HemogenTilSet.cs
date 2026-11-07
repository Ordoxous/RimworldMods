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
    static HemogenTilSet()
    {
        if (!ModsConfig.IsActive("biotechpatch.1trickPwnyta"))
            return;

        Patch(new Harmony(typeof(HemogenTilSet).Namespace));
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
        // Caravan and transport pod pawns still tick comps but have no map
        if (
            !__instance.Pawn.Spawned
            || !__instance.Pawn.IsHashIntervalTick(15000)
            || !BiotechPatchSettings.HemogenFarmAnyone
            || !__instance.HemogenFarmEnabled
            || __instance.Pawn.IsPrisoner
            || !SanguophageUtility.CanSafelyBeQueuedForHemogenExtraction(__instance.Pawn)
            || __instance.Pawn.BillStack.Bills.Any(b => b.recipe == RecipeDefOf.ExtractHemogenPack)
        )
        {
            return false;
        }
        
        var packCount = __instance.Pawn.Map.listerThings.ThingsOfDef(ThingDefOf.HemogenPack).Sum(t => t.stackCount);
        if (packCount >= 30) {
            return false;
        }

        HealthCardUtility.CreateSurgeryBill(__instance.Pawn, RecipeDefOf.ExtractHemogenPack, null, sendMessages: false);
        return false;
    }
}
