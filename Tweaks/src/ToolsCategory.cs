using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.Tweaks;

// Done in C# because weapons usually inherit thingCategories, so a patch can't match them on the same node as
// equippedStatOffsets. It also needs the stat's category, to leave out things like the minigun's move speed penalty.
[StaticConstructorOnStartup]
public static class ToolsCategory
{
    static ToolsCategory()
    {
        if (!TweaksSettings.ToolsCategory)
            return;

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
