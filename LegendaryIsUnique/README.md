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

There are no in-game scenarios or GABS profiles yet. A suite would need a profile with Odyssey and possibly VSE. To set one up, follow `new-mod-setup.md` in the skill. Scenarios worth having:

- Crafting at or above the threshold makes the unique thing, with the rolled quality kept.
- Crafting below it makes the normal weapon.
- A weapon with no unique variant is untouched.
- The chance setting is respected over many rolls.
- The forced quality doesn't leak into the next craft, including when something throws.
- VSE's bonuses apply when VSE is loaded.

## History

- Fixed stale quality rolls (the reason for the finalizer), and added the threshold setting.
- Added the unique chance setting, and kept VSE's quality bonus.
