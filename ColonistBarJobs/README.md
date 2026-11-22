# ColonistBarJobs

Shows "Colonist Bar Job Icons" in game, package ID `Ordo.ColonistBarJobs`. It adds a small icon to each portrait on the colonist bar showing what that colonist is doing right now. Hovering the icon shows the job report ("Mining granite."), the bill and the progress, and clicking it jumps the camera to the job's target. With Biotech, mechs get icons on their portraits in the mechanitor's control group gizmos.

## Dependencies

- **Hard:** Harmony.
- **DLCs:** none needed. With **Biotech**, mechs' icons go on their portraits in the mechanitor's control group gizmos, and clearing pollution gets its own icon (that def has `MayRequire`).
- **Works with:** CM Color Coded Mood Bar (`CrashM.ColorCodedMoodBar.11`), checked in game on 2026-11-15 and 2026-11-16. It replaces `DrawColonist` with its own copy and its own icon list, so the activity row hooks its `MoodCache.DoIconVars` (to rearrange the list) and `MoodPatch.DrawIcons` (to draw over the placeholder) by name. If those names change, a warning is logged and the icon goes to its corner instead. Its bleeding mark sits on the body. Its other icons (illness, temperature, relationships, gender) weren't triggered.
- **Optional: MultiFloors** (`telardo.MultiFloors`, Workshop 3384660931, 1.6 only, loaded on 1.7 from its 1.6 folder). Each level of a base is its own map. A pawn with work only on another level first takes `MF_ChangeLevelThroughStair`, whose target is the stair, and MultiFloors keeps the job it will start on arrival in the pawn's `NextJobThinkResult` (a Prepatcher field; Waylith provides it without the Prepatcher mod). While a pawn is on that stair job, `IconFor` resolves the queued job instead, so the bar shows the work it's going to. With nothing queued (a plain move order) it shows the stair as before. The tooltip is still the stair job's report. Its types are only touched in `src/MultiFloorsLevels.cs`, in a NoInlining method run only when it's active.
- **Optional: [FSF] Complex Jobs** (`FrozenSnowFox.ComplexJobs`, Workshop 2069684319). It splits vanilla's work types, so its Harvesting, Taming, Training, Nurse, Surgeon and Rescue are listed beside the vanilla ones they split from, as `li` entries with `MayRequire`. Its other work types target a building or an item, whose icon says enough, or are covered by a job mapping (smoothing, snow, pollution). Not checked in game: the defNames come from its 1.7 XML.
- **Mods that replace the bar:** won't show the icons. **Colonist Bar KF** rewrites the whole bar, so this mod's postfix on the vanilla drawer never runs with it loaded.

## How it works

