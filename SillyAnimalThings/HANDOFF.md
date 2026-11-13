# SillyAnimalThings handoff

Last updated 2026-11-13 (animal AI: abilities used wherever animals fight; see "How the AI uses abilities").

## What the mod is

Gives animals silly abilities. It uses the same pattern as Odyssey's war trumpet:

- `AnimalTagDef`s give abilities to races: at startup each tag's abilities go into `PawnKindDef.abilities` for every kind of each member race.
- Each ability is gated by a special `TrainableDef` through `CompProperties_AbilityRequiresTrainable`. The tag adds that trainable to `race.specialTrainables`, read from the comp. Don't use `TrainableDef.enablesAbility`: it's `[Obsolete]` and nothing in the game reads it.
- Wild, manhunting and enemy animals use the ability by themselves (`aiCanUse` + `aiCanCastWithoutTrainable`).
- Tamed animals only get the gizmo once trained. From then on they also use it by themselves when they fight, except Detonate and Fire Burst, which would hurt the colony. Untrained tamed animals never do. See "How the AI uses abilities".

Dependencies:

- Hard: Harmony, Odyssey.
- Optional: Biotech, needed for Fire Burst (the whole Incendiary tag) and the toxalope. These are guarded with `MayRequire="Ludeon.RimWorld.Biotech"`.

All defNames use the `Ordo_` prefix.

## Current state

- The tag system, CSV conversion, expanded memberships and the second batch of abilities (Howl, Poison Bite, Intimidate, Withdraw, Kick, Spit) are written. It builds with no warnings (`dotnet build SillyAnimalThings/SillyAnimalThings.csproj -c Debug`; see "Building" below for why `-c Debug` matters).
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
| Detonate | Volatile | A rocket leap, range 14.9: the dash verb with `PawnFlyer_Rocket`, which arcs high trailing fire, sparks and smoke, then calls `pawn.Kill(null)` where it lands. That fires the race's death action (big explosion, small explosion, or tox cloud). The kill is in the flyer because the jump verb applies ability comps at take-off; the comp only decides when the AI launches, at hostiles within 14.9 cells. | `PawnFlyer_Rocket.cs`, `CompAbilityEffect_Detonate.cs` |
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
- `SelfBuffAutoCast.cs` holds `AutoCastExtension`, a per-ability mod extension with `castBelowHealth`, `castWhenEnemyFartherThan` and `castWhenEnemyWithin`, and `needsAllyInRadius`. Add it to any self-targeted ability instead of adding code.
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

The workflow and tools below are shared by every mod in the repo. The `rimworld-mod-dev` skill (`.claude/skills/rimworld-mod-dev`) describes them for any mod; this section keeps what's specific to SillyAnimalThings.

### Running the scenario suite

`tools/BridgeTools/Scenarios` holds 49 scenarios, one per mechanism and per case that behaves differently, plus one that checks every tagged race got its abilities. Start the game through GABS, then call `ordo/run_scenarios`. Tests don't record anything; for videos, see the showcase below.

- **Suites:** every scenario is in suites: one per ability (`charge`, `detonate`, ...), plus `ai`, `player`, `knockback`, `visuals` and `setup`. Pass `suite` (space or comma separated) to test one feature, and `ordo/list_scenarios` lists them. `filter` still matches names.
- **The game is the fixture:** keep one game running and run suites against it one after another; each scenario sets up its own site and cleans up after itself. Load XML changes with `ordo/hot_reload_defs` and most C# changes with `hot_reload.ps1` and `ordo/apply_update` (see "Reloading without restarting"). Restart for the rest, and for changes to the bridge tools.
- **Check visuals with scenarios, not by eye:** the `visuals` suite checks that each overlay's mote appears and goes, through `HediffComp_Overlay.LastMote`. The game doesn't list motes with the map's other things. Use the showcase to judge how something looks once it's there.

