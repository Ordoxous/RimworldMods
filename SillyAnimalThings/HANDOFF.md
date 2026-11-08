# SillyAnimalThings handoff

Last updated 2026-11-08.

## What the mod is

Gives animals silly abilities. It uses the same pattern as Odyssey's war trumpet:

- `AnimalTagDef`s give abilities to races: at startup each tag's abilities go into `PawnKindDef.abilities` for every kind of each member race.
- Each ability is gated by a special `TrainableDef` through `CompProperties_AbilityRequiresTrainable`. The tag adds that trainable to `race.specialTrainables`, read from the comp. Don't use `TrainableDef.enablesAbility`: it's `[Obsolete]` and nothing in the game reads it.
- Wild, manhunting and enemy animals cast the ability themselves (`aiCanUse` + `aiCanCastWithoutTrainable`).
- Tamed animals only get the gizmo once trained, and from then on only cast it when commanded. Regenerate and Hidden Dragon are the exceptions: they also auto-cast for trained player animals (see `SelfBuffAutoCast`).

Dependencies:

- Hard: Harmony, Odyssey.
- Optional: Biotech, needed for Fire Burst (the whole Incendiary tag) and the toxalope. These are guarded with `MayRequire="Ludeon.RimWorld.Biotech"`.

All defNames use the `Ordo_` prefix.

## Current state

- Step 1 of the plan is done: the tag system, the CSV conversion and the expanded memberships. It builds with no warnings (`dotnet build SillyAnimalThings/SillyAnimalThings.csproj`).
- **Nothing has been tested in game yet.** Checks so far are static: the build, XML parsing, every race and `Ordo_` reference resolving, every class named in XML existing, every texture path found in `Assets.zip` (Fire Burst's icon is Biotech's own), and `Animals.csv` matching `AnimalTags_SillyAnimals.xml` for the built tags.
- The first batch and the BioFuelFertilizer removal are pushed to `base/master`. The step 1 work is committed locally (`main`), but not pushed.

### Bugs fixed while doing step 1

These were in the committed first batch, found by reading game code:

- **Hidden Dragon ended itself.** `Verb.TryCastNextBurstShot` calls `health.Notify_UsedVerb` after the cast, and verbs default to `violent = true`, so `HediffComp_RemoveOnAttack` removed the invisibility as soon as it was granted. The verb is now `violent: false`. Keep this in mind for any self-buff with a remove-on-attack comp.
- **Regenerate and Hidden Dragon needed Biotech.** They used `CastAbilityOnThingWithoutWeapon`, which is a Biotech job. They now use the default cast job.

### Built abilities

