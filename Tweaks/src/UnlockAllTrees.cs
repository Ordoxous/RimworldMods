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
        harmony.Patch(
            AccessTools.Method(typeof(GameComponent_UnlockedCrops), nameof(GameComponent_UnlockedCrops.FinalizeInit)),
            postfix: new HarmonyMethod(Postfix_GameComponent_UnlockedCrops_FinalizeInit)
        );
    }

    public static void Postfix_GameComponent_UnlockedCrops_FinalizeInit(
        HashSet<ThingDef> ___unlockedCrops
    )
    {
        var woodLogDef = ThingDef.Named("WoodLog");
        ___unlockedCrops.AddRange(
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