- **Profiles:** all open straight into an `autostart` save at midday, in a 720p window. `sillyanimalthings` skips the 3 Fire Burst scenarios and `sillyanimalthings-biotech` runs all 49. `sillyanimalthings-profiling` is for timing the mod; see "Dubs Performance Analyzer" below.
- **Headless:** `sillyanimalthings-headless` and `sillyanimalthings-biotech-headless` add Waylith's `-Headless` flag, which means no window and no rendering. The suite takes 1.4-4 s, so use these unless you need to see the game. Screenshots don't work, and `games_stop` fails because there's no window to signal, so stop with `games_kill`. Headless runs don't exercise anything that draws, so use a windowed run for visual changes. `-nographics` and `-batchmode` do nothing.
- **The test save:** a 33x33 map of flat soil with no pawns, things, roofs or fog, just big enough for one site, which every scenario shares. An empty colony gets the Stranger in Black rescue event, so both tools turn off the storyteller and remove all pawns before starting. That lasts until the save is next loaded.
- **Remaking a save:** with no `autostart.rws` in the profile's `Saves`, call `ordo/start_test_colony`, `rimbridge/wait_for_long_event_idle`, `ordo/blank_map` and `ordo/set_hour`, then `rimworld/save_game` with `saveName` set to `autostart`.
- **What fails a scenario:** a broken check, an exception, any error in the game log, or a dialog left open.
- **Deploying:** stop the game, then build and copy each project as two separate commands, both with `-c Debug`: `dotnet build <project> -c Debug`, then `dotnet build <project> -c Debug -t:CopyToRimworld`. Do this for `SillyAnimalThings/SillyAnimalThings.csproj` and `tools/BridgeTools/BridgeTools.csproj`; the suite deploys neither.
- **Building:** without `-c Debug`, the `Debug` property group in `Directory.Build.props` doesn't apply, because that file is read before the SDK sets the default configuration. The build then goes to `bin/Debug` instead of `out/Assemblies`, and `CopyToRimworld` deploys whatever DLL was in `out/` already, often an older one from one of Rider's background builds. The symptom is "Could not find a type named Ordo..." for anything new. To check a deploy, search the deployed DLL for a new class name.
- **The suite is known to catch bugs:** with four faults put in on purpose (the roamer bug, a violent Hidden Dragon verb, an error logged by strikes, and a Detonate dialog), exactly the scenarios that cover them failed.

**Known flake:** `detonate_manhunter` and `combat_hidden_dragon_ambush` have each occasionally failed in a full run, with the manhunter not hurting the test colonist. Neither failed in 12 isolated repeats or the last 4 full runs. The cause isn't known: clothing, pacifism and non-combatant scoring were ruled out or didn't explain it, and drafting the real colonists may have helped. Both scenarios now report the pawns' state on failure, so read that next time it happens. `combat_hidden_dragon_ambush` later failed on "the colonist was hurt", with the tiger revealed but the colonist unhurt, which would fit a bite that missed, though that isn't confirmed. Explosions left running by cleanup also made the next stage's manhunters fail when both shared a site, which is now fixed, but it isn't known whether that explains the earlier `detonate_manhunter` failures.

**Fixed flake: knockback depended on age.** Stages generated animals at random ages, and body size shrinks with youth, so a young caster was sometimes smaller than its target and knockback skipped it (it skips anything bigger than the caster). `Stage.Spawn` now always makes adults. This may also explain some of the flakes below.

**Explained: `strike_kick_player` "never hurt".** A kick can take off a minor part, such as a tail, which leaves summary health at 100%, and `Stage.Hurt` only looked at summary health. It now also counts any injury or missing part. The kick was never doing no damage.

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

## How the AI uses abilities

Reviewed and reworked on 2026-11-13. Every way an animal fights now goes through `AnimalAbilityAI.TryGetAbilityJob` (`Behaviours/AnimalAbilityAI.cs`), with the hooks in `Behaviours/AnimalCombatPatches.cs`:

| Situation | Vanilla hook | Before |
|---|---|---|
| Manhunting, in a raid, or guarding its master | Prefix replacing `JobGiver_AIFightEnemy.GetAbilityJob` for animals with an `Ordo_` ability | Vanilla picked the first castable ability in list order |
| Fighting back against whoever hits it in melee | Postfix on `JobGiver_ReactToCloseMeleeThreat.TryGiveJob` | Plain melee only |
| A predator hunting prey | Postfix on `JobDriver_PredatorHunt.CheckWarnPlayerInterval`, which runs while it chases. Every 30 ticks it may pause the hunt for an ability (`resumeCurJobAfterwards`), and the hunt resumes after | Vanilla's hunt never uses abilities |
| Chasing in melee with a ranged or combat ability ready, or about to be | Postfixes on `JobGiver_Manhunter.TryGiveJob` and `JobGiver_AIFightEnemy.MeleeAttackJob` shorten the job to when it's ready + 90 ticks | Melee jobs lasted 420-900 ticks, so animals that started out of range rarely used them |

