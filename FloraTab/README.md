# FloraTab

Shows "Flora Tab" in game, package ID `Ordo.FloraTab`. It adds a **Resources** main tab next to Wildlife with two pages, each a table with one row per kind:
- **Plants:** the harvestable wild plants on the current map, such as berry bushes, wild healroot, agave and, if you turn it on, trees.
- **Minerals:** the visible ore, such as steel, gold, plasteel, jade, uranium and compacted machinery.

The mod and the button def keep their Flora names (`Ordo.FloraTab`, `Ordo_Flora`, `MainTabWindow_Flora`) so saves and mod lists aren't affected.

## Dependencies

- **Hard:** none. It has no Harmony patches.
- **DLCs:** none needed. With Ideology, marking trees gives vanilla's warning about tree-loving ideoligions once per click.

## How it works

| Part | Where |
|---|---|
| The main button: `Ordo_Flora`, order 51, so it sits just after Wildlife (50) | `mod/Defs/MainButtons.xml` |
| The survey: one row per plant kind, with its counts, readiness, risk and distance | `src/FloraSurvey.cs` |
| The forecast: the map's next 7 days hour by hour, used for frost and days until ready | `src/MapForecast.cs` |
| Harvest and Cancel for a whole row | `src/FloraDesignations.cs` |
| The window: the Plants and Minerals tabs, and the shared toolbar with area and distance dropdowns and a search box. Each page adds its own toggles. | `src/MainTabWindow_Flora.cs` |
| The table both pages use: columns that each say how they sort and draw, the header, scrolling, hover and jump-to-next | `src/TablePage.cs` |
| The pages: their columns, toggles and row buttons | `src/PlantsPage.cs`, `src/MineralsPage.cs` |
| Minerals: the survey, Mine and Cancel, and the hover highlight | `src/MineralSurvey.cs`, `src/MineralDesignations.cs`, `src/MineralHighlight.cs` |
| Hover highlight: hovering a row outlines its plants on the map. Yellow is marked for harvest, red at risk, green ready and white growing. If none are on screen, an arrow at the screen edge points to the nearest ready one. | `src/FloraHighlight.cs` |

**What's listed:** plants in the map's `ThingRequestGroup.HarvestablePlant` lister group, meaning plants whose def has a harvest product. Stumps aren't listed. Plants in fog are never listed, even in god mode, which shows the fogged map.
- **Trees** are hidden by default.
- **Cultivated plants** are hidden by default. "Cultivated" uses vanilla's `Plant.DeliberatelyCultivated()`: the plant is in a growing zone set to that plant, or on a building that supports plants.
- **Only ready** hides kinds with nothing ready now.
- **Hide yieldless** is on by default. It hides kinds that give nothing when harvested: no `harvestedThingDef` and no secondary output. Every vanilla plant in the group has a product, so this only affects modded plants with a harvest yield but no product.
- **Area** limits the list to plants in the home area or an allowed area.
- **Within** limits the list to plants within N cells of the base, in a straight line. The base is the middle of the home area, or of the free colonists if there's no home area, or else the middle of the map.
- Harvest marks only the plants listed, so an area or distance limit also limits what it marks.

**Columns:**

