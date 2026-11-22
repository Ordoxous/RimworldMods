using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MultiFloors;
using Verse;

namespace Ordo.Saddlebound;

// MultiFloors (optional) makes each level of a base its own map, linked by stairs. Its types are only touched inside the
// NoInlining method, which runs only when it's active, so they're never resolved without it.
public static class MultiFloorsLevels
{
    public static readonly bool Active = ModsConfig.IsActive("telardo.MultiFloors");

    // The other levels of the map's stack, or none without MultiFloors
    public static IEnumerable<Map> OtherLevels(
        Map map
    ) => Active && map != null ? OtherLevelsInt(map) : Enumerable.Empty<Map>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<Map> OtherLevelsInt(
        Map map
    ) => map.TryGetLevelControllerOnCurrentTile(out var controller)
        ? controller.GetAllLevelMaps(except: map).ToList()
        : [];
}