| Part | Where |
|---|---|
| **Activity row (default):** a transpiler on `ColonistBarColonistDrawer.DrawIcons` runs once vanilla has filled its row of status icons at the bottom left, just before it sizes them. When there's a job icon, it removes vanilla's calm activities (idle, sleeping, medical rest), puts a clear placeholder first so the row sizes and spaces itself as usual, and draws the job icon over that slot. The urgent states stay: forming caravan, mental break, fleeing, attacking, burning, inspired, and the slave, ideology role or faction icon. Sleeping shows no job icon, so vanilla's Z stays. | `src/ActivityRow.cs` |
| **Corner:** a postfix on `ColonistBarColonistDrawer.DrawColonist` draws the icon where the corner settings put it. It's also the fallback when the row couldn't be hooked. Both draw over a dark square, with the job driver's report as tooltip. | `src/ColonistBarJobIcons.cs` |
| **Tooltip:** the job driver's report, then the bill for a bill job ("Bill: Cook simple meal (3/10)", with `Bill_Production.RepeatInfoText`), then the progress where the game tracks it: a bill once its work has begun (`workLeft` against the recipe's total, as vanilla's progress bar works it out), a frame (`PercentComplete`), and research (the current project). Not mining: its report already gives the rock's health ("Digging at granite (82%)"), and a progress line beside it read as a contradiction. Progress is only for the pawn's current driver, so not for MultiFloors' queued job. Last, in grey, "Click to jump to <target>." | `src/JobDetails.cs` |
| **Click to jump:** a prefix on `ColonistBarColonistDrawer.HandleClicks`, which the bar calls before it draws and before the map's selector sees the click. On a left mouse-down over the rect where the colonist's icon was drawn in the last two frames, it jumps the camera to the job's target and uses the event, so the colonist isn't selected or dragged, and a double click doesn't jump to the colonist instead. The target is `targetA`'s thing wherever it is (a carried thing jumps to its carrier), else its cell on the pawn's map. The pawn itself and a cell of a job queued for another level aren't targets; with none, the click goes to the bar as usual. | `src/ColonistBarJobIcons.cs`, `src/JobDetails.cs` |
| **Mechs (Biotech):** mechs aren't on the bar, so a postfix on `MechanitorControlGroupGizmo.GizmoOnGUI` works out its portrait grid as the gizmo does (130 by 75, 6 px padding, a 30 px label row, then the largest square, stepping down a pixel at a time, that fits every mech, centred) and draws each mech's icon over its portrait's bottom right, at 45% of the portrait, with the job report as tooltip. Clicks stay the gizmo's: they select the mech and jump to it. A setting turns it off. | `src/MechGroupIcons.cs` |
| Settings: placement (activity row, the default, or a corner). For a corner: the corner (default top right), size (15-50% of the portrait width, default 30%), sideways and up-down offsets (±32 px at the vanilla portrait width, scaling with the bar), and backdrop darkness (0-100%, default 75%), with a live preview. With Biotech: show on mechs (default on). They apply at once. | `src/ColonistBarJobsSettings.cs` |
| The resolver decides what a job shows | `src/JobIcon.cs`, `JobIconResolver.For(Job)` |
| Mappings for jobs and work types, as `Ordo.ColonistBarJobs.JobIconDef` | `mod/Defs/JobIcons.xml` |

**Which icon a job gets** (first match wins):

1. **A `JobIconDef` listing the job's `JobDef`.** For jobs whose target would mislead: Deconstruct, Uninstall, Strip and Slaughter would otherwise show the building or animal they remove. Clean (the home area icon, rather than the filth), ClearSnow (snow clearing), ClearPollution (pollution clearing, Biotech), SmoothFloor and SmoothWall (smoothing, not the rock) and BeatFire (a firefoam popper) are mapped by job, not work type, so they show the same whichever work type gave them: snow clearing is vanilla Cleaning, and a drafted colonist beats fire with no work giver. The bar's own burning icon was passed over for fire, since a burning colonist keeps it in the row.
2. **A `JobIconDef` listing the work type of the job's work giver.** For work whose target is a pawn, a cell or plain rock: Mining, PlantCutting, Growing, Hunting, Handling, Warden (the prisoner icon), and Doctor (industrial medicine), plus Complex Jobs' splits of them. Jobs that didn't come from a work giver (orders, think tree jobs) have no work type.
3. **Recreation:** any job with a `joyKind` gets the joy icon (`UI/Icons/DrugPolicy/Joy`) unless it targets a thing with an icon (`targetFirst`), so watching TV shows the TV and skygazing or a walk shows the joy icon. Wandering (`GotoWander`, `Wait_Wander`) shows vanilla's idle icon, as a job mapping in step 1. Sleeping (`LayDown`) is mapped to `showNothing`, so it draws no icon, not even the bed: vanilla's own Z at the bottom left already shows it.
4. **The icon of the job's target thing (`targetA`):**
   - Blueprints and frames show what they build, in its material.
   - Minified things show what's inside.
   - Pawns and corpses show nothing, because their icon is just the race.
5. **Nothing.** For example: walking to a cell, waiting, or being drafted.

**Art:** none. Mapped icons are vanilla designator textures or a thing's icon, and unmapped jobs use their target's own icon. Textures load with `reportFailure: false`, so a wrong path draws nothing instead of a pink square. The setup scenario checks every path.

