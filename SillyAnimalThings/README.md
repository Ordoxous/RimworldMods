# SillyAnimalThings

`Ordo.SillyAnimalThings`. Gives animals silly abilities: elephants spray water, boomalopes rocket at their enemies, penguins belly-slide, and so on. Wild and manhunting animals use them by themselves, and tamed animals get them once trained.

This README is for working on the mod. The player-facing description is in `mod/About/About.xml`. For building, deploying, testing, reloading, profiling and recording, load the repo's `rimworld-mod-dev` skill. Its `rimworld-gotchas.md` holds the game API traps this mod ran into: roamers not being hostile, `IsEnemyOf`, ability comp classes, and more.

## Dependencies

- **Hard:** Harmony, Odyssey.
- **Optional: Biotech,** needed for Fire Burst (the whole Incendiary tag) and the toxalope. These are guarded with `MayRequire="Ludeon.RimWorld.Biotech"`.

All defNames use the `Ordo_` prefix. Style: tabs, file-scoped namespace `Ordo.SillyAnimalThings`, and one parameter per line in method signatures. XML uses 2 spaces.

## How it works

It uses the same pattern as Odyssey's war trumpet:

- **Tags grant abilities.** `AnimalTagDef`s give abilities to races: at startup, `AnimalTagUtility.ApplyAllTags` adds each tag's abilities to `PawnKindDef.abilities` for every kind of each member race.
  - Members are listed in `Defs/AnimalTags_SillyAnimals.xml` by race defName, which doesn't always match the CSV name (Scimitar Cat is `Cat_Scimitar`).
  - An animal can have several tags. Other mods can patch their races into a tag's `races` list, or add their own `AnimalTagDef`.
- **Training gates each ability,** through a special `TrainableDef` and `CompProperties_AbilityRequiresTrainable`. The tag adds that trainable to `race.specialTrainables`, read from the comp.
  - Don't use `TrainableDef.enablesAbility`: it's `[Obsolete]`, and nothing in the game reads it.
- **Wild, manhunting and enemy animals** use their abilities by themselves (`aiCanUse` + `aiCanCastWithoutTrainable`).
- **Tamed animals** only get the gizmo once trained. From then on they also use the ability by themselves when they fight, except Detonate and Fire Burst, which would hurt the colony. Untrained tamed animals never use them.

Other files:

- `SillyAnimalThings.cs` is the Harmony bootstrap. It calls `ApplyAllTags` first.
  - It has a `Pawn.SpawnSetup` postfix that grants missing `Ordo_` kind abilities to animals that existed before the mod was added, because vanilla only grants kind abilities at generation.
  - It patches `PlayDataLoader.HotReloadDefs` to queue `ApplyAllTags` again after a def reload, which otherwise drops what the tags added in code. `ExecuteWhenFinished` runs too early, before the reload has started.
- `SelfBuffAutoCast.cs` holds `AutoCastExtension`, a per-ability mod extension with `castBelowHealth`, `castWhenEnemyFartherThan`, `castWhenEnemyWithin` and `needsAllyInRadius`. Add it to any self-targeted ability instead of writing code.
- `mod/Analyzer.xml` gives the mod its own Dubs Performance Analyzer tab; see below.
- All icons are borrowed vanilla textures and need real art.

### Abilities

