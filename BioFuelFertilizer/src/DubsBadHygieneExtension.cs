using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DubsBadHygiene;
using HarmonyLib;
using Verse;

namespace Ordo.BioFuelFertilizer;

public static class DubsBadHygieneExtension
{
    public static bool IsAvailable()
    {
        return AccessTools.TypeByName("DubsBadHygieneMod") != null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PatchDubsBadHygiene(Harmony harmony)
    {
        harmony.Patch(
            typeof(DefExtensions).GetMethod(nameof(DefExtensions.GiveFuel)),
            prefix: new HarmonyMethod(Prefix_DubsBadHygiene_DefExtensions_GiveFuel)
        );
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IEnumerable<string> DubsBadHygieneConfigErrors()
    {
        var dubsBadHygieneMod = LoadedModManager.GetMod<DubsBadHygieneMod>();
        if (dubsBadHygieneMod != null && dubsBadHygieneMod.GetSettings<Settings>().Hydroponics)
        {
            yield return $"{nameof(CompProperties_RefuelableRareTicking)} is incompatible with {nameof(DubsBadHygieneMod)} {nameof(Settings.Hydroponics)}";
        }
    }

    private static bool Prefix_DubsBadHygiene_DefExtensions_GiveFuel(bool Switch)
    {
        // We define our own CompProperties_Refuelable instance that DubsBadHygiene removes, we want to skip their implementation if aquaponics is disabled
        // See: DubsBadHygiene.DefExtensions.GiveFuel
        return Switch;
    }
}