**Waylith drawing quirk:** a solid box of pure black (0, 0, 0) draws nothing at any alpha, whether through `Widgets.DrawBoxSolid` or a `WhiteTex` tinted by `GUI.color`. Any other colour draws, so the backdrop is near-black (0.05, 0.05, 0.06). Found by drawing opaque red, then translucent dark red, in its place.

**Cost:** the bar draws every frame, so each pawn's icon is resolved once per job and cached by the job's `loadID`.
- **Don't key on the `Job` reference:** jobs are pooled, and drafting a miner reused the same instance for `Wait_Combat`.
- **The cache can hold stale pawns:** pawns that left are kept until it passes 256 entries, then it's cleared.
- **Repaint only:** drawing happens on `Repaint` events. The tooltip region is registered on every event.

**Other mods:** can add a `JobIconDef` for their own jobs or work types.

## Testing

**Profiles:**
- `colonistbarjobs`: Harmony, Core, RimBridgeServer and the mod.
- `colonistbarjobs-headless`: the same with no window. Textures still load headless, so the setup check works there.
- `colonistbarjobs-moodbar` and its `-headless` twin: plus CM Color Coded Mood Bar, from the Workshop (needs Steam running).
- `colonistbarjobs-multifloors` and its `-headless` twin: plus Prepatcher and MultiFloors, from the Workshop (needs Steam running), for `resolve_stairs_show_the_work_upstairs`.
- `colonistbarjobs-biotech` and its `-headless` twin: plus Biotech, for `mechs_in_control_groups` and the pollution mapping. The plain profiles have the DLCs installed but not active, so they check the mod without them.

**Finding vanilla textures:** the game's textures are packed. `ordo/list_textures` lists a folder's textures by name, and `ordo/textures_exist` checks exact paths.

**Scenarios** (`tools/BridgeTools/Scenarios/ColonistBarJobsScenarios.cs`):

| Suite | Scenarios | What it checks |
|---|---|---|
| `setup` | `setup_defs_textures_and_patch` | The defs loaded, every `texPath` has a texture, every `thingIcon` has an icon, `DrawColonist` has the mod's postfix and `HandleClicks` its prefix |
| `resolve` | `resolve_job_mapping_beats_target` | Deconstructing a wall shows the deconstruct icon, not the wall |
| | `resolve_work_type_mapping` | Mining from the Mining work giver shows the mining icon. The same job with no work giver shows the rock. |
| | `resolve_target_thing` | Hauling steel shows steel. Putting on a cloth parka shows a parka in cloth. |
| | `resolve_blueprint_shows_what_it_builds` | A wooden wall blueprint shows a wooden wall |
| | `resolve_nothing_to_show` | No job, walking to a cell and attacking a pawn show nothing |
| | `resolve_off_work` | Skygazing shows the joy icon, horseshoes the pin, wandering the idle icon, and sleeping in a bed nothing |
| `mappings` (also `resolve`) | `resolve_more_mappings` | Cleaning filth, clearing snow, smoothing a granite wall, beating out a fire and recruiting a prisoner each show their mapping; with Biotech, so does clearing pollution |
| `tooltip` | `tooltip_bill_and_progress` | A cooking job names its bill and count ("(3x)"), a job with no bill none. A half-built wall is 50% done once the pawn has started on it, and nothing before; the tooltip has the 50% and the wall's name. Mining has no progress line. |
| `click` | `click_jump_target` | Hauling jumps to the steel, walking (once started) to its cell on the map, and a job on the pawn itself, an unstarted cell job or no job nowhere |
| `mechs` | `mechs_in_control_groups` (Biotech) | The control group gizmo has the postfix, a mechanitor's lifter is in a control group and shows steel while hauling it, and the portrait grid matches the gizmo's for 1, 6 and 12 mechs |
| `settings` | `settings_activity_row` | The row is the default and hooked; with a job icon the placeholder leads and idle goes but burning stays; with none, idle stays; the slot is vanilla's size |
| | `settings_corners_size_and_offsets` | Each corner, offsets inward and outward, and offsets scaling with a doubled portrait |
| `cache` | `cache_follows_job` | A real miner shows the mining icon while mining, and nothing once drafted |
| `levels` | `resolve_stairs_show_the_work_upstairs` (also `resolve`) | With MultiFloors, a miner whose only rock is on the level above (`UpperLevel`) takes the stairs: the stair job alone wouldn't show mining, but the bar does, and still does once it mines upstairs |