How the choice is made:

- **Self-buffs first** (`AutoCastExtension` with combat conditions), then offensive abilities by `AbilityAIExtension.priority`, highest first, then vanilla's order: an ability that can hit the foe, then an AOE target, then self.
- **`AbilityAIExtension`** (in `AnimalAbilityAI.cs`) is per ability: `priority` (Detonate is -1, so a boomrat fire bursts first), `useWhileHunting` (off for Intimidate, since terror sends prey running) and `playerAutoCast` (off for Detonate and Fire Burst).
- **Player animals:** vanilla's `CompAbilityEffect_RequiresTrainable.AICanTargetNow` lets *untrained* animals cast by themselves and stops trained ones. A postfix reverses that for `Ordo_` abilities: player animals use what they're trained in. The comps no longer refuse player casters.
- **Foes that aren't hostile:** a wild animal isn't hostile to a colonist hitting it, or to its prey. `HostilityUtility.IsEnemyOf` also counts the caster's `meleeThreat`, and the foe the AI last picked for it (remembered for 900 ticks), so abilities land on them. Use `IsEnemyOf` and `IsAllyOf` in any new ability.
- **Intimidate** only counts enemies within its range. Before, a manhunter that picked a far-off target found no one to aim at and cast it on itself: a goose roared from 14 cells and wasted the 3-hour cooldown.
- **Howl** needs an ally in its radius (`needsAllyInRadius`). **Hidden Dragon** casts only with the enemy 6.9-39.9 cells away. Self-buffs from damage aren't cast while downed.
- **Trunk Blast** (`CompAbilityEffect_SprayLiquidSparingAllies`) won't spray where an ally stands. That includes a wild elephant's own herd, which vanilla's faction check misses.

The `ai_*` scenarios cover each case. Run against the build before this change, the 8 that check for a cast failed and the 2 that check nothing is cast passed. Player scenarios use non-roamers (rats, boomrats), for the reason under "Testing risks".

## Visual effects

Reviewed against the showcase clips on 2026-11-10. Effects reuse the base game's flecks and motes; none need new art.

