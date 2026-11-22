using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.NatureTab;

// A page's chosen area, kept by name (or as the home area), since areas belong to one map: the same choice carries to
// another map, as when a gravship moves the colony, wherever that map has an area of that name. Saved with the game by
// GameComponent_NatureTab.
public sealed class AreaChoice : IExposable
{
    // Null means anywhere
    private string label;
    private bool isHome;

    // The area last found, so a rename on its map is followed
    private Area found;

    public void ExposeData()
    {
        Scribe_Values.Look(ref label, "label");
        Scribe_Values.Look(ref isHome, "isHome");
    }

    public void Set(Area chosen)
    {
        label = chosen?.Label;
        isHome = chosen is Area_Home;
        found = chosen;
    }

    // The area on this map. A map without it is surveyed anywhere, and the choice is kept for a map that has it.
    public Area On(Map map)
    {
        if (map == null || label == null)
            return null;
        if (isHome)
            return map.areaManager.Home;
        if (found != null && found.Map == map && map.areaManager.AllAreas.Contains(found))
        {
            label = found.Label;
            return found;
        }
        // Not kept when there's none, so a map that's gone isn't held on to
        found = map.areaManager.AllAreas.FirstOrDefault(a => a.Label == label && a.AssignableAsAllowed());
        return found;
    }
}