**Checking by eye:** `ordo/stage_colonistbarjobs` (`ColonistBarJobsTools.cs`) clears the map and leaves four colonists working: a hauler with steel and a stockpile, an idle colonist, a miner, and a builder at wooden wall blueprints. It unpauses the game. With Biotech it also leaves a selected mechanitor (drafted) with two lifters hauling, so their control group gizmo shows.
- The bar lists colonists in spawn order. Dev mode's toolbar covers the top of the middle slots, so the hauler spawns first and the builder last.

**Results:**
- **2026-11-14, first run:** 4/7 on `colonistbarjobs-headless`. Three failures, all real:
  - `UI/Designators/HarvestPlants` doesn't exist. It's `UI/Designators/Harvest`.
  - A blueprint's material is in `stuffToUse`, not `Stuff`.
  - The cache kept the mining icon after drafting, because the pooled `Job` instance was reused. That's why it now keys on `loadID`.
- **After the fixes:** 7/7, and no errors in the log.
- **Windowed, by eye:** steel for the hauler, nothing for the idle colonist, the pickaxe for the miner, and a wooden wall for the builder.
- **2026-11-15, settings and off-work icons:** 9/9 on `colonistbarjobs-headless`. Windowed on `colonistbarjobs-moodbar`: the four icons drew over the mood bar mod's portraits, wandering colonists showed the idle icon, and there were no errors in the log.

- **2026-11-16, activity row:** 10/10 on `colonistbarjobs-headless` and on `colonistbarjobs-moodbar-headless`, with no errors in the log. Windowed, on both `colonistbarjobs` and `colonistbarjobs-moodbar`: steel, the pickaxe and the wooden wall drew first in the bottom-left row, and the idle colonist kept vanilla's clock.
- **2026-11-22, MultiFloors:** 11/11 on `colonistbarjobs-multifloors-headless`, and 10/10 on `colonistbarjobs-headless`, where the levels scenario skips. Not looked at windowed, and the scenario wasn't seen to fail against a broken build.
- **2026-11-22, tooltip, clicks, mechs and more mappings:** 13/13 on `colonistbarjobs-headless` (mechs and levels skip) and 14/14 on `colonistbarjobs-biotech-headless`, with no errors in the log. The first mechs run failed on the test's own sum (six mechs fit at 19 px, not 17). Windowed on `colonistbarjobs-biotech`: the lifters' icons sat in their portraits' bottom right corners; hovering the miner's pickaxe showed "Digging at granite (82%).", "Progress: 18%" and the click hint. The progress line contradicted the report's rock health, so mining's was dropped, and the hint now names the thing without its health. Hovering a lifter's icon showed "Wandering." under the gizmo's own tooltip. **Clicking wasn't tried:** the bridge can hover but not click at a screen point.

## Open questions

- **Backdrop:** at 75% the near-black square is visible but subtle, because the portrait background is already dark navy. Raise the default if players miss it.
- **Settings window:** opened in game on 2026-11-16. The preview matched the bar. The corner radio buttons sat at the far edge of their column, away from their labels, so they're now drawn in narrow 200 px rows.
- **Offsets:** the range went from 16 to 32 px after the first in-game try maxed out the up-or-down slider.
- **Click to jump:** not yet clicked in game. Check that it jumps, that the colonist stays unselected, and that clicking the portrait beside the icon still selects.
- **Icon choices:** the home area icon for cleaning and the firefoam popper for firefighting are stand-ins, since vanilla has no broom or fire-beating icon. Childcare (Biotech) targets the baby, so it shows nothing; it has no clear icon either.
- **Complex Jobs:** its mapped work types haven't been seen in play.
- **Mechs on the bar:** they still aren't on it; only the control group gizmo shows them, and only while their mechanitor is selected.
