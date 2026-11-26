using Verse;

namespace Ordo.Tweaks;

/// <summary>
/// Empty, kept so saves made while Tweaks cleared MultiFloors' stuck claims still load cleanly: a game component is saved by
/// its class name, and a missing class is an error on load. The feature itself is in the MultiFloors fork now
/// (<c>MultiFloors.Ordo.StuckClaims</c>, run by <c>MultiFloorManager</c>).
/// </summary>
public class GameComponent_MultiFloorsStuckClaims : GameComponent
{
    public GameComponent_MultiFloorsStuckClaims(
        Game game
    )
    {
    }
}