| Column | What it shows |
|---|---|
| Yields | Everything harvesting gives (see below), as icons that open info cards, plus their names. Sorts by the first one's name. |
| Count, Ready | Plants listed, and how many are `HarvestableNow` |
| Next ready | How long until the soonest growing plant is ready, or "stalled" if none will grow. The tooltip adds how many are ready within about 10 days (vanilla's `HarvestableSoon`), how many aren't growing, and when the cold stops growth if that falls within the forecast. |
| Yield now | The deterministic part of `Plant.YieldNow()`: growth, health and the difficulty's crop yield factor, without the random rounding. With several yields it shows each amount, like `125 + 2`. It sorts by what all the yields are worth together (`BaseMarketValue`), so rows giving different things compare. |
| At risk | Plants dying now (`Plant.Dying`, or blighted), plants reaching old age within 7 days, and plants frost kills within 7 days. Sort by it to harvest these first. |
| Nearest | Straight-line cells from the base to the nearest plant of that kind |
| Marked | Plants designated for harvest |

**Several yields** (`src/FloraYields.cs`): vanilla plants give only `plant.harvestedThingDef`. Mods add more with Vanilla Expanded Framework's `VEF.Plants.DualCropExtension`, for example Alpha Biomes' techno tree, which also gives components. VEF's `Plant.PlantCollected` prefix gives `secondaryOutput`, or one of `randomSecondaryOutput` picked evenly when `randomOutput` is set, as `(int)(outPutAmount × Growth)`. It gives this only if the plant `CanYieldNow`, the harvester is humanlike or a mechanoid, and `Rand.Value` is under the harvester's `PlantHarvestYield`. The tab reads the extension by reflection and caches it per def, so it needs no VEF dependency. The estimate assumes a 100% harvester, and a random output counts at its share (1/n). This is untested in game, since the test profiles don't load VEF.

**Frost doesn't kill wild plants.** Plants that freeze go leafless (`Plant.CheckMakeLeafless`, below a threshold set for each plant, `LeaflessTemperatureThresh`), but only those with `dieIfLeafless` die. In vanilla those are crops, roses and daylilies, glowstool and bryolux. No vanilla wild plant has it: wild plants just stop growing in the cold, and old age is what kills them. A berry bush lives 6 grow-days × 8, so 48 days.

**The forecast** (`MapForecast`, recomputed once an in-game hour):
- **Temperature** is the world tile's for each hour (`TileTemperaturesComp.OutdoorTemperatureAt`, with its daily swing and variation). It adds the map's current offset, such as a cold snap, assumed to last the whole week.
- **Glow** is `GenCelestial.CelestialSunGlow` for the tile, in clear weather.
- **Resting** is `Plant.Resting`'s rule: no growth before 25% or after 80% of the local day.

**Days until ready** follows `Plant.TickLong`: growth per tick is `1 / (60000 × growDays) × GrowthRate` while the plant isn't resting.
- **Outdoors**, the light and temperature factors are summed hour by hour into a growth curve for each plant kind. The plant's own fertility, noxious haze and drought factors multiply it, and a binary search finds the hour it reaches `harvestMinGrowth`. Past 7 days it extrapolates at the week's average.
- **Indoors or under a roof**, the light is steady, so it uses today's `GrowthRate` for the hours that aren't resting.
- **What it leaves out:** weather dimming the sun, and artificial light outdoors. The growth scenario saw a real day come in 9% under the forecast in clear weather.

**Row actions:**
- **Click the plant name** to jump to and select the next plant of that kind. At-risk plants come first, then ready ones, then the order goes by position.
- **Click a product's icon** to open its info card. Clicking the names opens the first product's card.
- **Harvest** checks every listed plant with vanilla's `CanDesignateThing`, using `Designator_PlantsHarvest`, or `Designator_PlantsHarvestWood` for trees. It then designates the plant the way `Designator_Plants.DesignateThing` does: an existing cut designation becomes a harvest designation. It skips the wood designator's per-tree messages.
- **Cancel** removes the row's harvest designations.

**Cost, and why it doesn't use TD Find Lib or Ctrl-F:**
- **No faster with a dependency:** TD Find Lib's search (`QuerySearch.Get`) also starts from `listerThings.ThingsInGroup(ThingRequestGroup.Plant)` and filters each thing. It keeps no index of its own, so depending on it would make nothing faster.
- **The lister does the work:** the game keeps the `HarvestablePlant` group up to date as plants spawn and despawn, so a survey walks only that group.
- **Only while open:** the survey runs only while the tab is open. That's when it opens, when the map changes, after a button press, and at most once a second of real time.
- **The forecast is shared:** it costs 168 samples an hour, and each plant kind's growth curve is built once per forecast.

## Minerals page

**What's listed:** cells of every def with `building.isResourceRock` and a `mineableThing`. That's vanilla's steel, silver, gold, uranium, plasteel, jade and compacted machinery, Odyssey's obsidian, and any modded ore that sets the flag. Plain stone (granite, marble and the rest) isn't listed. Fogged cells are never listed, even in god mode, so ore inside unexplored rock shows up only once it's uncovered. Area and Within limit it just as on Plants.

**Cost:** there's no lister group for mineables, but `listerThings.ThingsOfDef` indexes by def. So a survey walks only the ore kinds' own cells, never the map's ordinary rock.

**Columns:** every column with data sorts.

| Column | What it shows |
|---|---|
| Mineral, Yields | The rock and what mining it gives. Click the rock to jump to its next vein, nearest the base first. Click the product for its info card. |
| Cells | Visible cells |
| Veins | Groups of cardinally touching cells, the way vein mining spreads |
| Yield | `Mineable.TrySpawnYield` without its randomness: at least 1 of `EffectiveMineableYield` (difficulty's mine yield factor), times `mineableDropChance`. Wasteable yields scale with `yieldPct`, which mining damage adds to as HitPoints / MaxHitPoints × the miner's MiningYield. So the estimate assumes a 100% miner, and a part-mined cell counts what it has banked plus what's left. |
| Value | Yield × the product's `BaseMarketValue`. This is the default sort. |
| Overhead | Cells under thick (overhead mountain) roof. That roof collapses if it ends up more than 6 cells from a support. |
| Nearest, Marked | As on Plants. Marked counts both Mine and Mine vein designations. |

**Row actions:**
- **Mine** checks each listed cell with vanilla's `Designator_Mine.CanDesignateThing`, then adds a Mine designation and removes any smoothing designation, as `DesignateSingleCell` does. It skips that method's ideoligion warning for each cell and gives it once per click instead. It also skips its god-mode instant mining. Cells already marked, including for vein mining, are left alone.
- **Cancel** removes both Mine and Mine vein designations from the row's cells.
- **The hover highlight** outlines marked cells in yellow, overhead cells in orange and the rest in white. If none are on screen, an arrow points to the vein nearest the base.

## Testing

**Profiles:**
- `floratab`: Harmony, Core, RimBridgeServer and the mod.
- `floratab-headless`: the same with no window.
- `floratab-profiling`: as `floratab`, plus Dubs Performance Analyzer. It's windowed, because DPA fails headless.

### Profiling

`mod/Analyzer.xml` gives the mod its own Dubs Performance Analyzer tab, under Modder > FloraTab. It names each method that does real work: the window's drawing and update, the survey, the forecast, the highlight and the row actions. The 1.7 DPA build only finds some methods when given whole `Types`, and it skips names it can't find without logging anything. So after adding or renaming methods, regenerate the list with `ordo/profiling_targets mod=FloraTab`.

**All of the mod's work happens while the tab is open,** and none of it runs in game ticks, so `ordo/run_scenarios profile=true` shows nothing for it.

**DPA can't time most of it.** Patched through `rimworld/dpa_patch_methods`, with the category set to Update or GUI, DPA recorded only `WindowUpdate`, which also runs `FloraHighlight:Draw`. It recorded nothing from the window's `OnGUI`: `DoWindowContents` and everything it calls, which includes the survey and the forecast. That was over 30 s at Superfast with the tab open. So the Modder tab is likely to show only `WindowUpdate` and the highlight.

**Instead, the `perf` suite times the work directly.** `perf_survey_and_forecast` runs the survey and the forecast 20 times each with a stopwatch, on whatever map it runs on. It logs a line starting `[FloraTab perf]`, because the runner doesn't report checks that pass. It fails if:
- a forecast rebuild averages 5 ms or more
- a survey with a new forecast averages 20 ms or more
- a survey with the hour's forecast averages 10 ms or more

To run it:
1. Load a save with plenty of plants in `floratab-profiling` or `floratab`.
2. Run `ordo/run_scenarios mod=Ordo.FloraTab suite=perf`.
3. Read the line with `rimbridge/list_logs`.

The test save is `floratab-perf` in the `floratab-profiling` data folder. It's the 250×250 Crashlanded debug colony on a desert map, with berry bushes, healroot, agave, ambrosia, oaks and poplars spawned across the map.

**Results,** 2026-11-20, windowed, on the `floratab-perf` save with 827 plants of 10 kinds, trees and cultivated plants included:

| What | When it runs | Average | Worst |
|---|---|---|---|
| Forecast rebuild | Once an in-game hour | 0.19 ms | 0.27 ms |
| Survey with a new forecast | The first survey each in-game hour | 1.2 ms | 1.5 ms |
| Survey with the hour's forecast | Once a second of real time while the tab is open | 1.0 ms | 1.4 ms |
| `WindowUpdate`, from DPA | Every frame while the tab is open | 0.00015 ms | 0.0017 ms |

That's about 1 ms once a second while the tab is open, and nothing while it's closed.

**Scenarios** (`tools/BridgeTools/Scenarios/FloraTabScenarios.cs`). Counts are compared with a survey taken before anything is placed, or by checking that a row contains the placed plants, so plants already on the map don't affect the results.

| Suite | Scenarios | What it checks |
|---|---|---|
| `setup` | `setup_main_button` | The button def loads, opens `MainTabWindow_Flora` and comes after Wildlife |
| `survey` | `survey_counts_ready_soon_and_yield` | 2 grown and 1 young berry bush: count +3, ready +2, yield +2 full harvests, and the product is berries |
| | `survey_skips_unharvestable_and_fogged` | Grass has no row. Wild healroot is listed, and hidden once fogged. |
| `filter` | `filter_trees` | An oak is hidden by default and listed with Trees on |
| | `filter_cultivated` | A potato counts as wild until a potato growing zone covers it |
| | `filter_only_ready` | A kind with nothing ready is hidden, and shown once one plant is grown |
| `filter area` | `filter_area` | Only the bush in the home area is listed, and Harvest marks only it |
| | `filter_distance_and_nearest` | An agave 1 cell from the base is the row's nearest. Within 5 cells lists it but not one 12 cells away. |
| `designate` | `designate_ready_and_cancel` | Harvest marks the grown bush and not the young one, and replaces a cut designation. A second press marks nothing. Cancel clears the designations. |
| | `designate_trees` | A grown oak is marked for wood |
| `risk` | `risk_old_age_and_dying` | A bush 2 days from its lifespan counts as old age, with old age in 2 days. A bush past its lifespan is dying. |
| | `risk_forecast_first_hour_below` | The forecast's first-hour-below search, on a made-up forecast |
| | `risk_frost_kills_only_die_if_leafless` | On a made-up forecast with -100° at hour 12, an outdoor potato is killed in 0.5 days and a berry bush isn't. With no frost, nothing is killed. |
| `growth` | `growth_days_until_ready_matches_a_day` | A ready bush is ready in 0 days. A young bush's growth over a real day, stepped 2000 ticks at a time, is within 25% of the forecast's. |
| `perf` | `perf_survey_and_forecast` | Times the survey and the forecast on the current map. See Profiling. |

**Stepping time:** one `context.Step(60000)` grew a bush only about half as much as 30 steps of 2000 ticks did. That was seen twice, against two different estimates. A diagnostic stepping 2000 ticks at a time saw growth match the game's own `GrowthPerTick × 2000` to within 10%. Step in chunks when a test depends on ticking over a long time.

**Results:** 2026-11-20, `floratab-headless`, after adding risk, area and days until ready: 14/14 passed, with no errors in the log. Earlier the same day, in the windowed `floratab` profile, the first version of the tab opened next to Wildlife with correct counts, and Harvest appeared only on rows with unmarked ready plants. That was checked by eye from a screenshot.

## History

- 2026-11-20: first version.
- 2026-11-20: hovering a row outlines its plants on the map.
- 2026-11-20: Dubs Performance Analyzer tab (`mod/Analyzer.xml`), the `floratab-profiling` profile, and the `perf` suite. A survey of 827 plants takes about 1 ms.
- 2026-11-20: At risk (dying, old age, frost), Next ready from an hourly forecast, Area and Within filters, and the Nearest column. Soon became part of Next ready's tooltip.

## Open questions

- **Not seen in game yet:** the hover highlight. Hovering a row may need a person's mouse.
- **Seen:** the toolbar and columns were seen in a windowed game during the profiling run.
- **Width:** the window is 1026 px wide. On a 1280 px screen it fits, but narrower screens would clip the right-hand buttons.
- **Truncated label:** "Within: Any distance" is cut off to "Within: Any dista..." on its 130 px button.
- Should rows be grouped by product, for example several berry-like plants from mods all giving berries?
- Should there be a hotkey? Wildlife uses F5, and nothing is bound here yet.
