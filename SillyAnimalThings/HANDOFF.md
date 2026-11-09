# SillyAnimalThings handoff

Last updated 2026-11-08 (second batch of abilities added).

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

- The tag system, CSV conversion, expanded memberships and the second batch of abilities (Howl, Poison Bite, Intimidate, Withdraw, Kick, Spit) are written. It builds with no warnings (`dotnet build SillyAnimalThings/SillyAnimalThings.csproj`).
- **Tested in game on 2026-11-09**, and rerunnable with the scenario suite. See "In-game test results" below. Static checks also pass: the build, XML parsing, every race and `Ordo_` reference resolving, every class named in XML existing, every texture path found in `Assets.zip` (Fire Burst's icon is Biotech's own), and `Animals.csv` matching `AnimalTags_SillyAnimals.xml` for the built tags.
- The tag system is pushed to `base/master`. The second batch of abilities is committed locally on `main`, but not pushed.

### Bugs fixed while doing step 1

These were in the committed first batch, found by reading game code:

- **Hidden Dragon ended itself.** `Verb.TryCastNextBurstShot` calls `health.Notify_UsedVerb` after the cast, and verbs default to `violent = true`, so `HediffComp_RemoveOnAttack` removed the invisibility as soon as it was granted. The verb is now `violent: false`. Keep this in mind for any self-buff with a remove-on-attack comp.
- **Regenerate and Hidden Dragon needed Biotech.** They used `CastAbilityOnThingWithoutWeapon`, which is a Biotech job. They now use the default cast job.

### Built abilities

