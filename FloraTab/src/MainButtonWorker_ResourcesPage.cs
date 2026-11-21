using RimWorld;

namespace Ordo.FloraTab;

// Given to the game's Animals and Wildlife buttons, which this mod hides, so their hotkeys (F4 and F5 by default) open
// their pages of the Resources tab instead
public class MainButtonWorker_ResourcesPage : MainButtonWorker_ToggleTab
{
    public override void Activate() => MainTabWindow_Flora.TogglePageFor(def);
}
