# RimWorld 1.7 and Waylith gotchas

Behaviour found while building and testing mods here. Each one cost time once.

## Waylith (the 1.7 engine)

- The game runs on Waylith (Vulkan/GLFW on .NET 10, `.NET 10.0.15-waylithclr`), not Unity or Mono. Unity flags (`-batchmode`, `-nographics`) do nothing, and Mono-based tools (Reloader, Doorstop) don't apply. Its own flags include `-Headless`; strings next to it in `RimWorld.dll` suggest `NoFileLog`, `NoCrashDialog` and `ReadPixelsStaging`.
- Without a window, a synchronous long event never runs, because it waits for its loading screen to be drawn. Anything that queues one headless (hot reloading defs, for example) needs `alreadyDisplayed` set by hand; `ordo/long_events markDisplayed` does that.
- `rimworld/click_cell` presses at the cell but releases at the real cursor, so it never selects. Use `ordo/select`.

## Defs and XML

- Give every ability comp entry a `Class`. A bare `<li>` with only `compClass` loads as the base properties type, and `CompAbilityEffect.Props` then throws an invalid cast. Use `Class="CompProperties_AbilityEffect"` when there are no custom properties.
- `TrainableDef.enablesAbility` is `[Obsolete]` and nothing reads it. Gate an ability on training with `CompProperties_AbilityRequiresTrainable`, and add the trainable to `race.specialTrainables`.
- Abilities added to `PawnKindDef.abilities` in code are lost on a def hot reload; see [reload-and-debug.md](reload-and-debug.md).

## Pawns and AI

- **Player-owned roamers aren't hostile to anything.** `Pawn.ThreatDisabledBecauseNonAggressiveRoamer` makes a player-owned animal with `race.roamMtbDays` non-hostile until it has recently fought, so `Thing.HostileTo(Thing)` is false. In any effect a player's animal can trigger, use `HostilityUtility.IsEnemyOf`, which also checks against the caster's faction. Vanilla's `CompAbilityEffect_OnlyTargetHostiles` has the same problem.
- Enemy-faction animals flee instead of fighting. Test animal AI against manhunters.
- `JobGiver_Manhunter` only checks abilities when it picks a job, and melee jobs last 420-900 ticks, so an animal that starts out of an ability's range may rarely use it.
- The AI casts the first castable ability in its list, so an earlier long-range ability can starve a later one.
- Body size shrinks with youth. Anything that compares sizes (knockback, for example) gives random results with randomly aged pawns, so spawn adults in tests.
- Summary health can stay at 100% after a hit that removes a minor part such as a tail. Count injuries and missing parts too.

## Abilities

- `Ability.StartCooldown(0)` leaves `CanCast` false until the next tick.
- A location jump is refused when the caster is inside rock.
- Explosions spread over several ticks, and spray projectiles land one after another, so effects often aren't visible on the tick of the cast.
- Overrides of protected game methods (`Projectile.Impact`, `PawnFlyer.RespawnPawn`) must be `public override`, because the publicizer makes the base public.

## Visuals

- Motes aren't listed with the map's other things. To test that a mote appears, keep a reference to it in the mod.
- The base game's motes and rings grow several cells wide, which fills the screen over a crowd. Make smaller copies for effects that show on many pawns.
- A thing drawn in the same cell as a pawn can be hidden underneath it. Land thrown objects one cell back towards the thrower.
