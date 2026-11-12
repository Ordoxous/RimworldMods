# Profiling with Dubs Performance Analyzer

## Timing a scenario run

1. Use a GABS profile that loads Dubs Performance Analyzer (DPA) on top of the mod's normal list, with its own data folder so the normal test profiles never load DPA. For SillyAnimalThings, that's `sillyanimalthings-profiling`.
2. Call `ordo/run_scenarios` with `profile: true` (and `suite`, `filter` or `mod` as usual).

The runner patches every method `ordo/profiling_targets` lists for each mod under test into DPA, through RimBridgeServer's `rimworld/dpa_*` tools. It resets DPA before each scenario and snapshots it after, because DPA only keeps the last 2000 ticks and a full run can be longer. The result's `profile` section has per-method calls, total, average and worst time (with the scenario it happened in), and per-scenario totals.

- **It needs a window.** Headless, DPA logs "Caught analyzer trying to begin a new update cycle before finishing the previous one", which fails every scenario. Expect about 5x the headless run time.
- Only time inside game ticks counts, so a scenario's own setup, such as dealing damage directly, doesn't show.
- Times include whatever the method calls, and the first call of something is much slower than later ones (JIT and caches). One ability's landing took 94 ms the first time and 2-13 ms after. Judge by average and by later calls.
- What matters is per-tick cost. Look for anything called every tick with a non-trivial average (above about 0.01 ms), then for one-off spikes above about 15 ms.

## A DPA tab for players: `mod/Analyzer.xml`

DPA reads `Analyzer.xml` from a mod's root folder and adds a tab under Modder > <Mod>. Without DPA it does nothing. See `SillyAnimalThings/mod/Analyzer.xml` for the format.

- List each method by name (`Type:Method`). The 1.7 build of DPA only finds methods in a few of a mod's types when given `Types`, and doesn't read `NestedTypes`.
- DPA skips names it can't find without logging anything, so regenerate the list with `ordo/profiling_targets` after adding or renaming methods.