| Ability | What shows | Where |
|---|---|---|
| Belly Slide, Charge | Dust trail along the whole dash | `PawnFlyer_Trail` (both flyers derive from it) |
| Belly Slide | The penguin lies flat for the slide and stands up where it lands, by setting its posture during the flight | `PawnFlyer_Slide` |
| Detonate | Fire, sparks and smoke along a high arc, then the race's own explosion | `PawnFlyer_Rocket` |
| Withdraw | The shield belt's bubble, tinted like a shell, for as long as it's withdrawn | `Ordo_Mote_Shell` on `Ordo_Withdrawn` |
| Kick, Charge, Trunk Blast | Base game's STUN overlay for as long as the knockback stagger lasts | `Ordo_Dazed` hediff, added by `KnockbackUtility` |
| Spit | Green splash on impact, then green drips while half blind. No damage, so no red injury flash | `landedEffecter`, `Ordo_SpitInEyes` |
| Intimidate | Small terror halo on each pawn it scares, gone when the terror ends | `CompAbilityEffect_GiveTerror`, `Ordo_Terrified`, `Ordo_Mote_TerrorHalo` |
| Howl | A small ring on each ally it rallies, which also tells it apart from Intimidate (both share Odyssey's roar ring) | `Ordo_RallyRing` |
| Regenerate | Healing cross each time it heals | `HediffComp_Regenerate` |
| Gnaw | Wood chips, dust and wood-chewing sound while it winds up | `Ordo_Gnawing` warm-up effecter |
| Trunk Throw | The chunk lands one cell back towards the thrower when it hits a pawn; in the pawn's own cell it was drawn underneath and couldn't be seen | `Projectile_ThrownChunk` |

`HediffComp_Overlay` is the reusable piece for debuffs: give a hediff a mote, kept on the pawn or remade every `intervalTicks`, and optionally `whileMentalState` to end it with a mental state. The base game's motes grow several cells wide, which over a crowd fills the screen, so the mod has smaller copies (`Ordo_Mote_RallyRing`, `Ordo_Mote_TerrorHalo`).

Still without a visual: **Poison Bite** after the bite (`ToxicBuildup` is the base game's hediff).

Showcase staging fixes made in the same review: Charge targets deer (cows are bigger than a walrus, so they never moved), and Trunk Blast only lights fires next to the cow (one further away survived the spray and set the cow alight).

## Dubs Performance Analyzer

`mod/Analyzer.xml` gives the mod its own tab, under Modder > SillyAnimalThings, for players and for watching a live game. DPA reads it from the mod's root folder, and without DPA it does nothing. It names each method, because the installed 1.7 build of DPA only finds methods in 4 of the mod's 23 types when given `Types`, and doesn't read `NestedTypes`. DPA skips names it can't find without logging anything, so regenerate the list with `ordo/profiling_targets` after adding or renaming methods.

**Profiling a test run:** start the `sillyanimalthings-profiling` GABS profile, then call `ordo/run_scenarios` with `profile: true`. The profile is the Biotech one plus DPA, with its own data folder, so the normal test profiles never load DPA. The runner patches every method `ordo/profiling_targets` lists into DPA through RimBridgeServer's `rimworld/dpa_*` tools. It resets DPA before each scenario and snapshots it after, because DPA only keeps the last 2000 ticks and a full run is about 3300. The result's `profile` section has per-method calls, total, average and worst time (with the scenario it happened in), and per-scenario totals. Things to know:

- It needs a window, so it takes about 21 s instead of 4. Headless, DPA logs "Caught analyzer trying to begin a new update cycle before finishing the previous one", which fails the scenarios.
- Only time inside game ticks is counted, so a scenario's own setup, such as dealing damage directly, doesn't show.
- Times include whatever the method calls, and the first cast of an ability is much slower than later ones. Charge's landing took 94 ms the first time and 2-13 ms after.

The first run on 2026-11-10 found nothing that costs anything every tick: the debuff overlays average 0.012 ms a call and the dash trail 0.007 ms. The rest is one-off work when an ability lands, all under 15 ms apart from first casts and Detonate's explosion (42 ms).

## Reloading without restarting

Both defs and C# code can be changed in a running game, headless or windowed. Checked on 2026-11-12, with the full suite passing on games changed both ways. Restart only for the changes listed under "Needs a restart".

**Defs (XML): `ordo/hot_reload_defs`.** Deploy the changed XML (`-t:CopyToRimworld`), then call it; it takes about 1.5 s. It runs the same reload as dev mode's `Actions\Hot reload Defs`, and works headless where the debug action hangs:

- A synchronous long event only runs once its loading screen has been drawn (`QueuedLongEvent.alreadyDisplayed`), and nothing is drawn headless. The tool marks each event as drawn by reflection. `ordo/long_events` shows the queue.
- The reload rebuilds every def from XML, which drops the abilities and trainables the tags add in code. `SillyAnimalThings.cs` patches `PlayDataLoader.HotReloadDefs` to queue `AnimalTagUtility.ApplyAllTags` after it. `ExecuteWhenFinished` runs too early, before the reload has started, and `LoadAllPlayData`'s flag is `recovering`, not a hot reload flag.
- `ordo/ability_def` reads an ability's live label, range and cooldown, to check a change took.

**C# code: .NET hot reload, through `tools/HotReloadDelta`.** Waylith's runtime (`.NET 10.0.15-waylithclr`) accepts hot reload updates for the mod's Debug build with no setup: `MetadataUpdater.IsSupported` is true, and no `DOTNET_MODIFIABLE_ASSEMBLIES` or debugger is needed. `ordo/runtime_info` reports this. The game loads the mod from its file into the default load context.

1. Deploy a build the normal way, start the game, and run `tools/HotReloadDelta/hot_reload.ps1 -Project SillyAnimalThings -Reset`. It saves the exact `csc` command line and snapshots `src`, which is the baseline the game has loaded.
2. Edit the code, then run `hot_reload.ps1 -Project SillyAnimalThings`. It prints a folder; pass that to `ordo/apply_update` as `directory`. Making the delta takes about 4 s and applying it a few milliseconds.
3. Repeat. Deltas chain, so the script snapshots the source each one applied, and the tool replays the chain to reach the game's current state.

How it works: `HotReloadDelta` is what `dotnet watch` does on the build side. It compiles the baseline and current source with Roslyn using the build's own arguments, finds changed and new members, and emits the metadata, IL and PDB deltas with `EmitDifference`. Its baseline comes from the deployed DLL and its embedded PDB. `ordo/apply_update` calls `MetadataUpdater.ApplyUpdate` in the game.

- **Applies:** changed method bodies, new methods, new fields, and new types (whose static initializer runs on first use).
- **Needs a restart (the script says so and writes nothing):** removed methods, changed signatures, and new constructors or virtual methods on existing types. Removed types and fields are only noted, since they stay loaded harmlessly.
- **Not rerun:** static constructors, such as the Harmony patching in `SillyAnimalThings`. A new or changed Harmony patch needs a restart.
- **Lost on restart:** the deployed DLL on disk is unchanged, so after a restart, deploy a fresh build and `-Reset`.

**Debugging in Rider: attach to the running game.** Checked on 2026-11-12 with a headless game. The game ships the .NET debugging files (`mscordaccore.dll`, `mscordbi.dll`) next to `coreclr.dll`, and the mod's Debug build embeds its PDB, so Rider maps breakpoints to the source with no setup.

- Start the game through GABS; `games_status` gives its PID as `gamePid`. In Rider, use Run > Attach to Process and pick **.NET Debugger**, not ".NET and Native" or LLDB.
- Breakpoints in mod code stop the game, with full locals, stepping, expressions that call methods, and the call stack down through the game's own code (`Ability.Activate`, `Verb.WarmupComplete`, `Pawn.Tick`). Rider reports "didn't find the associated module" until the mod's DLL loads, then binds the breakpoint.
- While stopped, the game's main thread is paused, so bridge tool calls hang until you resume. A scenario run started before the stop carries on afterwards.
- Stopping the debug session detaches; the game keeps running.
- **It can't be combined with `ordo/apply_update`:** while a debugger is attached, the runtime refuses those updates ("Assembly updates cannot be applied while a debugger is attached"). Detach first, or try Rider's own hot reload, which goes through the debugger; that one isn't tested here.

Other tools, not used: [Reloader](https://github.com/pardeike/Reloader) and [Rimworld-Doorstop](https://github.com/pardeike/Rimworld-Doorstop) need Unity's Mono runtime or Doorstop, which Waylith doesn't have. [Lib.QuickReload](https://www.nuget.org/packages/Lib.QuickReload) is deprecated, and [HotSwap](https://github.com/Zetrith/HotSwap) is archived. [EditCompileReload](https://github.com/Zetrith/EditCompileReload) would probably work, since it's plain .NET, but it rewrites the IL at build time and supports fewer kinds of change than the runtime's own hot reload. [RimWorldHotReload](https://github.com/xiao-e-yun/RimWorldHotReload) reloads textures and sounds too; its 1.7 support isn't stated.

## Testing risks to check in game

- How often hostile elephants and walruses use their abilities, and whether hungry wild alphabeavers are too eager to eat buildings.
- Boomrat: Fire Burst now goes before Detonate within 2.9 cells. Fire burst isn't protected from its own flames, so a boomrat may ignite itself (funny, but check).
- Charge damage across body sizes (goat about 16, mastodon about 44).
- Howl: allies get `Ordo_Rallied` and the caster's own call plays. A player-ordered cast goes through the same self-target path as vanilla's roars.
- Intimidate: whether `Terror` on colonists from wild geese is too harsh. In testing, one manhunting goose and cobra put two of three colonists in `Terror` within seconds.
- Withdraw: animals crawl rather than stop dead, by design. Check the armor bonus is felt.
- Kick and Poison Bite: the AI walks to any enemy within 8 cells.
- Wild animals now fight back with their abilities, and predators use them on prey. Check this doesn't make hunting elephants or walruses, or wild cobras, too punishing.
- A player's pen animals (horses, cows, muffalo and other roamers) don't fight back unless they started the fight: vanilla disables threats between them and anything they haven't engaged. So trained ones rarely use abilities by themselves; whether guarding a master gets round this isn't tested. Not changed.
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