| Ability | Currently on | How it works | Key files |
|---|---|---|---|
| Trunk Blast | Trunked | `CompProperties_AbilitySprayLiquid`, 7 cells. The projectile puts out fires and knocks back and staggers pawns (but not pawns bigger than the caster). | `Projectile_WaterBlast.cs`, `KnockbackUtility.cs` |
| Trunk Throw | Trunked | Destroys a stone or slag chunk within 2.9 cells and launches a projectile drawn as that chunk. The chunk is re-placed where it lands. The gizmo is disabled when no chunk is nearby. | `CompAbilityEffect_TrunkThrow.cs`, `Projectile_ThrownChunk.cs` |
| Detonate | Volatile | Touch ability (walks to the target), then `pawn.Kill(null)`, which fires the race's death action (big explosion, small explosion, or tox cloud). Has a confirmation dialog. AI charges hostiles within 15 cells. | `CompAbilityEffect_Detonate.cs` |
| Gnaw | Woodchewer | Touch ability on a wooden building (Woody stuff, or WoodLog in its cost list). Deals 80 Bite damage and restores 25% food. | `CompAbilityEffect_Gnaw.cs` |
| Hungry gnawing (behaviour) | Woodchewer | Harmony prefix on `JobGiver_GetFood.TryGiveJob`. A hungry, non-player animal with Gnaw ready targets a reachable wooden building within 12 cells. | `Behaviours/HungryGnawing.cs` |
| Fire Burst | Incendiary (Biotech only) | Vanilla fire burst, radius 2.9. A comp subclass makes the AI cast it when a hostile is within the radius; vanilla only casts while the caster is being aimed at. | `CompAbilityEffect_FireBurstNearEnemy.cs` |
| Belly Slide | Slider | `Verb_CastAbilityDash` (a subclass of the jump verb) with a zero-height `PawnFlyer`, so it slides along the ground. Player-only. | `Verb_CastAbilityDash.cs`, `Defs/Flyers_SillyAnimals.xml` |
| Charge | Charger | Same dash, using the `PawnFlyer_Charge` flyer. On landing it deals 10 + 7.5 x body size blunt damage (walrus 25, elephant 40) to hostiles at or next to the landing cell, then knocks them back and staggers them. AI uses it on targets more than 3.9 cells away. | `PawnFlyer_Charge.cs`, `CompAbilityEffect_ChargeTarget.cs` |
| Regenerate | Tenacious | Self-cast that applies the `Ordo_Regenerating` hediff: heals non-permanent injuries by 1 HP per second for 30 seconds. 12-hour cooldown. Auto-cast by a postfix on `Pawn.PostApplyDamage` when health drops below 60%. | `HediffComp_Regenerate.cs`, `SelfBuffAutoCast.cs` |
| Hidden Dragon | Big Cat | Self-cast that applies `Ordo_HiddenDragon`: `HediffComp_Invisibility` (the same mechanism as Royalty's psychic invisibility; the comp is in Core) plus `HediffComp_RemoveOnAttack`. Lasts 20 seconds. Auto-cast by a prefix on `JobGiver_AIFightEnemy.GetAbilityJob` when the enemy is more than 6.9 cells away. | `HediffComp_RemoveOnAttack.cs`, `SelfBuffAutoCast.cs` |

Other files:

- `SillyAnimalThings.cs` is the Harmony bootstrap. It also has a `Pawn.SpawnSetup` postfix that grants missing `Ordo_` kind abilities to animals that existed before the mod was added, because vanilla only grants kind abilities at generation.
- `AnimalTagDef.cs` holds the def and `AnimalTagUtility.ApplyAllTags`, called first in the bootstrap. Members are listed in `Defs/AnimalTags_SillyAnimals.xml` by race defName, which doesn't always match the CSV name (Scimitar Cat is `Cat_Scimitar`). Add a tag there once its ability exists.
- All icons are borrowed vanilla textures and need real art.

## Design

**Tags, not groups.** Each tag grants a set of abilities, and an animal can have several tags. Other mods can patch their races into a tag's `races` list or add their own `AnimalTagDef`.

**CSVs** are the design record, and the mod doesn't read them:

- `Animals.csv`: Animal, Tags (semicolon-separated). All 107 animals; 25 are untagged.
- `Tags.csv`: Tag, Abilities, Description. All 26 tags, including ones with no ability built yet.
- Keep the UTF-8 BOM; the user edits these files in Excel.

### Agreed tags and members

Built and live in `AnimalTags_SillyAnimals.xml`:

| Tag | Animals | Abilities |
|---|---|---|
| Trunked | Elephant, Mastodon | Trunk Blast, Trunk Throw |
| Woodchewer | Alphabeaver | Gnaw (deliberately wood-specific and alphabeaver-only) |
| Volatile | Boomalope, Boomrat, Toxalope | Detonate. Toxalope's death action is `DeathActionWorker_ToxCloud`, so it releases a tox cloud. |
| Incendiary | Boomrat, Lava Snail | Fire Burst |
| Big Cat | Tiger, Cougar, Panther, Scimitar Cat | Hidden Dragon (big cats only) |
| Charger | Walrus, Rhinoceros, Hippo, Bison, Muffalo, Wild Boar, Ibex, Goat, Moose, Elk, Mastodon, Elephant | Charge |
| Slider | Penguin, Otter, Seal, Sea Lion | Belly Slide |
| Tenacious | Wolverine, Badger | Regenerate |

Designed but not built:

| Tag | Animals | Ability |
|---|---|---|
| Venomous | Rat, Waste Rat, Cobra, Megaspider, Monitor Lizard | Poison Bite (the user specifically wants rats to have a poison bite) |
| Prolific | Hare, Snowhare, Guinea Pig | Duplicate: targets another hare, puts both on cooldown, creates a new hare |
| Musky | Muskox, Mink | Spray Musk |
| Small Cat | Cat, Lynx | Nine Lives or Pounce (**undecided**) |
| Alligator | Alligator | Death Roll, or untagged (**undecided**) |
| Shelled | Tortoise, Sea Turtle, Hermit Crab, Stone Crab, Lava Snail, Armadillo, Megascarab | Withdraw |
| Leaper | Hare, Snowhare, Bullfrog, Colossus Toad, Gazelle, Monkey | Leap |
| Kicker | Horse, Donkey, Ostrich, Emu, Cassowary | Kick |
| Spitter | Dromedary, Alpaca | Spit |
| Quilled | Porcupine | Quill Volley |
| Pack Howler | Timber Wolf, Arctic Wolf, Warg, Greatwolf, Bog Hound, Husky | Howl |
| Intimidating | Goose, Gorilla, Grizzly Bear, Cobra | Intimidate |
| Burrower | Prairie Dog, Badger, Armadillo, Megavole | Burrow |
| Scavenger | Vulture, Crow, Raccoon, Waste Rat | Steal / Scavenge |
| Mimic | Macaw, Crow | Mimic Taunt |
| Flier | Bluebird, Sparrow, Crow, Macaw, Vulture, Duck, Goose, Swan | Swoop |
| Loyal | Labrador Retriever, Husky, Yorkshire Terrier | Fetch |
| Hive | Hive Queen, Larva, Spelopede, Megaspider, Megascarab | Call the Swarm |

Untagged for now (fine as plain animals): most livestock, Deer, Caribou, Chinchilla, Squirrel, Capybara, Iguana, the three foxes (a "Trickster" tag was floated), Panda, Polar Bear, Heron, Flamingo, Megasloth, and both thrumbos (they already have Odyssey roars).

### Effort / worth ratings for the unbuilt abilities

| Ability | Effort | Worth | Implementation notes |
|---|---|---|---|
| Howl | Low | High | Like `AnimalRoarBase` / WarTrumpet, but with an allies-only filter comp and a buff hediff. |
| Poison Bite | Low | High | Touch ability plus `CompProperties_AbilityGiveHediff` adding ToxicBuildup, and some damage. |
| Intimidate | Very low | Medium | Mostly an XML copy of TerrorRoar or WarTrumpet. |
| Withdraw | Low | Medium | Self-buff hediff: armor stat offsets, Moving capacity capped at 0. Auto-cast when hurt through `SelfBuffAutoCast`. |
| Kick | Low | Medium | Touch ability: damage plus `KnockbackUtility`. |
| Spit | Low | Medium | Projectile that applies a short blind or nausea hediff. |
| Spray Musk | Low | Medium | Effect still undecided: make enemies flee, a debuff, or a stink gas cloud (`GasType.RotStink` is the easiest). |
| Leap | Very low | Low–Med | Vanilla `Verb_CastAbilityJump` with the default `PawnFlyer`. |
| Quill Volley | Low | Low–Med | Spike projectile through `LaunchProjectile` or `SprayLiquid`. |
| Nine Lives | Medium | Medium | Harmony intercept on death plus a lives counter. It's a passive. |
| Duplicate | Medium | Med–High | Pawn spawning, partner validation, paired cooldowns. Needs balancing so it can't be abused as free breeding. |
| Death Roll | Low–Med | Low | Touch ability, stun plus damage. One animal only. |
| Swoop | Low–Med | Low | `PawnFlyer_Charge` with height added and less damage. |
| Call the Swarm | Medium | Low–Med | |
| Mimic Taunt | Medium | Low–Med | Forcing an enemy to retarget means overriding its AI. |
| Steal / Scavenge | Med–High | Medium | Needs a new job driver. |
| Burrow | High | Medium | Cheaper fake: invisible jump plus dust effects. |
| Fetch | High | High (for players) | Animals can't carry pawns in vanilla, so it needs a custom rescue job. |

### Suggested next steps

1. ~~Implement the tag def, convert the CSVs, and expand the built abilities to their full tag memberships.~~ Done.
2. Test in game (see below), ideally before adding more abilities.
3. Build the cheap, high-value batch: Howl, Poison Bite, Intimidate, Withdraw, Kick, Spit.
4. Spray Musk (once its effect is decided) and Leap.
5. Bigger items by user preference. Fetch and Duplicate looked most popular.

### Open questions for the user

- Small cats: Nine Lives or Pounce?
- Alligator: Death Roll, or untagged?
- Spray Musk: what effect?
- Duplicate: does the new hare spawn as a baby or an adult? Male/female only? Does a tamed pair produce a tamed hare?
- Should the alphabeaver behaviour also include manhunters chewing through walls and doors (a pathing hook)? Not built.

## Testing risks to check in game

- Detonate: killing the boomalope mid-cast may log errors.
- Thrown chunk projectile: it's drawn with the chunk's `graphic.MatSingle`; check it renders.
- Dash and charge flyers: zero `heightFactor`, landing on a pawn's cell, and the knockback afterwards.
- How often hostile elephants and walruses use their abilities, and whether hungry wild alphabeavers are too eager to eat buildings.
- Hidden Dragon: confirm invisibility survives the cast and ends when the cat attacks.
- Boomrat has both Detonate and Fire Burst. The AI picks the first castable ability, and Detonate triggers from 15 cells, so hostile boomrats may never fire burst. Fire burst also isn't protected from its own flames, so a boomrat may ignite itself (funny, but check).
- Charge damage across body sizes (goat about 16, mastodon about 44).
- Tag startup: the training tab lists the new trainables on `trainability: None` animals (code says it should), and existing saves pick up the abilities.

## Environment notes and gotchas

- **`F:` drive:** it was emptied partway through the session. RimWorld is now at `D:\SteamLibrary\steamapps\common\RimWorld` (game data in `Data\`, version 1.7.5000).
  - `Directory.Build.props` and `Directory.Build.targets` still point at `F:\...` for `RimWorldDirectory` and the CopyToRimworld target. Builds fall back to the `Krafs.Rimworld.Ref` NuGet package ("Local RimWorld referenced: false").
  - The user hasn't decided yet whether to repoint them to `D:`. In-game testing needs the copy target or a manual copy of `out/` to `D:\SteamLibrary\steamapps\common\RimWorld\Mods\SillyAnimalThings`.
- **Publicizer:** `Krafs.Publicizer` makes protected game members public. Overrides of protected methods (for example `Projectile.Impact`, `PawnFlyer.RespawnPawn`) must therefore be declared `public override`, and private fields like `PawnFlyer.destCell` are accessible.
- **Decompiled source for API lookups:**
  - Install ilspycmd locally: `dotnet tool install ilspycmd --tool-path <dir>`.
  - Then run `<dir>\ilspycmd.cmd -p -o <out> "D:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll"`. Note that `Assembly-CSharp.dll` is in the install root.
  - Scratchpad copies don't survive between sessions, so expect to regenerate this.
- **Style:** C# uses tabs, file-scoped namespace `Ordo.SillyAnimalThings`, and one parameter per line in method signatures (matching LegendaryIsUnique). XML uses 2 spaces.
