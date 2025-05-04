using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace MechUi.Harmony;

[StaticConstructorOnStartup]
public class SprinklerManagerHarmonyPatches
{

    static SprinklerManagerHarmonyPatches()
    {
        var harmony = new HarmonyLib.Harmony("MechUi.Harmony");

        harmony.Patch(
            typeof(FertilityGrid).GetMethod("CalculateFertilityAt", BindingFlags.NonPublic | BindingFlags.Instance),
            postfix: new HarmonyMethod(typeof(SprinklerManagerHarmonyPatches).GetMethod(nameof(Patch_FertilityGrid_CalculateFertilityAt)))
        );

        harmony.Patch(
            typeof(Building_PlantGrower).GetMethod(nameof(Building_PlantGrower.TickRare)),
            postfix: new HarmonyMethod(typeof(SprinklerManagerHarmonyPatches).GetMethod(nameof(Patch_Building_PlantGrower_TickRare)))
        );
    }

    public static void Patch_FertilityGrid_CalculateFertilityAt(IntVec3 loc, ref float __result, FertilityGrid __instance)
    {
        var map = Traverse.Create(__instance).Field<Map>("map").Value;
        var building = loc.GetEdifice(map);
        if (building != null && typeof(Building_PlantGrower).IsAssignableFrom(building.GetType()))
        {
            if (building.TryGetComp<CompRefuelable>(out var refuelable))
            {
                if (refuelable.HasFuel)
                {
                    __result *= 1.2f;
                }
            }
        }
    }

    public static void Patch_Building_PlantGrower_TickRare(Building_PlantGrower __instance)
    {
        if (__instance.TryGetComp<CompRefuelable>(out var refuelable))
        {
            refuelable.ConsumeFuel(((CompProperties_Refuelable) refuelable.props).fuelConsumptionRate / 60000f * 250f);
        }
    }
}