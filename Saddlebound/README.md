# Saddlebound

Shows "Saddlebound" in game, package ID `Ordo.Saddlebound`. It adds Ideology memes and precepts built around riding with Giddy-Up, so riding becomes a cultural practice: how often pawns ride, fighting from the saddle, and how mounts are treated. Owning a personal mount and horse-specific bonuses are left to other mods (see [Overlap](#overlap-with-other-mods)).

This README replaces the design handoff of 2026-11-14. Version 1 is built. Version 2's plan is under [Planned](#planned).

## Dependencies

- **Hard:** Harmony, Ideology, **Giddy-Up 2 - Continued** (`MemeGoddess.GiddyUp`, Workshop `3674332861`, v2.2.5 when built, supports 1.4–1.7). It's all-in-one: Core, Ride and Roll, Battle Mounts and Caravans ship in one mod and are switched on in its settings.
- **Optional:** Vanilla Ideology Expanded - Memes and Structures (`VanillaExpanded.VMemesE`). When it's loaded, the Riding precepts list its `VME_Nomad` meme among their associated memes (`MayRequire` in the XML). Not yet tested with it loaded.
- **Build:** `GiddyUpCore.dll` is referenced directly from the Workshop folder (`Private=false`). Every call into it is in `src/GiddyUpAdapter.cs`, so an API change breaks one file. Soft dependencies, when they get code, go through reflection as Tweaks does.

## What's in version 1

| Def | What it does |
|---|---|
| **Horde Riders** meme (`Ordo_HordeRiders`, Misc) | "The saddle is our home; to walk is to be lesser." Requires Riding: Essential and Mounted Glory: Glorious. Factions with it raid mostly mounted. Has its own name rules, myth and symbol packs. |
| **Riding: Disliked** | Mounting gives "had to ride" −2 for a day. Conflicts with Horde Riders. |
| **Riding: Respected** | "Rode recently" +2 while riding and for a day after. |
| **Riding: Essential** | +3 while riding and for a day after. Nothing on day 2. −2 from day 2 and −5 from day 4 without riding, but only while there's an animal the pawn could ride. |
| **Mount Death: Mourned** | When a player animal that's been ridden dies, every colonist gets "our mount died" −4. Its usual rider also gets "my mount died" −4, so −8 in all. |
| **Mounted Glory: Glorious** | Killing an enemy while mounted gives "killed from the saddle" +3 for 3 days, stacking 3 times. |
| **Preferred mounts** (up to 3 species per ideoligion) | Mounting a preferred species gives "rode a preferred mount" +2 for a day. With Riding: Essential, mounting any other species gives "rode a lesser mount" −1. Chosen in the ideoligion editor's own "Preferred mounts" section. Generated ideoligions get 2 with Horde Riders, 1 with Riding: Respected or Essential, and none otherwise. |
| **Horde raids** (setting, on by default) | Horde Riders factions try to mount 90% of their raiders, in place of Giddy-Up's enemy mount chance. |

Icons are vanilla placeholders: the rancher meme icon, and the animal connection, bonding and raiding issue icons.

## How it works

**Tracking riding** (`RidingTracker`, a saved `GameComponent`): postfixes on Giddy-Up's `MountUtility.Mount` (private, found with `AccessTools`) and `MountUtility.Dismount` record each rider's last riding tick, on mounting and on dismounting. It's keyed by thing ID and stores ticks, not live state.
- **Riding now** means `IsMounted()`, or in a world caravan with the mount Giddy-Up paired them with (`ReservedMount`) in the same caravan. A pawn riding for days without dismounting counts as riding the whole time.
- **A pawn the tracker hasn't seen** starts its count when first asked, so new colonists and old saves don't start at −5.
- **Per mount,** it counts rides by each rider. The usual rider is the living player pawn who rode it most, ties going to the last rider.
- **Why not the `ExtendedPawnData.Mount` setter:** it's a one-line `set => _mount = value;`, which the JIT may inline into its callers, so a postfix may never run. Every mount goes through `Mount` (from `GoMount` with `Instant`, which the mount job calls when it ends), apart from the WTH mechanoid raid patch, which sets the field for raider mechs and doesn't matter here.

**Thoughts:**
- **Riding:** situational thoughts with C# workers, `ThoughtWorker_Precept_RodeRecently` and `ThoughtWorker_Precept_RidingEssential`. "Had to ride" comes from the `Ordo_Mounted` history event, fired in the `Mount` postfix with the rider as doer, only for the player's pawns.
- **Mount death and glory:** history events with vanilla comps, all XML. `Ordo_MountDied` has no doer, so every free colonist hears of it (`PreceptComp_KnowsMemoryThought`). `Ordo_OwnMountDied` and `Ordo_KilledFromSaddle` have the rider or killer as doer (`PreceptComp_SelfTookMemoryThought`). A prefix on `Pawn.Kill` decides what to fire before the death (the victim's faction, the killer still mounted), and the postfix fires it once the pawn is dead.
- **Essential's penalties** check `HasRideableMount`: an animal on the pawn's map, or in their caravan, that Giddy-Up would let them ride. That means its mod options, faction, tameness and "who may ride" setting, and the rider being old enough. The answer is cached per pawn for an hour of game time.
- **The Riding precepts** have `enabledForNPCFactions` off. NPC pawns aren't tracked off the map, and their mood doesn't matter.

**Preferred mounts** (`src/PreferredMounts.cs`): modelled on vanilla's venerated animals, with one precept per species (`Ordo_PreferredMount`) in an issue that allows several.
- **Its own class:** `Precept_PreferredMount` extends `Precept_ThingDef`, not `Precept_Animal`, because vanilla treats every `Precept_Animal` as a venerated animal (`Ideo.IsVeneratedAnimal`, `PreceptWorker_Animal.CanUse`).
- **Editor:** `IdeoUIUtility.DoPrecepts` draws its sections by exact precept class, so a postfix adds a "Preferred mounts" section by calling vanilla's own `DoPreceptsInt` (private, reached through the publicizer). That gives the cards, the "Add mount..." button and the species menu for free.
- **Choices:** `PreceptWorker_PreferredMount` offers every race in Giddy-Up's `MountableCache`, so only species Giddy-Up lets anyone ride, and not one already chosen.
- **Weights for generation:** body size (clamped 0.5–3), ×10 for a faction def's Giddy-Up `FactionRestrictions` animals, ×4 for the player's starting or current biome, and ×3 for farm and fighting animals (Giddy-Up's own test for domestic mounts). NPC factions are generated before they have settlements, so they get no biome.
- **Generation:** the precept has no selection weight and doesn't count towards the precept limit, so vanilla never picks it. A postfix on `IdeoFoundation.RandomizePrecepts` adds the right number, as vanilla adds venerated animals in a step of their own. The editor's randomize button goes through the same method.
- **Thoughts:** the `Mount` postfix fires `Ordo_RodePreferredMount` or `Ordo_RodeLesserMount` (the latter only with Riding: Essential), and `PreceptComp_SelfTookMemoryThought` turns them into memories. With several preferred-mount precepts each comp fires, so the thoughts have a stack limit of 1 and the second renews the first.

**Horde raids:** Giddy-Up reads `enemyMountChance` (15, or `enemyMountChancePreInd` 33 below Industrial) in a local function inside `MountUtility.GenerateMounts`. Its `mountChance` setting (40) is only read by a settings slider. A prefix sets both enemy chances to 90 when the raiding faction's primary ideoligion has Horde Riders, and a finalizer puts them back, even if Giddy-Up throws. A faction's `FactionRestrictions` or a pawn kind's `CustomMounts` override still wins. Small raids get fewer mounts than 90%, because Giddy-Up only picks animals with `combatPower * 2 < points`. Battle Mounts must be on, because its raid patch is what calls `GenerateMounts`.

## Giddy-Up API

As read from the v2.2.5 source shipped in the Workshop folder (`Source/`), 2026-11-15.

| Need | Hook |
|---|---|
| Is this pawn mounted? | `StorageUtility.IsMounted(this Thing)`, which reads the static `ExtendedDataStorage.isMounted` set |
| Mount and dismount | Private `MountUtility.Mount(this Pawn rider, Pawn animal)`, public `Dismount(rider, animal, pawnData, ...)`. `InvoluntaryDismount` and `GoDismount` end in `Dismount`. |
| Rider data | `pawn.GetExtendedPawnData()`: `Mount`, `Rider`, `ReservedMount` and `ReservedBy` are saved. `selectedForCaravan` is a caravan-screen flag: it isn't saved and is never cleared, so it's no use for "riding in a caravan". |
| Can this rider use that animal? | `IsMountable(animal, out reason, rider, checkState, checkFaction, checkTraining)`, `IsAllowed(rider, animal)`, `IsCapableOfRiding(rider, out reason)` |
| Mountable races | `ModSettings_GiddyUp.MountableCache` is keyed by the race's `ThingDef.shortHash` (`animal.def.shortHash` in `IsMountable`). It's built in `Setup.cs` from body size over 1.2, the `Mountable` and `NotMountable` extensions and the player's inverted rules, so races switched off in the settings aren't in it. `GenerateMounts` checks it with a `PawnKindDef`'s `shortHash`, so the key type isn't consistent there. |
| Faction mounts | `FactionRestrictions` (a `FactionDef` extension with `allowedNonWildAnimals` and `allowedWildAnimals`) is `internal`, so the adapter reads it by reflection |
| Raid mounts | `MountUtility.GenerateMounts(ref List<Pawn>, IncidentParms)`, called from the raid, ambush, caravan-incident and debug-spawn patches. Skips non-humanlike pawns and slaves. |
| Module switches | `ModSettings_GiddyUp.battleMountsEnabled`, `caravansEnabled` |

## Overlap with other mods

- **Mount Ownership** (`font.mountownership`, lists 1.6 only) has personal-mount precepts (−1 to −5 without a mount, +3 to +6 with one), riding thoughts (+1, or +4 for a named mount) and a riding joy job. This mod doesn't repeat ownership or naming.
- **Alpha Memes' `AM_Horses`** has a horses-only precept with a +25% caravan riding speed. Not repeated here.
- **VME's `VME_Nomad`** meme is travel flavour with no riding logic. The Riding precepts list it as an associated meme.

## Testing

**Profiles:**
- `saddlebound`: Harmony, Core, Ideology, RimBridgeServer, Giddy-Up 2 - Continued (Workshop, needs Steam running) and the mod.
- `saddlebound-headless`: the same with no window.

The test save was made on 2026-11-15 with these mods loaded, so the player has an ideoligion.

**Scenarios** (`tools/BridgeTools/Scenarios/SaddleboundScenarios.cs`): riding goes through Giddy-Up's real `Mount` and `Dismount`, so the patches run for real. Time off a mount is faked by backdating the tracker, not by stepping days. Each scenario swaps the colonists' ideoligion's precept for its issue and puts the old one back.

| Suite | Scenarios | What it checks |
|---|---|---|
| `setup` | `setup_no_load_errors` | No errors mention the mod, the meme loaded, and the player has an ideoligion |
| | `setup_giddyup_api` | Every Giddy-Up member the adapter uses resolves, and `Mount`, `Dismount`, `GenerateMounts` and `Pawn.Kill` carry the mod's patches |
| `riding` | `riding_tracks_mount_and_dismount` | Mounting and dismounting update the tick, and the horse's record names the rider |
| | `riding_respected_thought` | "Rode recently" while riding and for a day after, gone after that, and no Essential thought |
| | `riding_essential_stages` | Just rode, a day and a bit, 3 days and 5 days give stages 0, none, 1 and 2 |
| | `riding_essential_long_ride` | A pawn mounted 5 days ago and still riding is at stage 0 |
| | `riding_essential_suppressed_no_mounts` | No penalty with no animal, or with only a wild horse. The penalty comes back with a trained horse. |
| | `riding_disliked_memory` | Mounting gives "had to ride", and no "rode recently" |
| `mounts` | `mount_death_memory` | A ridden horse dies: both colonists get "our mount died", only the rider gets "my mount died", and the record is dropped |
| | `mount_death_unridden_ignored` | A horse no one rode isn't mourned |
| `glory` | `mounted_kill_glory` | A mounted kill gives the memory. A kill on foot, or killing a colonist from the saddle, doesn't. |
| `preferred` | `preferred_mount_choices` (also `setup`) | The editor's species list has the horse and dromedary, not the chicken, and only mountable races. A chosen species can't be picked twice. A preferred mount isn't a venerated animal. |
| | `preferred_mount_memory` | With Riding: Essential and the horse preferred, the horse gives "rode a preferred mount" and the dromedary "rode a lesser mount" |
| | `preferred_mount_lesser_needs_essential` | With Riding: Respected, the dromedary gives neither |
| | `preferred_mount_none_no_memory` | An ideoligion with no preferred mounts gives neither |
| | `preferred_mount_generation` | Five Horde Riders ideoligions (fixed seed) get two distinct mountable species and Riding: Essential. One without riding precepts gets none, and any other gets what its riding precepts call for. |
| `raids` | `horde_raid_mount_chance` | A test prefix running after the mod's reads the chance Giddy-Up would use and skips generation: Giddy-Up's chance without the meme, 90 with it, the settings put back afterwards, and Giddy-Up's chance again with the setting off |

**Results:**
- **2026-11-15, first run:** 12/12 on `saddlebound-headless`, and nothing in the launch log but an About.xml warning (Harmony's Workshop link, since added).
- **Fault planting:** with three faults hot reloaded (riding now ignores `IsMounted`, mounted kills skip the mounted check, and "my mount died" never fires), exactly `riding_essential_long_ride`, `mounted_kill_glory` and `mount_death_memory` failed. After reverting, 12/12 again.
- **2026-11-15, preferred mounts:** 17/17 on `saddlebound-headless`. With three more faults planted (preferred never matches, generation adds none, and the lesser memory ignores Essential), exactly `preferred_mount_memory`, `preferred_mount_lesser_needs_essential` and `preferred_mount_generation` failed. After reverting, 17/17 again.
- **Windowed, 2026-11-15:** `ordo/stage_saddlebound` (`SaddleboundTools.cs`) gives the player's ideoligion Riding: Essential and the horse and dromedary. The Ideoligions tab drew Riding: Essential among the precepts and a "Preferred mounts" section with both animals' icons, and in dev edit mode an "Add mount..." button, with no errors in the log. The bridge's `click_ui_target` didn't open that button's menu, nor vanilla's "Add animal...", so the species menu itself wasn't seen.
- **Not yet checked in game:** the settings window, the species menu and creating an ideoligion at game start, a real mounted raid, caravans, save and reload, and VME loaded.

## Planned

**Tests still to write** (from the handoff):
- `caravan_counts_as_riding`: an Essential pawn in a world caravan with their paired mount gets no penalty. Once back and on foot, the penalty returns on schedule.
- `save_load_tracker`: the ticks and per-mount records survive a save and reload.
- `horde_raid_mounted`: over many large generated raids, Horde Riders are mounted far more often than a faction without the meme.
- A showcase clip of a mounted Horde Riders raid. It needs a caption source other than an `AbilityDef`.

**Preferred mounts, possible next steps:**
- Horde Riders raids could bring their ideoligion's preferred species, by weighting Giddy-Up's animal pick in `GenerateMounts`.
- An issue icon. It has none, like vanilla's venerated animals.

**Version 1 leftovers:**
- **Mounted Glory's on-foot penalty:** "fought on foot with a mount available" −1, while drafted and unmounted with a free mount. A situational worker using `HasRideableMount`.
- **VME exclusion:** exclude Horde Riders with VME's city-builder meme when VME is loaded.
- **Mount Ownership stacking:** next to its +1/+4 riding thoughts, Essential's +3 may stack too high. Lower it, or add a profile with Mount Ownership to judge it.
- **Icons:** the meme and the three issues use vanilla placeholders.

**Version 2:**
- **Saddle Lord role** (specialist, Animals 6+): +15% move speed while mounted (a `StatPart`), +10% mounted ranged accuracy (a postfix after Giddy-Up's `AdjustedAccuracy`; check they stack sensibly), and a Rallying Charge ability (vanilla `CompAbilityEffect_GiveHediff`, filtered to mounted allies). Inactive while unmounted.
- **Eating Mounts** (Abhorrent / Horrible / Disapproved / Acceptable, −8/−5/−2): events from slaughtering a mountable animal and eating its meat. Meat has no pawn: map `ingestible.sourceDef` to the race and check `MountableCache` by the race's `shortHash`. Check whether the cache covers races switched off in Giddy-Up's settings.
- **Steed Kinship** meme: requires Mount Death: Mourned and Eating Mounts: Abhorrent.
- **Mount Training: Revered:** "bonded with a steed" +2 on taming or training a mountable animal, and +10% tame and train chance.
- **Stabling: Desired** (low priority): +1 for mounts kept in a roofed pen or barn, −1 for mounts sleeping outside.
- **Left out:** a Naming of the Steed ritual (about 40% of the effort, and it overlaps Mount Ownership's naming), and styles.

## Open questions

- **The name:** Saddlebound, or something else (the handoff's alternative was RidingCreeds)?
- **Horde raids** is its own setting, on by default. Is that the right default?
- **Lesser mounts:** the −1 applies only with Riding: Essential. Should Respected cultures feel it too, or should the preferred bonus scale with the Riding precept?
- **Scope:** keep Steed Kinship and the Eating Mounts ladder for version 2, or stay a riding-only mod?
- **Balance:** the Essential ladder (−5 after 4 days) is in line with vanilla's Essential precepts. Check it in play.
