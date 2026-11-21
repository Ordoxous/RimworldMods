using RimWorld;

namespace Ordo.NatureTab;

// Given to the game's Animals and Wildlife buttons, which this mod hides, so their hotkeys (F4 and F5 by default) open
// their pages of the Nature tab instead
public class MainButtonWorker_NaturePage : MainButtonWorker_ToggleTab
{
    public override void Activate() => MainTabWindow_Nature.TogglePageFor(def);
}