| Ability | Tag | How it works | Key files |
|---|---|---|---|
| Trunk Blast | Trunked | `CompProperties_AbilitySprayLiquid`, 7 cells. The projectile puts out fires and knocks back and staggers pawns (but not pawns bigger than the caster). It sprays around its target, weighted to the far side, not along the way, so it won't put out a fire between the elephant and its target. It won't spray where an ally stands (`CompAbilityEffect_SprayLiquidSparingAllies`), including a wild elephant's own herd, which vanilla's faction check misses | `Projectile_WaterBlast.cs`, `KnockbackUtility.cs` |
| Trunk Throw | Trunked | Destroys a stone or slag chunk within 2.9 cells and launches a projectile drawn as that chunk. The chunk is put back where it lands, one cell back towards the thrower when it hits a pawn. The gizmo is disabled with no chunk nearby | `CompAbilityEffect_TrunkThrow.cs`, `Projectile_ThrownChunk.cs` |
| Detonate | Volatile | A rocket leap, range 14.9: the dash verb with `PawnFlyer_Rocket`, which arcs high, trailing fire, sparks and smoke, then calls `pawn.Kill(null)` where it lands. That fires the race's death action (big explosion, small explosion, or tox cloud). The kill is in the flyer because the jump verb applies ability comps at take-off; the comp only decides when the AI launches. No confirmation dialog, at the user's request | `PawnFlyer_Rocket.cs`, `CompAbilityEffect_Detonate.cs` |
| Gnaw | Woodchewer | Touch ability on a wooden building (Woody stuff, or WoodLog in its cost list). Deals 80 Bite damage and restores 25% food | `CompAbilityEffect_Gnaw.cs` |
| Hungry gnawing (behaviour) | Woodchewer | Harmony prefix on `JobGiver_GetFood.TryGiveJob`. A hungry, non-player animal with Gnaw ready targets a reachable wooden building within 12 cells | `Behaviours/HungryGnawing.cs` |
| Fire Burst | Incendiary (Biotech only) | Vanilla fire burst, radius 2.9. A comp subclass makes the AI cast it when a hostile is within the radius; vanilla only casts while the caster is being aimed at | `CompAbilityEffect_FireBurstNearEnemy.cs` |
| Belly Slide | Slider | `Verb_CastAbilityDash` (a subclass of the jump verb) with a zero-height `PawnFlyer`, so it slides along the ground. Player-only | `Verb_CastAbilityDash.cs`, `Defs/Flyers_SillyAnimals.xml` |
| Charge | Charger | The same dash, with `PawnFlyer_Charge`. On landing it deals 10 + 7.5 x body size blunt damage (walrus 25, elephant 40) to hostiles at or next to the landing cell, then knocks them back and staggers them. The AI uses it on targets more than 3.9 cells away | `PawnFlyer_Charge.cs`, `CompAbilityEffect_ChargeTarget.cs` |
| Regenerate | Tenacious | Self-cast that applies `Ordo_Regenerating`: heals non-permanent injuries by 1 HP a second for 30 seconds. 12-hour cooldown. Auto-cast below 60% health | `HediffComp_Regenerate.cs`, `SelfBuffAutoCast.cs` |
| Howl | Pack Howler | Area buff on the caster: `CompAbilityEffect_OnlyTargetAllies` plus `CompProperties_AbilityAnimalRoar` (needs hearing, plays the animal's own call), giving `Ordo_Rallied` for 30 seconds. Radius 14.9. Allies are the same faction, or for wild animals the same race. Auto-cast when an enemy is within 19.9 cells and an ally is in the radius | `CompAbilityEffect_OnlyTargetAllies.cs`, `SelfBuffAutoCast.cs` |
| Poison Bite | Venomous | Touch ability using `CompAbilityEffect_Strike` with Core's `ToxicBite` damage, 4 + 6 x body size (rat about 5, megaspider about 11). Toxic buildup comes from the damage def, so it scales with toxic resistance | `CompAbilityEffect_Strike.cs` |
| Intimidate | Intimidating | A copy of Odyssey's TerrorRoar: gives the `Terror` mental state in a 9.9 radius, with a chance of up to 40% by hearing. Uses `CompAbilityEffect_OnlyTargetEnemies`, not vanilla's `OnlyTargetHostiles`, so player roamers can use it | `Defs/Abilities_SillyAnimals.xml`, `CompAbilityEffect_GiveTerror.cs` |
| Withdraw | Shelled | Self-cast giving `Ordo_Withdrawn` for 20 seconds: armor +1.2 sharp and blunt, +0.6 heat, MoveSpeed x0.05. Auto-cast below 75% health | `Defs/Hediffs_SillyAnimals.xml`, `SelfBuffAutoCast.cs` |
| Kick | Kicker | Touch ability using `CompAbilityEffect_Strike`: Blunt 6 + 5 x body size (horse 18), then knockback and a 2-second stagger | `CompAbilityEffect_Strike.cs`, `KnockbackUtility.cs` |
| Spit | Spitter | `CompProperties_AbilityLaunchProjectile`, range 7.9. `Projectile_ApplyHediff` gives flesh pawns it hits `Ordo_SpitInEyes`: sight x0.5 for 15 seconds | `Projectile_ApplyHediff.cs` |
| Hidden Dragon | Big Cat | Self-cast that applies `Ordo_HiddenDragon`: `HediffComp_Invisibility` (the same mechanism as Royalty's psychic invisibility; the comp is in Core) plus `HediffComp_RemoveOnAttack`. Lasts 20 seconds. Auto-cast with the enemy 6.9-39.9 cells away. Its verb is `violent: false`, or the cast itself counts as an attack and removes the invisibility | `HediffComp_RemoveOnAttack.cs`, `SelfBuffAutoCast.cs` |

### How the AI uses abilities

Every way an animal fights goes through `AnimalAbilityAI.TryGetAbilityJob` (`Behaviours/AnimalAbilityAI.cs`), with the hooks in `Behaviours/AnimalCombatPatches.cs`:

| Situation | Vanilla hook | Without the hook |
|---|---|---|
| Manhunting, in a raid, or guarding its master | Prefix replacing `JobGiver_AIFightEnemy.GetAbilityJob` for animals with an `Ordo_` ability | Vanilla picked the first castable ability in list order |
| Fighting back against whoever hits it in melee | Postfix on `JobGiver_ReactToCloseMeleeThreat.TryGiveJob` | Plain melee only |
| A predator hunting prey | Postfix on `JobDriver_PredatorHunt.CheckWarnPlayerInterval`, which runs while it chases. Every 30 ticks it may pause the hunt for an ability (`resumeCurJobAfterwards`), and the hunt resumes after | Vanilla's hunt never uses abilities |
| Chasing in melee with a ranged or combat ability ready, or about to be | Postfixes on `JobGiver_Manhunter.TryGiveJob` and `JobGiver_AIFightEnemy.MeleeAttackJob` shorten the job to when it's ready + 90 ticks | Melee jobs lasted 420-900 ticks, so animals that started out of range rarely used their abilities |

How the choice is made:

- **Self-buffs come first** (`AutoCastExtension` with combat conditions). Then offensive abilities by `AbilityAIExtension.priority`, highest first. Then vanilla's order: an ability that can hit the foe, then an AOE target, then self.
- **`AbilityAIExtension`** (in `AnimalAbilityAI.cs`) is per ability:
  - `priority`: Detonate is -1, so a boomrat fire-bursts first.
  - `useWhileHunting`: off for Intimidate, since terror sends prey running.
  - `playerAutoCast`: off for Detonate and Fire Burst.
- **Player animals:** vanilla's `CompAbilityEffect_RequiresTrainable.AICanTargetNow` lets *untrained* animals cast by themselves and stops trained ones. A postfix reverses that for `Ordo_` abilities, so player animals use what they're trained in. The comps don't refuse player casters.
- **Foes that aren't hostile:** a wild animal isn't hostile to a colonist hitting it, or to its prey.
  - `HostilityUtility.IsEnemyOf` also counts the caster's `meleeThreat`, and the foe the AI last picked for it (remembered for 900 ticks), so abilities land on them.
  - Use `IsEnemyOf` and `IsAllyOf` in any new ability.
- **Intimidate** only counts enemies within its range. Otherwise a manhunter that picked a far-off target found no one to aim at and cast it on itself. A goose roared from 14 cells and wasted the 3-hour cooldown.
- Self-buffs triggered by damage aren't cast while downed.

### Visual effects

Effects reuse the base game's flecks and motes, and none need new art.

| Ability | What shows | Where |
|---|---|---|
| Belly Slide, Charge | A dust trail along the whole dash | `PawnFlyer_Trail` (both flyers derive from it) |
| Belly Slide | The penguin lies flat for the slide and stands up where it lands, by setting its posture during the flight | `PawnFlyer_Slide` |
| Detonate | Fire, sparks and smoke along a high arc, then the race's own explosion | `PawnFlyer_Rocket` |
| Withdraw | The shield belt's bubble, tinted like a shell, for as long as it's withdrawn | `Ordo_Mote_Shell` on `Ordo_Withdrawn` |
| Kick, Charge, Trunk Blast | The base game's STUN overlay for as long as the knockback stagger lasts | `Ordo_Dazed` hediff, added by `KnockbackUtility` |
| Spit | A green splash on impact, then green drips while half blind. No damage, so no red injury flash | `landedEffecter`, `Ordo_SpitInEyes` |
| Intimidate | A small terror halo on each pawn it scares, gone when the terror ends | `CompAbilityEffect_GiveTerror`, `Ordo_Terrified`, `Ordo_Mote_TerrorHalo` |
| Howl | A small ring on each ally it rallies, which also tells it apart from Intimidate (both use Odyssey's roar ring) | `Ordo_RallyRing` |
| Regenerate | A healing cross each time it heals | `HediffComp_Regenerate` |
| Gnaw | Wood chips, dust and a wood-chewing sound while it winds up | `Ordo_Gnawing` warm-up effecter |

`HediffComp_Overlay` is the reusable piece for debuffs. Give a hediff a mote, kept on the pawn or remade every `intervalTicks`, and optionally `whileMentalState` to end it with a mental state. The base game's motes grow several cells wide, which over a crowd fills the screen, so the mod has smaller copies (`Ordo_Mote_RallyRing`, `Ordo_Mote_TerrorHalo`).

Poison Bite has no visual after the bite (`ToxicBuildup` is the base game's hediff).

## Design

**The CSVs are the design record.** The mod doesn't read them:

- `Animals.csv`: Animal, Tags (semicolon-separated). All 107 animals; 25 are untagged.
- `Tags.csv`: Tag, Abilities, Description. All 26 tags, including ones with no ability built yet.
- Keep the UTF-8 BOM: the user edits these files in Excel.

Built tags and members, live in `AnimalTags_SillyAnimals.xml`:

| Tag | Animals | Abilities |
|---|---|---|
| Trunked | Elephant, Mastodon | Trunk Blast, Trunk Throw |
| Woodchewer | Alphabeaver | Gnaw (deliberately wood-specific and alphabeaver-only) |
| Volatile | Boomalope, Boomrat, Toxalope | Detonate. The toxalope's death action is `DeathActionWorker_ToxCloud`, so it releases a tox cloud |
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
| Prolific | Hare, Snowhare, Guinea Pig | Duplicate: targets another hare, puts both on cooldown, and creates a new hare |
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

Untagged for now, and fine as plain animals: most livestock, Deer, Caribou, Chinchilla, Squirrel, Capybara, Iguana, the three foxes (a "Trickster" tag was floated), Panda, Polar Bear, Heron, Flamingo, Megasloth, and both thrumbos (they already have Odyssey roars).

Effort and worth of the unbuilt abilities:

| Ability | Effort | Worth | Notes |
|---|---|---|---|
| Spray Musk | Low | Medium | Effect still undecided: make enemies flee, a debuff, or a stink gas cloud (`GasType.RotStink` is the easiest) |
| Leap | Very low | Low–Med | Vanilla `Verb_CastAbilityJump` with the default `PawnFlyer` |
| Quill Volley | Low | Low–Med | A spike projectile through `LaunchProjectile` or `SprayLiquid` |
| Nine Lives | Medium | Medium | A Harmony intercept on death plus a lives counter. It's a passive |
| Duplicate | Medium | Med–High | Pawn spawning, partner validation, paired cooldowns. Needs balancing so it can't be abused as free breeding |
| Death Roll | Low–Med | Low | Touch ability, stun plus damage. One animal only |
| Swoop | Low–Med | Low | `PawnFlyer_Charge` with height added and less damage |
| Call the Swarm | Medium | Low–Med | |
| Mimic Taunt | Medium | Low–Med | Forcing an enemy to retarget means overriding its AI |
| Steal / Scavenge | Med–High | Medium | Needs a new job driver |
| Burrow | High | Medium | A cheaper fake: an invisible jump plus dust effects |
| Fetch | High | High (for players) | Animals can't carry pawns in vanilla, so it needs a custom rescue job |

## Next steps

1. Spray Musk (once its effect is decided) and Leap.
2. Bigger items by user preference. Fetch and Duplicate looked most popular.
3. Add scenarios and showcase clips for anything new.

### Open questions for the user

- Small cats: Nine Lives or Pounce?
- Alligator: Death Roll, or untagged?
- Spray Musk: what effect?
- Duplicate: does the new hare spawn as a baby or an adult? Male/female only? Does a tamed pair produce a tamed hare?
- Should the alphabeaver behaviour also include manhunters chewing through walls and doors (a pathing hook)? Not built.

## Testing

**Profiles:**

- `sillyanimalthings` (Core and Odyssey) skips the 3 Fire Burst scenarios.
- `sillyanimalthings-biotech` runs all 49.
- Both have `-headless` twins.
- `sillyanimalthings-profiling` adds Dubs Performance Analyzer.
- `sillyanimalthings-showcase` is for recording.

Run the suite with `ordo/run_scenarios`. The scenarios are in `tools/BridgeTools/Scenarios/SillyAnimalThingsScenarios.cs`: one per mechanism and per case that behaves differently, plus `tags_grant_abilities`, which checks every tagged race got its abilities.

- **Suites:** one per ability (`charge`, `detonate`, ...), plus `ai`, `player`, `knockback`, `visuals` and `setup`.
- **The `visuals` suite** checks that each overlay's mote appears and goes, through `HediffComp_Overlay.LastMote`, because the game doesn't list motes with the map's other things. Use the showcase to judge how something looks.
- **Player scenarios use non-roamers** (rats, boomrats). A player-owned roamer isn't hostile to anything until it has fought, which a scenario can't rely on.
- **Headless runs take 1.4-4 s.** A windowed run is needed for anything that draws.

**The suite is known to catch bugs:**

- With four faults put in on purpose (the roamer bug, a violent Hidden Dragon verb, an error logged by strikes, and a Detonate dialog), exactly the scenarios that cover them failed.
- Run against the build before the AI rework, the 8 `ai_*` scenarios that check for a cast failed, and the 2 that check nothing is cast passed.

**Results:**

- 49/49 on `sillyanimalthings-biotech-headless` on 2026-11-13. No errors were logged except from the faults put in on purpose.
- In earlier testing, all 15 abilities passed when commanded, and all that have AI use passed for it.
- Not measured: Withdraw's armor bonus, Howl's stat bonus, Charge damage across body sizes, and Fire Burst's burning (only its cooldown is checked).

**Known flake:** `detonate_manhunter` and `combat_hidden_dragon_ambush` have each occasionally failed in a full run, with the manhunter not hurting the test colonist.

- Neither failed in 12 isolated repeats or the 4 full runs after.
- Ruled out or not explaining it: clothing, pacifism and non-combatant scoring. Drafting the real colonists may have helped.
- `combat_hidden_dragon_ambush` later failed on "the colonist was hurt", with the tiger revealed but the colonist unhurt. That would fit a bite that missed, but it isn't confirmed.
- Explosions left running by cleanup also made the next stage's manhunters fail when both shared a site. That's fixed, but it isn't known whether it explains the earlier `detonate_manhunter` failures.
- Both scenarios now report the pawns' state on failure, so read that next time it happens.

### Showcase

`tools/BridgeTools/Showcase/SillyAnimalThingsShowcase.cs` has 22 clips: one per ability, plus `ai_*` clips showing when animals use them. Record on `sillyanimalthings-showcase` with `ordo/record_showcase`, then cut with `make_showcase.ps1`. All 15 ability clips take about 95 s to record and 45 s to cut.

- Captions come from each ability's label and description.
- The Gnaw clip starts its wall worn down, because a real wall takes three bites with ten seconds between them.
- Charge targets deer: cows are bigger than a walrus, so they never moved.
- Trunk Blast only lights fires next to the cow. One further away survived the spray and set the cow alight.

### Profiling

`mod/Analyzer.xml` gives the mod its own tab in Dubs Performance Analyzer, under Modder > SillyAnimalThings. It names each method, because the installed 1.7 build of DPA only finds methods in 4 of the mod's 23 types when given `Types`, and doesn't read `NestedTypes`. DPA skips names it can't find without logging anything, so regenerate the list with `ordo/profiling_targets` after adding or renaming methods.

The first profiled run, on 2026-11-10, found nothing that costs anything every tick:

- The debuff overlays average 0.012 ms a call, and the dash trail 0.007 ms.
- The rest is one-off work when an ability lands, all under 15 ms apart from first casts and Detonate's explosion (42 ms).
- Charge's landing took 94 ms on its first cast and 2-13 ms after.

## Risks to check in game

- How often hostile elephants and walruses use their abilities, and whether hungry wild alphabeavers are too eager to eat buildings.
- Boomrat: Fire Burst goes before Detonate within 2.9 cells. Fire burst isn't protected from its own flames, so a boomrat may set itself alight (funny, but check).
- Charge damage across body sizes (goat about 16, mastodon about 44).
- Intimidate: whether `Terror` on colonists from wild geese is too harsh. In testing, one manhunting goose and cobra put two of three colonists in `Terror` within seconds.
- Withdraw: animals crawl rather than stop dead, by design. Check the armor bonus is felt.
- Kick and Poison Bite: the AI walks to any enemy within 8 cells.
- Wild animals fight back with their abilities, and predators use them on prey. Check this doesn't make hunting elephants or walruses, or wild cobras, too punishing.
- A player's pen animals (horses, cows, muffalo and other roamers) don't fight back unless they started the fight: vanilla disables threats between them and anything they haven't engaged. So trained ones rarely use abilities by themselves. Whether guarding a master gets round this isn't tested. Not changed.
- `Projectile_WaterBlast` still uses `HostileTo` when `preventFriendlyFire` is set. No trunked animal is a roamer today, but a roamer added to that tag would hit the roamer bug.
- Tag startup: the training tab lists the new trainables on `trainability: None` animals (the code says it should), and existing saves pick up the abilities.

## History

- **Hidden Dragon ended itself.** `Verb.TryCastNextBurstShot` calls `health.Notify_UsedVerb` after the cast, and verbs default to `violent = true`, so `HediffComp_RemoveOnAttack` removed the invisibility as soon as it was granted. Fixed with `violent: false`. Watch for this in any self-buff with a remove-on-attack comp.
- **Regenerate and Hidden Dragon needed Biotech.** They used `CastAbilityOnThingWithoutWeapon`, which is a Biotech job. They now use the default cast job.
- **Abilities did nothing for player-owned roamers** (Charge on muffalo, bison, goats and the like; Intimidate on geese). Roamers aren't hostile to anything until they've fought, so hostility checks failed. Fixed with `HostilityUtility.IsEnemyOf` and `CompAbilityEffect_OnlyTargetEnemies`.
- **Knockback depended on age.** Stages generated animals at random ages, and body size shrinks with youth, so a young caster was sometimes smaller than its target and knockback skipped it. `Stage.Spawn` now always makes adults.
- **`strike_kick_player` "never hurt"** was a test bug. A kick can take off a minor part, such as a tail, which leaves summary health at 100%. `Stage.Hurt` now counts any injury or missing part.
- **2026-11-13: the AI rework.** Abilities are now used wherever animals fight; see "How the AI uses abilities".
