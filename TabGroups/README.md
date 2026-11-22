# TabGroups

Shows "Tab Groups" in game, package ID `Ordo.TabGroups`. It folds the bottom bar's main buttons into a few **group buttons**, so a long modded bar fits again. A group button always shows the group's name. It never changes to show one of its tabs.

- **Click** (left or right) lists every member, with its hotkey. The open member is in yellow. A click never opens a tab by itself.
- **The button lights up** while one of its members is open.
- **Hotkeys** still open members directly.
- **Research's progress bar** shows on the Knowledge button. The bar shows the highest progress of any member, and only Research has one.

## Groups

The defaults are in `mod/Defs/MainButtons.xml`, chosen for the author's mod list (2026-11-21, 640 mods). Names that aren't loaded are skipped, so the mod depends on none of them.

| Group | Order | Default members |
|---|---|---|
| Colonists | 10 | Work, Schedule, Numbers (`kNumbersOverviewTab`), Assign, MultiFloors' floor settings (which floors each pawn may use), VQE Drones, Mechs, Mech Work Tab, WVC Mechs Work, Dubs Mint gear/needs/health |
| Knowledge | 60 | Research, Ideoligions, WeaponStats, Keyz Reference Tools, WikiRim |
| World | 70 | World, Quests, Factions (or Relations Tab), VFE Empire's Royalty |
| Other | 475 | Mod-specific extras: RJW, RimWorld Animations offsets, NL Facial Animation, Missile Girl, SQUISHY Ranged recoil |
| Tools | 480 | History, Mint bills, Ctrl-F, Visible Wealth, Mint minimap, Paint Shop, AAAA, Dubs Performance Analyzer, XML Extensions mod settings |

Architect, Nature (NatureTab) and Menu stay on the bar. In the settings, any button with `buttonVisible` can be moved into any group, or back onto the bar. A move is saved at once. Groups are built from every button, visible or not, because some mods turn `buttonVisible` on after startup (Animation Manager does once a game loads), and they are rebuilt when a mod adds a button def from code later (Character Editor). Only the changes from the defaults are saved (`assignments`, defName to group defName, or `""` for on the bar), so new defaults still apply to buttons the player never touched.

## Dependencies

- **Hard:** Harmony.
- **DLCs:** none.

## How it works

| Part | Where |
|---|---|
| Group defs: `MainButtonDef`s with `MainButtonWorker_TabGroup` and a `TabGroupExtension` listing default members | `mod/Defs/MainButtons.xml`, `src/TabGroups.cs` |
| Membership, the current member and the last one used, rebuilt from the defs and settings at startup and on every settings change | `src/TabGroups.cs` |
| The button: drawing, click and right-click, the menu, the tooltip and the dots | `src/MainButtonWorker_TabGroup.cs` |
| Hiding members: a postfix on the `Visible` getter of `MainButtonWorker` **and of every subclass that declares one**, since workers like Biotech's `MainButtonWorker_ToggleMechTab` override it without calling base | `src/Patches.cs` |
| Settings | `src/TabGroupsMod.cs` |

- **Hotkeys need nothing:** `MainButtonsRoot.MainButtonsOnGUI` checks the hotkey of every def in `allButtonsInOrder`, visible or not, so a hidden member's hotkey still opens it.
- **Whether a member would show:** `TabGroups.WouldShow` reads the member's `Visible` with the postfix switched off. That's how a group leaves out members their own worker hides, such as Mechs with no mechs, and how it hides itself when none would show.
- **Which member is open:** the member whose tab is open, or World while the world is shown, since the world isn't a tab window. It lights the button and colours the member in the list.
- **Right-click is used**, so it doesn't fall through to `MainTabsRoot`, which opens Architect on a right-click with nothing selected.
- **Until 2026-11-21** a click opened the member used last, and the button showed that member's icon. It was changed to always list the members, so the bar doesn't change under you.
- **Group order** is fixed in XML, not taken from the members, because `MainButtonsRoot` sorts its list once when a game loads.
- **A member whose worker does its work in `DoButton`**, rather than `Activate`, wouldn't work from a group. None is known.

## Testing

**Profiles:**
- `tabgroups`: Harmony, Core, Biotech, RimBridgeServer and the mod. Biotech brings the Mechs button, whose worker overrides `Visible`.
- `tabgroups-headless`: the same with no window.

**Suites** (`tools/BridgeTools/Scenarios/TabGroupsScenarios.cs`): `setup`, `bar`, `open` and `settings`. Drawing is checked by eye in the windowed profile.

**Results, 2026-11-22:** 9 of 9 pass on `tabgroups-headless`, with no errors logged, after adding `settings_hidden_and_late_buttons` (a button hidden by its def still goes into the group it was moved to and shows in its menu once visible; a button def added after startup joins its group). The settings file is put back empty after the run.

**Results, 2026-11-21:** 8 of 8 pass on `tabgroups-headless`, with no errors logged. In the windowed profile, the bar shows Architect, Colonists, Animals, Wildlife, Knowledge, World, Tools and Menu.

## Open questions

- Icons for the groups. They show text labels now, while mods like Bradson's Main Button Icons give every other button an icon.
- Untested with the full mod list: it should be run once there to check every default member's worker opens from a group.
