# FloraTab

Shows "Flora Tab" in game, package ID `Ordo.FloraTab`. It adds a **Flora** main tab next to Wildlife that lists the harvestable wild plants on the current map, one row per kind: berry bushes, wild healroot, agave and, if you turn it on, trees.

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
| The window: a toolbar of toggles, area and distance dropdowns and a search box, sortable column headers, and the rows | `src/MainTabWindow_Flora.cs` |
| Hover highlight: hovering a row outlines its plants on the map. Yellow is marked for harvest, red at risk, green ready and white growing. If none are on screen, an arrow at the screen edge points to the nearest ready one. | `src/FloraHighlight.cs` |

**What's listed:** plants in the map's `ThingRequestGroup.HarvestablePlant` lister group, meaning plants whose def has a harvest product. Stumps aren't listed. Plants in fog aren't listed unless god mode is on, which matches the Wildlife tab.
- **Trees** are hidden by default.
- **Cultivated plants** are hidden by default. "Cultivated" uses vanilla's `Plant.DeliberatelyCultivated()`: the plant is in a growing zone set to that plant, or on a building that supports plants.
- **Only ready** hides kinds with nothing ready now.
- **Area** limits the list to plants in the home area or an allowed area.
- **Within** limits the list to plants within N cells of the base, in a straight line. The base is the middle of the home area, or of the free colonists if there's no home area, or else the middle of the map.
- Harvest marks only the plants listed, so an area or distance limit also limits what it marks.

**Columns:**

| Column | What it shows |
|---|---|
| Count, Ready | Plants listed, and how many are `HarvestableNow` |
| Next ready | How long until the soonest growing plant is ready, or "stalled" if none will grow. The tooltip adds how many are ready within about 10 days (vanilla's `HarvestableSoon`), how many aren't growing, and when the cold stops growth if that falls within the forecast. |
| Yield now | The deterministic part of `Plant.YieldNow()`: growth, health and the difficulty's crop yield factor, without the random rounding |
| At risk | Plants dying now (`Plant.Dying`, or blighted), plants reaching old age within 7 days, and plants frost kills within 7 days. Sort by it to harvest these first. |
| Nearest | Straight-line cells from the base to the nearest plant of that kind |
| Marked | Plants designated for harvest |

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
- **Click the product** to open its info card.
- **Harvest** checks every listed plant with vanilla's `CanDesignateThing`, using `Designator_PlantsHarvest`, or `Designator_PlantsHarvestWood` for trees. It then designates the plant the way `Designator_Plants.DesignateThing` does: an existing cut designation becomes a harvest designation. It skips the wood designator's per-tree messages.
- **Cancel** removes the row's harvest designations.

**Cost, and why it doesn't use TD Find Lib or Ctrl-F:**
- **No faster with a dependency:** TD Find Lib's search (`QuerySearch.Get`) also starts from `listerThings.ThingsInGroup(ThingRequestGroup.Plant)` and filters each thing. It keeps no index of its own, so depending on it would make nothing faster.
- **The lister does the work:** the game keeps the `HarvestablePlant` group up to date as plants spawn and despawn, so a survey walks only that group.
- **Only while open:** the survey runs only while the tab is open. That's when it opens, when the map changes, after a button press, and at most once a second of real time.
- **The forecast is shared:** it costs 168 samples an hour, and each plant kind's growth curve is built once per forecast.

## Testing

**Profiles:**
- `floratab`: Harmony, Core, RimBridgeServer and the mod.
- `floratab-headless`: the same with no window.

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

**Stepping time:** one `context.Step(60000)` grew a bush only about half as much as 30 steps of 2000 ticks did. That was seen twice, against two different estimates. A diagnostic stepping 2000 ticks at a time saw growth match the game's own `GrowthPerTick × 2000` to within 10%. Step in chunks when a test depends on ticking over a long time.

**Results:** 2026-11-20, `floratab-headless`, after adding risk, area and days until ready: 14/14 passed, with no errors in the log. Earlier the same day, in the windowed `floratab` profile, the first version of the tab opened next to Wildlife with correct counts, and Harvest appeared only on rows with unmarked ready plants. That was checked by eye from a screenshot.

## History

- 2026-11-20: first version.
- 2026-11-20: hovering a row outlines its plants on the map.
- 2026-11-20: At risk (dying, old age, frost), Next ready from an hourly forecast, Area and Within filters, and the Nearest column. Soon became part of Next ready's tooltip.

## Open questions

- **Not seen in game yet:** the hover highlight, the new toolbar and the new columns. The test game was shared with another session. It needs a windowed game, and hovering a row may need a person's mouse.
- **Width:** the window is about 1000 px wide. On a 1280 px screen that fits, but narrower screens would clip the right-hand buttons.
- Should rows be grouped by product, for example several berry-like plants from mods all giving berries?
- Should there be a hotkey? Wildlife uses F5, and nothing is bound here yet.
