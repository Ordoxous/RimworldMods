using System.Collections.Generic;
using System.Linq;
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
        var harmony = new Harmony(typeof(UnlockAllTrees).Namespace);

        harmony.Patch(
            typeof(GameComponent_UnlockedCrops).GetMethod(nameof(GameComponent_UnlockedCrops.FinalizeInit)),
            postfix: new HarmonyMethod(Postfix_GameComponent_UnlockedCrops_FinalizeInit)
        );
    }

    public static void Postfix_GameComponent_UnlockedCrops_FinalizeInit(
        GameComponent_UnlockedCrops __instance
    )
    {
        var woodLogDef = ThingDef.Named("WoodLog");
        var unlockedCrops = Traverse.Create(__instance).Field<HashSet<ThingDef>>("unlockedCrops").Value;
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