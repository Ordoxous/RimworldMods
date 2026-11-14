# LegendaryIsUnique

`Ordo.LegendaryIsUnique`. When a colonist crafts a weapon that has an Odyssey unique variant and rolls Legendary quality, they make the unique version instead. Its quality, art and style are kept.

This README is for working on the mod. The player-facing description is in `mod/About/About.xml`. For building and deploying, load the repo's `rimworld-mod-dev` skill.

## Dependencies

- **Hard:** Harmony, Odyssey (for the unique weapons).
- **Optional:** Vanilla Skills Expanded (`vanillaexpanded.skills`). When it's active, the quality roll goes through VSE's own, so its quality bonuses count.

## How it works

Everything is in `src/LegendaryIsUnique.cs`. Harmony patches are applied in its static constructor.

- **Finding the unique variants.** At startup, every `ThingDef` whose defName ends in `_Unique` and that has `CompUniqueWeapon` is mapped to its base defName: the same name without `_Unique`. If two variants claim the same base, the first wins and a warning is logged.
- **Swapping the product.** A prefix on `GenRecipe.PostProcessProduct` rolls the quality itself, for the crafter and the recipe's work skill. If the quality is at or above the threshold, and the chance roll passes, it replaces the product with the unique thing before the original runs. The game then applies quality, art, style, minification and the craft notification to the unique thing itself.
- **Keeping one roll.** The pre-rolled quality is stored with the crafter in `_forcedQuality`. Prefixes on `QualityUtility.GenerateQualityCreatedByPawn` (and on VSE's `VSE.Stats.QualityUtility:GenerateQuality` when VSE is active) hand it back to the game's own roll. That way the item gets the quality that decided whether it became unique. A finalizer clears it even if the original throws, so an unused roll never leaks into a later craft.

`src/LegendaryIsUniqueMod.cs` holds the settings:

- **Minimum quality:** Excellent, Masterwork or Legendary (default Legendary).
- **Chance a qualifying weapon becomes unique:** 5-100% in steps of 5 (default 100%).

Style: tabs, file-scoped namespace `Ordo.LegendaryIsUnique`, and one parameter per line in method signatures.

## Testing

**Profiles** (in `~/.gabs/config.json`):

- `legendaryisunique` and `legendaryisunique-headless` load Harmony, Core, Odyssey, RimBridgeServer and this mod. Data folder `~/.gabs/rimworld/legendaryisunique`.
- `legendaryisunique-vse` and `legendaryisunique-vse-headless` add Vanilla Expanded Framework and Vanilla Skills Expanded (Workshop, so the Steam client must be running), for the VSE quality path. Data folder `~/.gabs/rimworld/legendaryisunique-vse`.
- Both saves were made fresh for their mod lists, headless, from the main menu.
- The mod has no UI to draw, so the headless profiles are enough. There's no showcase.

Run the suite with `ordo/run_scenarios` on both profiles. The scenarios are in `tools/BridgeTools/Scenarios/LegendaryIsUniqueScenarios.cs`.

| Scenario | Suites | What it checks |
|---|---|---|
| `setup_variants_and_patches` | `setup` | Every `_Unique` def with `CompUniqueWeapon` is mapped to its base weapon, and nothing else is; the prefix and finalizer on `GenRecipe.PostProcessProduct` and the prefix on the game's quality roll are in place, and with VSE, the prefix on VSE's roll |
| `craft_threshold_matches_quality` | `craft quality settings` | The core: with Excellent as the threshold and a skill-14 crafter, 200 crafts are unique exactly when the item's quality is Excellent or better, and both outcomes happen. That proves the roll that decided the swap is the roll the item got |
| `craft_default_needs_legendary` | `craft settings` | With the default threshold, a skill-20 crafter who reaches Masterwork but never Legendary (no inspiration) never makes the unique weapon |
| `craft_chance` | `craft settings` | With every roll qualifying and a 25% chance, 400 crafts give between 18% and 32% unique |
| `craft_no_variant_untouched` | `craft` | A weapon with no unique variant stays itself, with a quality, even with every roll qualifying |
| `quality_roll_cleared_after_throw` | `quality` | No roll is left waiting after a craft. A temporary Harmony patch then makes the game's quality roll throw while the pre-roll waits, ahead of the mod's prefix; the finalizer must still clear it |
| `quality_vse_pre_roll` | `quality vse` | Only with VSE (skipped, and shown as skipped, without it): over 50 crafts, the roll that decides the swap is VSE's own, so VSE's bonuses count, and the item gets that roll |

How the scenarios are built:

- **Crafting** goes through `GenRecipe.MakeRecipeProducts`, the game's own path from a finished bill to its products, rather than a pawn working a bench. The weapons are picked from the recipes at runtime: the first weapon recipe (by defName) not made from stuff, with and without a unique variant. Today those are the great bow and the recurve bow.
- **The crafter** is a colonist capable of the recipe's skill, with the skill set. A colonist incapable of a skill counts as level 0 whatever it's set to, so `Crafter` generates until one is capable.
- **The random seed is fixed** (`Rand.PushState`) for the crafting loops, so every run gives the same outcomes and the statistical checks can't flake.
- **Settings** are set per scenario and restored in a `finally`. A minimum quality of Awful, below what the settings window offers, makes every roll qualify.
- **The mod's private state** (`_uniqueThingsByBaseDefName`, `_forcedQuality`) is read by reflection.
- `quality_vse_pre_roll` uses the runner's `requiresMod`, which skips a scenario when a mod isn't active and shows it as skipped, so a VSE profile that failed to load VSE can't pass as one that did.

**Results (2026-11-14):** 6/6 passed with one skip on `legendaryisunique-headless` (twice in a row in one game, about 1 s), and 7/7 on `legendaryisunique-vse-headless`.

Every behaviour scenario was seen to fail with a fault planted by C# hot reload. The patch methods are plain static methods, so deltas apply to them even though Harmony calls them. Each batch failed exactly the scenarios it targeted:

| Fault | Profile | Failed |
|---|---|---|
| The swap ignores the threshold | plain | `craft_threshold_matches_quality`, `craft_default_needs_legendary` |
| The finalizer doesn't clear the roll | plain | `quality_roll_cleared_after_throw` |
| The swap ignores the chance | plain | `craft_chance` |
| The game's roll takes the waiting roll but rolls again anyway | plain | `craft_threshold_matches_quality` |
| Any crafted weapon becomes the great bow's unique variant | plain | `craft_no_variant_untouched` |
| The pre-roll ignores VSE and uses the vanilla roll | VSE | `quality_vse_pre_roll` only: the item still gets the deciding roll, so only this scenario sees VSE's bonuses lost |
| VSE's roll doesn't take the waiting roll | VSE | `craft_threshold_matches_quality`, `quality_vse_pre_roll` |

`setup_variants_and_patches` only checks wiring done in the static constructor, which hot reload doesn't rerun, so it wasn't faulted.

Not covered: a pawn working a real bill at a bench (the products come from the same `MakeRecipeProducts` call), and inspirations or Ideology's production specialist, which raise the quality roll before the mod sees it.

## History

- Fixed stale quality rolls (the reason for the finalizer), and added the threshold setting.
- Added the unique chance setting, and kept VSE's quality bonus.
