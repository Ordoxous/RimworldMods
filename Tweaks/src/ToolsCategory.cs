using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

// Done in C# because weapons usually inherit thingCategories, so a patch can't match them on the same node as
// equippedStatOffsets. It also needs the stat's category, to leave out things like the minigun's move speed penalty.
[StaticConstructorOnStartup]
public static class ToolsCategory
{
    // Other mods can give weapons their work offsets after startup: Setosa's Power Tools queues a long event from its static
    // constructor that applies its settings. Waiting until every static constructor has run, then queueing behind whatever
    // they queued, catches those weapons whichever order the static constructors ran in.
    static ToolsCategory()
    {
        if (!TweaksSettings.ToolsCategory)
            return;

        LongEventHandler.ExecuteWhenFinished(() => LongEventHandler.QueueLongEvent(AddTools, "Ordo_Tweaks_ToolsCategory", false, null));
    }

    private static void AddTools()
    {
        var toolsCategory = ThingCategoryDef.Named("Ordo_Tools");

        foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading.Where(IsTool))
        {
            def.thingCategories.Add(toolsCategory);
            toolsCategory.childThingDefs.Add(def);

            // The category tree is already resolved by now, so its caches need updating too
            toolsCategory.allChildThingDefsCached.Add(def);
            foreach (var parent in toolsCategory.Parents)
                parent.allChildThingDefsCached.Add(def);
        }

        toolsCategory.sortedChildThingDefsCached = toolsCategory.childThingDefs.OrderBy(def => def.label).ToList();
    }

    private static bool IsTool(ThingDef def) =>
        def.IsWeapon
        && def.thingCategories != null
        && def.equippedStatOffsets != null
        && def.equippedStatOffsets.Any(offset => offset.value > 0f && offset.stat.category == StatCategoryDefOf.PawnWork);
}
