using System.Runtime.CompilerServices;
using MultiFloors;
using Verse;

namespace Ordo.BlueprintMaterials;

// MultiFloors (optional) makes each level of a base its own map, linked by stairs, and its haulers deliver materials to a
// blueprint from any level. Its types are only touched inside the NoInlining method, which runs only when it's active, so
// they're never resolved without it.
public static class MultiFloorsLevels
{
    private static readonly bool Active = ModsConfig.IsActive("telardo.MultiFloors");

    // Whether another level of the map's stack holds any of the material
    public static bool AnyOnOtherLevels(
        ThingDef materialDef,
        Map map
    ) => Active && AnyOnOtherLevelsInt(materialDef, map);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool AnyOnOtherLevelsInt(
        ThingDef materialDef,
        Map map
    )
    {
        if (!map.TryGetLevelControllerOnCurrentTile(out var controller))
        {
            return false;
        }

        foreach (var level in controller.GetAllLevelMaps(except: map))
        {
            if (level.listerThings.ThingsOfDef(materialDef).Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