| Ability | Currently on | How it works | Key files |
|---|---|---|---|
| Trunk Blast | Trunked | `CompProperties_AbilitySprayLiquid`, 7 cells. The projectile puts out fires and knocks back and staggers pawns (but not pawns bigger than the caster). | `Projectile_WaterBlast.cs`, `KnockbackUtility.cs` |
| Trunk Throw | Trunked | Destroys a stone or slag chunk within 2.9 cells and launches a projectile drawn as that chunk. The chunk is re-placed where it lands. The gizmo is disabled when no chunk is nearby. | `CompAbilityEffect_TrunkThrow.cs`, `Projectile_ThrownChunk.cs` |
| Detonate | Volatile | Touch ability (walks to the target), then `pawn.Kill(null)`, which fires the race's death action (big explosion, small explosion, or tox cloud). AI charges hostiles within 15 cells. | `CompAbilityEffect_Detonate.cs` |
| Gnaw | Woodchewer | Touch ability on a wooden building (Woody stuff, or WoodLog in its cost list). Deals 80 Bite damage and restores 25% food. | `CompAbilityEffect_Gnaw.cs` |
| Hungry gnawing (behaviour) | Woodchewer | Harmony prefix on `JobGiver_GetFood.TryGiveJob`. A hungry, non-player animal with Gnaw ready targets a reachable wooden building within 12 cells. | `Behaviours/HungryGnawing.cs` |
| Fire Burst | Incendiary (Biotech only) | Vanilla fire burst, radius 2.9. A comp subclass makes the AI cast it when a hostile is within the radius; vanilla only casts while the caster is being aimed at. | `CompAbilityEffect_FireBurstNearEnemy.cs` |
| Belly Slide | Slider | `Verb_CastAbilityDash` (a subclass of the jump verb) with a zero-height `PawnFlyer`, so it slides along the ground. Player-only. | `Verb_CastAbilityDash.cs`, `Defs/Flyers_SillyAnimals.xml` |
| Charge | Charger | Same dash, using the `PawnFlyer_Charge` flyer. On landing it deals 10 + 7.5 x body size blunt damage (walrus 25, elephant 40) to hostiles at or next to the landing cell, then knocks them back and staggers them. AI uses it on targets more than 3.9 cells away. | `PawnFlyer_Charge.cs`, `CompAbilityEffect_ChargeTarget.cs` |
| Regenerate | Tenacious | Self-cast that applies the `Ordo_Regenerating` hediff: heals non-permanent injuries by 1 HP per second for 30 seconds. 12-hour cooldown. Auto-cast below 60% health. | `HediffComp_Regenerate.cs`, `SelfBuffAutoCast.cs` |
| Howl | Pack Howler | Area buff on the caster: `CompAbilityEffect_OnlyTargetAllies` plus `CompProperties_AbilityAnimalRoar` (needs hearing, plays the animal's own call), giving `Ordo_Rallied` for 30 seconds. Radius 14.9. Allies are the same faction, or for wild animals the same race. Auto-cast when an enemy is within 19.9 cells. | `CompAbilityEffect_OnlyTargetAllies.cs`, `SelfBuffAutoCast.cs` |
| Poison Bite | Venomous | Touch ability using `CompAbilityEffect_Strike` with Core's `ToxicBite` damage, 4 + 6 x body size (rat about 5, megaspider about 11). Toxic buildup comes from the damage def, so it scales with toxic resistance. | `CompAbilityEffect_Strike.cs` |
| Intimidate | Intimidating | Copy of Odyssey's TerrorRoar: gives the `Terror` mental state in a 9.9 radius, chance up to 40% by hearing. | `Defs/Abilities_SillyAnimals.xml` |
| Withdraw | Shelled | Self-cast giving `Ordo_Withdrawn` for 20 seconds: armor +1.2 sharp and blunt, +0.6 heat, MoveSpeed x0.05. Auto-cast below 75% health. | `Defs/Hediffs_SillyAnimals.xml`, `SelfBuffAutoCast.cs` |
| Kick | Kicker | Touch ability using `CompAbilityEffect_Strike`: Blunt 6 + 5 x body size (horse 18), then knockback and a 2-second stagger. | `CompAbilityEffect_Strike.cs`, `KnockbackUtility.cs` |
| Spit | Spitter | `CompProperties_AbilityLaunchProjectile`, range 7.9. `Projectile_ApplyHediff` gives flesh pawns it hits `Ordo_SpitInEyes`: sight x0.5 for 15 seconds. | `Projectile_ApplyHediff.cs` |
| Hidden Dragon | Big Cat | Self-cast that applies `Ordo_HiddenDragon`: `HediffComp_Invisibility` (the same mechanism as Royalty's psychic invisibility; the comp is in Core) plus `HediffComp_RemoveOnAttack`. Lasts 20 seconds. Auto-cast when the enemy is more than 6.9 cells away. | `HediffComp_RemoveOnAttack.cs`, `SelfBuffAutoCast.cs` |

Other files:

- `SillyAnimalThings.cs` is the Harmony bootstrap. It also has a `Pawn.SpawnSetup` postfix that grants missing `Ordo_` kind abilities to animals that existed before the mod was added, because vanilla only grants kind abilities at generation.
- `SelfBuffAutoCast.cs` holds `AutoCastExtension`, a per-ability mod extension with `castBelowHealth`, `castWhenEnemyFartherThan` and `castWhenEnemyWithin`. Add it to any self-targeted ability instead of adding code.
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
| Pack Howler | Timber Wolf, Arctic Wolf, Warg, Greatwolf, Bog Hound, Husky | Howl |
| Venomous | Rat, Waste Rat, Cobra, Megaspider, Monitor Lizard | Poison Bite |
| Intimidating | Goose, Gorilla, Grizzly Bear, Cobra | Intimidate |
| Shelled | Tortoise, Sea Turtle, Hermit Crab, Stone Crab, Lava Snail, Armadillo, Megascarab | Withdraw |
| Kicker | Horse, Donkey, Ostrich, Emu, Cassowary | Kick |
| Spitter | Dromedary, Alpaca | Spit |

Designed but not built:

| Tag | Animals | Ability |
|---|---|---|
| Prolific | Hare, Snowhare, Guinea Pig | Duplicate: targets another hare, puts both on cooldown, creates a new hare |
| Musky | Muskox, Mink | Spray Musk |
| Small Cat | Cat, Lynx | Nine Lives or Pounce (**undecided**) |
| Alligator | Alligator | Death Roll, or untagged (**undecided**) |
| Leaper | Hare, Snowhare, Bullfrog, Colossus Toad, Gazelle, Monkey | Leap |
| Quilled | Porcupine | Quill Volley |
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
2. ~~Build the cheap, high-value batch: Howl, Poison Bite, Intimidate, Withdraw, Kick, Spit.~~ Done.
3. ~~Test in game.~~ Done, see below. Add scenarios for anything new.
4. Spray Musk (once its effect is decided) and Leap.
5. Bigger items by user preference. Fetch and Duplicate looked most popular.

### Open questions for the user

- Small cats: Nine Lives or Pounce?
- Alligator: Death Roll, or untagged?
- Spray Musk: what effect?
- Duplicate: does the new hare spawn as a baby or an adult? Male/female only? Does a tamed pair produce a tamed hare?
- Should the alphabeaver behaviour also include manhunters chewing through walls and doors (a pathing hook)? Not built.

## In-game test results

### Running the scenario suite

`tools/BridgeTools/Scenarios` holds 37 scenarios, one per mechanism and per case that behaves differently, plus one that checks every tagged race got its abilities. Start the game through GABS, then call `ordo/run_scenarios`, with `filter` to run only the names containing it. Tests don't record anything; for videos, see the showcase below.

- **Profiles:** all open straight into an `autostart` save at midday, in a 720p window. `sillyanimalthings` skips the 2 Fire Burst scenarios and `sillyanimalthings-biotech` runs all 37.
- **Headless:** `sillyanimalthings-headless` and `sillyanimalthings-biotech-headless` add Waylith's `-Headless` flag, which means no window and no rendering. The suite takes 1.4-4 s, so use these unless you need to see the game. Screenshots don't work, and `games_stop` fails because there's no window to signal, so stop with `games_kill`. Headless runs don't exercise anything that draws, so use a windowed run for visual changes. `-nographics` and `-batchmode` do nothing.
- **The test save:** a 33x33 map of flat soil with no pawns, things, roofs or fog, just big enough for one site, which every scenario shares. An empty colony gets the Stranger in Black rescue event, so both tools turn off the storyteller and remove all pawns before starting. That lasts until the save is next loaded.
- **Remaking a save:** with no `autostart.rws` in the profile's `Saves`, call `ordo/start_test_colony`, `rimbridge/wait_for_long_event_idle`, `ordo/blank_map` and `ordo/set_hour`, then `rimworld/save_game` with `saveName` set to `autostart`.
- **What fails a scenario:** a broken check, an exception, any error in the game log, or a dialog left open.
- **Deploying:** stop the game, then `dotnet build tools/BridgeTools/BridgeTools.csproj -c Debug '-t:Build;CopyToRimworld'`. The suite doesn't deploy the mod, so deploy that too after changing it.
- **The suite is known to catch bugs:** with four faults put in on purpose (the roamer bug, a violent Hidden Dragon verb, an error logged by strikes, and a Detonate dialog), exactly the scenarios that cover them failed.

**Known flake:** `detonate_manhunter` and `combat_hidden_dragon_ambush` have each occasionally failed in a full run, with the manhunter not hurting the test colonist. Neither failed in 12 isolated repeats or the last 4 full runs. The cause isn't known: clothing, pacifism and non-combatant scoring were ruled out or didn't explain it, and drafting the real colonists may have helped. Both scenarios now report the pawns' state on failure, so read that next time it happens. `combat_hidden_dragon_ambush` later failed on "the colonist was hurt", with the tiger revealed but the colonist unhurt, which would fit a bite that missed, though that isn't confirmed. Explosions left running by cleanup also made the next stage's manhunters fail when both shared a site, which is now fixed, but it isn't known whether that explains the earlier `detonate_manhunter` failures.

**Possible bug:** `strike_kick_player` failed once in a recorded full run. The video shows the cow flash and get knocked back, and the cooldown started, but the cow's health never dropped. It passed the 6 repeats straight after. The cause isn't known, and it could be a real kick that does no damage. The scenario now reports the cow's state on failure.

### Recording the showcase

`tools/BridgeTools/Showcase` records a staged clip of each ability at normal speed with sound, separate from the tests. They share only `tools/BridgeTools/Stage.cs`, which spawns things on a site and cleans up. The clips' checks only decide whether a take is worth keeping, and a clip gets up to 3 takes.

1. Needs ffmpeg (`winget install Gyan.FFmpeg`), and build the audio recorder once: `dotnet build tools/ProcessAudioCapture/ProcessAudioCapture.csproj -c Release`.
2. Start the `sillyanimalthings-showcase` profile through GABS. It's a copy of the Biotech one with a 1080p window, no music, and `runInBackground` on so that it keeps playing sound without focus. Don't minimize the window. The game's sound plays out loud while recording.
3. Call `ordo/record_showcase` with `outputDirectory` set to the absolute path of `tools/BridgeTools/videos`, which is gitignored, and `audioCapturePath` set to the absolute path of `tools/ProcessAudioCapture/bin/Release/net10.0-windows/win-x64/ProcessAudioCapture.exe`. `filter` records only the matching clips. All 15 take about 95 s. Don't use a folder called `showcase`: Windows would treat it as the `Showcase` source folder.
4. Run `tools/BridgeTools/Showcase/make_showcase.ps1`, which takes about 45 s. It cuts one captioned 60 fps MP4 per clip, plus `highlight_reel.mp4` when there's more than one, then deletes the recording unless given `-KeepRecording`.

How it works:

- **One continuous recording:** the tool starts ffmpeg and the audio recorder, then logs each clip's start and end times in `timeline.tsv`. Video frames are stamped with the real time, so the script can cut both tracks at the same moment. Detonate's sound measured 25 ms from its flash.
- **Video:** ffmpeg's `gfxcapture` records the game window, cropped to the middle 1280x720. It only gets frames when the window changes, so ffmpeg can't stop while the game isn't drawing. Never block the main thread waiting for it: that once added a 15 s delay.
- **Sound:** `ProcessAudioCapture` uses Windows' process loopback API, which records only the game, not the microphone or other programs. ffmpeg's only audio input here is the microphone, so don't use that. It's self-contained because the game can't find a per-user .NET install.
- **Tick stepping:** don't film while stepping ticks. Pawns' drawn positions don't update while the game is paused between steps, so they appear to turn and flash without moving.

Captions come from each ability's label and as much of its description as fits on a line, so they follow the mod's own text. The game UI is hidden through its screenshot mode. The Gnaw clip starts its wall worn down, because a real wall takes three bites with ten seconds between them.

### Findings

Run through RimBridgeServer with `tools/BridgeTools`. No errors were logged except from the faults put in on purpose.

**Changed: Detonate has no confirmation dialog**, at the user's request.

**Trunk Blast sprays around its target, not along the way.** The vanilla spray hits the target cell and the 6 cells around it, weighted to the far side, so it won't put out a fire between the elephant and its target.

**Fixed: abilities did nothing for player-owned roamers.** `Pawn.ThreatDisabledBecauseNonAggressiveRoamer` makes a player-owned roamer (`race.roamMtbDays` set) non-hostile to everything until it has recently fought, so `Thing.HostileTo(Thing)` is false. Use `HostilityUtility.IsEnemyOf` in any ability that a player's animal casts and that needs to know who its enemies are; it also checks against the caster's faction.

- **Charge** (Muffalo, Bison, Goat, Elk, Moose, Wild Boar) did no damage. A player muffalo now leaves an enemy cow at 80% with a bruise, knockback and stagger; friendly and wild cows at the landing spot aren't knocked back or staggered.
- **Intimidate** (Goose) failed 14 rolls in a row at 40%. It now uses `CompAbilityEffect_OnlyTargetEnemies` instead of vanilla's `CompAbilityEffect_OnlyTargetHostiles`. Enemy cows pass that filter 100% of the time; the goose, friendly and wild cows 0%. 2 of 4 enemy cows entered `Terror` after two roars, and a manhunting goose still terrifies colonists.
- `Projectile_WaterBlast` still uses `HostileTo` when `preventFriendlyFire` is set. No trunked animal is a roamer today, but a roamer added to that tag would hit this.

| Ability | Commanded | AI | Evidence |
|---|---|---|---|
| Hidden Dragon | Pass | Pass | Invisible through the approach; removed by the first bite, with the cooldown still running |
| Detonate | Pass | Pass | Caster dies, and the blast hurts the target; see the known flake |
| Regenerate | Auto-cast pass | Auto-cast pass | Cast below 60%, health rises; untrained player animals don't cast it |
| Withdraw | Auto-cast pass | Auto-cast pass | Cast below 75%; armor bonus not measured |
| Howl | Pass | Pass | Caster and allies in range get `Ordo_Rallied`; stat bonus not measured |
| Belly Slide | Pass | n/a | Moved 10 cells, cooldown started |
| Charge | Pass | Pass | Walrus and muffalo commanded, walrus AI; damage across body sizes not compared |
| Trunk Blast | Pass | Not run | Target pushed back, fire in the spray put out |
| Kick | Pass | Pass | Bruise, knockback, stagger; AI cooldown started |
| Poison Bite | Pass | Pass | `ToxicBuildup` when commanded; AI casts it and hurts its target |
| Spit | Pass | Not run | `Ordo_SpitInEyes` on target |
| Intimidate | Pass | Pass | Player goose scares enemies only; manhunting goose and cobra put colonists in `Terror` |
| Gnaw | Pass | Pass | Damages wood and restores food; stone is refused; hungry wild beavers gnaw, a player's don't |
| Trunk Throw | Pass | Not run | Hits pawns, lands at cells, and is disabled with no chunk nearby; how the projectile looks not confirmed |
| Fire Burst | Pass | Pass | Cooldown starts when cast; the burning itself isn't checked |

Things that look like bugs while testing but aren't:

- `Ability.StartCooldown(0)` leaves `CanCast` false until the next tick.
- Enemy-faction animals flee instead of fighting. Use a manhunter to test animal AI.
- A location jump is refused when the caster spawned inside rock.
- A cast from a stationary target can look like it missed, because explosions spread over several ticks and spray projectiles land one after another. Wait for the effect instead of checking straight away.
- Trained, knocked-back targets that are bigger than the caster stagger but don't move.

## Testing risks to check in game

- AI Charge from long range: a manhunting muffalo spawned about 16 cells from its target (range 11.9) walked in with `AttackMelee` and didn't charge within 200 ticks, while a walrus spawned 10 cells away did. `JobGiver_Manhunter` only checks abilities when it picks a job, and melee jobs last 420-900 ticks, so animals that start out of range may rarely charge. Cause not confirmed.

- Thrown chunk projectile: it's drawn with the chunk's `graphic.MatSingle`; check it renders.
- How often hostile elephants and walruses use their abilities, and whether hungry wild alphabeavers are too eager to eat buildings.
- Boomrat has both Detonate and Fire Burst. The AI picks the first castable ability, and Detonate triggers from 15 cells, so hostile boomrats may never fire burst. Fire burst also isn't protected from its own flames, so a boomrat may ignite itself (funny, but check).
- Charge damage across body sizes (goat about 16, mastodon about 44).
- Howl: allies get `Ordo_Rallied` and the caster's own call plays. A player-ordered cast goes through the same self-target path as vanilla's roars.
- Intimidate: whether `Terror` on colonists from wild geese is too harsh. In testing, one manhunting goose and cobra put two of three colonists in `Terror` within seconds.
- Withdraw: animals crawl rather than stop dead, by design. Check the armor bonus is felt.
- Kick and Poison Bite: the AI walks to any hostile within 8 cells.
- Tag startup: the training tab lists the new trainables on `trainability: None` animals (code says it should), and existing saves pick up the abilities.

## Environment notes and gotchas

- **Game paths:** RimWorld is at `D:\SteamLibrary\steamapps\common\RimWorld` (game data in `Data\`, version 1.7.5000). `Directory.Build.props` has a `SteamLibraryDirectory` property defaulting to `D:\SteamLibrary`, overridable in a git-ignored `Directory.Build.local.props`. With it, builds compile against the real game assemblies ("Local RimWorld referenced: true"), and the `CopyToRimworld` target copies to the right place. This came from the user's own work and was uncommitted when this note was written.
- **`dotnet`:** it may not be on `PATH`. The SDK is at `~/.dotnet/dotnet.exe`. If a build step prints nothing, check that it actually ran.
- **Ability comps:** give every comp entry a `Class`. A bare `<li>` with only `compClass` loads as the base properties type, and `CompAbilityEffect.Props` throws an invalid cast. Use `Class="CompProperties_AbilityEffect"` when there are no custom properties.
- **Publicizer:** `Krafs.Publicizer` makes protected game members public. Overrides of protected methods (for example `Projectile.Impact`, `PawnFlyer.RespawnPawn`) must therefore be declared `public override`, and private fields like `PawnFlyer.destCell` are accessible.
- **Decompiled source for API lookups:**
  - Install ilspycmd locally: `dotnet tool install ilspycmd --tool-path <dir>`.
  - Then run `<dir>\ilspycmd.cmd -p -o <out> "D:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll"`. Note that `Assembly-CSharp.dll` is in the install root.
  - Scratchpad copies don't survive between sessions, so expect to regenerate this.
- **Style:** C# uses tabs, file-scoped namespace `Ordo.SillyAnimalThings`, and one parameter per line in method signatures (matching LegendaryIsUnique). XML uses 2 spaces.
