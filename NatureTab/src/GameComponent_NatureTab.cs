using Verse;

namespace Ordo.NatureTab;

// Saves each page's area and Within distance with the game. Areas are kept by name, which only means something in the
// colony they were chosen in, so the choices belong to the save rather than to the mod's settings.
public class GameComponent_NatureTab : GameComponent
{
    // RimWorld makes one for every new or loaded game, before a loaded game's data is read
    public GameComponent_NatureTab(Game game)
    {
        MainTabWindow_Nature.ResetChoices();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        MainTabWindow_Nature.ExposeChoices();
    }
}
