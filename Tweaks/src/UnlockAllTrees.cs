using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ProgressionAgriculture;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

[StaticConstructorOnStartup]
public static class UnlockAllTrees
{

    static UnlockAllTrees()
    {
        if (!ModsConfig.IsActive("ferny.progressionagriculture"))
            return;

        Patch(new Harmony(typeof(UnlockAllTrees).Namespace));
    }

    // Kept out of the static constructor so ProgressionAgriculture types are only resolved when the mod is active
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Patch(Harmony harmony)
    {
        // GameComponent_UnlockedCrops doesn't override FinalizeInit, so the base method is the only one to patch
        harmony.Patch(
            AccessTools.DeclaredMethod(typeof(GameComponent), nameof(GameComponent.FinalizeInit)),
            postfix: new HarmonyMethod(Postfix_GameComponent_FinalizeInit)
        );
    }

    public static void Postfix_GameComponent_FinalizeInit(GameComponent __instance)
    {
        if (__instance is not GameComponent_UnlockedCrops || !TweaksSettings.UnlockAllTrees)
            return;

        var unlockedCrops = (HashSet<ThingDef>) AccessTools
            .Field(typeof(GameComponent_UnlockedCrops), "unlockedCrops")
            .GetValue(__instance);

        var woodLogDef = ThingDef.Named("WoodLog");
        unlockedCrops.AddRange(
            DefDatabase<ThingDef>.AllDefsListForReading
                .Where(x => x?.plant is { Sowable: true }
                            && (
                                x.plant is { treeCategory: not TreeCategory.None }
                                || (x.plant.harvestedThingDef?.Equals(woodLogDef) ?? false)
                            )
                )
        );
    }
}