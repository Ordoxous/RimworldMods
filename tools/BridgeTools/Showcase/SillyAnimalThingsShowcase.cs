using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Showcase;

// One clip per ability, staged to make its effect obvious. The checks only decide whether a take is worth keeping: the
// scenarios are what test the mod. Stay within 12 cells across and 5 down from the origin, because the frame ends there and the
// caption covers the bottom of it.
public static class SillyAnimalThingsShowcase
{
	public static readonly List<ShowcaseClip> All = new()
	{
		new("belly_slide", "Ordo_BellySlide", "Penguin", BellySlide),
		new("charge", "Ordo_Charge", "Walrus", Charge),
		new("kick", "Ordo_Kick", "Horse", Kick),
		new("poison_bite", "Ordo_PoisonBite", "Cobra", PoisonBite),
		new("spit", "Ordo_Spit", "Alpaca", Spit),
		new("howl", "Ordo_Howl", "Wolf_Timber", Howl),
		new("intimidate", "Ordo_Intimidate", "Goose", Intimidate),
		new("hidden_dragon", "Ordo_HiddenDragon", "Tiger", HiddenDragon),
		new("regenerate", "Ordo_Regenerate", "Wolverine", Regenerate),
		new("withdraw", "Ordo_Withdraw", "Tortoise", Withdraw),
		new("gnaw", "Ordo_Gnaw", "Alphabeaver", Gnaw),
		new("trunk_blast", "Ordo_TrunkBlast", "Elephant", TrunkBlast),
		new("trunk_throw", "Ordo_TrunkThrow", "Elephant", TrunkThrow),
		new("fire_burst", "Ordo_FireBurst", "LavaSnail", FireBurst),
		new("detonate", "Ordo_Detonate", "Boomalope", Detonate),

		// How the AI uses abilities by itself. Record just these with filter "ai_".
		new("ai_hunt_hidden_dragon", "Ordo_HiddenDragon", "Tiger", HuntHiddenDragon,
			"Predators use their abilities on prey: a hunting tiger vanishes before it closes in, then carries on the hunt"),
		new("ai_hunt_poison_bite", "Ordo_PoisonBite", "Cobra", HuntPoisonBite,
			"A hunting cobra stops to sink its venom into its prey, then goes back to the hunt"),
		new("ai_fights_back", "Ordo_Kick", "Horse", FightsBack,
			"Wild animals fight back with their abilities: punch a wild horse and it kicks you away"),
		new("ai_trained_defends", "Ordo_Intimidate", "Bear_Grizzly", TrainedDefends,
			"Trained animals use what they've learned by themselves: a grizzly attacked by a wolf pack roars them into terror"),
		new("ai_charge_from_afar", "Ordo_Charge", "Muffalo", ChargeFromAfar,
			"A charging animal looks again when its ability is ready: this manhunting muffalo charges as soon as it's in range"),
		new("ai_intimidate_waits", "Ordo_Intimidate", "Goose", IntimidateWaits,
			"A manhunting goose waits until its target is within range before it roars, instead of wasting it from afar"),
		new("ai_fire_burst_first", "Ordo_FireBurst", "Boomrat", FireBurstFirst,
			"Animals choose between their abilities: up close, a boomrat sprays fire before resorting to blowing itself up")
	};

	private static async Task HuntHiddenDragon(
		ShowcaseStage stage
	)
	{
		var tiger = stage.Spawn("Tiger", -9, 0, PawnMode.Wild);
		var deer = stage.Spawn("Deer", 5, 0, PawnMode.Wild, hold: true);
		await stage.Beat();
		StartHunt(tiger, deer);

		await stage.Until("the tiger vanishes", () => tiger.IsPsychologicallyInvisible(), 200);
		await stage.Until("the tiger pounces", () => Stage.Hurt(deer), 600);
		await stage.Linger();
	}

	private static async Task HuntPoisonBite(
		ShowcaseStage stage
	)
	{
		var cobra = stage.Spawn("Cobra", -6, 0, PawnMode.Wild);
		var deer = stage.Spawn("Deer", 2, 0, PawnMode.Wild, hold: true);
		await stage.Beat();
		StartHunt(cobra, deer);

		await stage.Until("the prey is poisoned", () => Stage.HasHediff(deer, "ToxicBuildup"), 400);
		await stage.Until("the hunt carries on", () => cobra.CurJobDef == JobDefOf.PredatorHunt, 200);
		await stage.Linger();
	}

	private static void StartHunt(
		Pawn predator,
		Pawn prey
	)
	{
		var hunt = JobMaker.MakeJob(JobDefOf.PredatorHunt, prey);
		hunt.killIncappedTarget = true;
		predator.jobs.StartJob(hunt, JobCondition.InterruptForced);
	}

	// The colonist really does punch it. The threat is also set directly, because a punch alone makes a wild animal flee or
	// turn manhunter at random.
	private static async Task FightsBack(
		ShowcaseStage stage
	)
	{
		var horse = stage.Spawn("Horse", 0, 0, PawnMode.Wild);
		var colonist = stage.Colonist(-3, 0, dressed: true);
		await stage.Beat();

		var punch = JobMaker.MakeJob(JobDefOf.AttackMelee, horse);
		punch.playerForced = true;
		punch.maxNumMeleeAttacks = 1;
		colonist.jobs.StartJob(punch, JobCondition.InterruptForced);
		await stage.Until("the colonist reaches the horse", () => colonist.Position.AdjacentTo8WayOrInside(horse.Position), 200);

		horse.mindState.meleeThreat = colonist;
		horse.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
		horse.jobs.CheckForJobOverride();

		await stage.Until("the horse kicks back", () => Stage.OnCooldown(horse, "Ordo_Kick"), 200);
		await stage.Linger();
	}

	private static async Task TrainedDefends(
		ShowcaseStage stage
	)
	{
		var bear = stage.Spawn("Bear_Grizzly", 0, 0, PawnMode.Trained);
		var wolves = new[]
		{
			stage.Spawn("Wolf_Timber", 2, 0, PawnMode.Enemy, hold: true),
			stage.Spawn("Wolf_Timber", -2, 1, PawnMode.Enemy, hold: true),
			stage.Spawn("Wolf_Timber", 1, -2, PawnMode.Enemy, hold: true),
			stage.Spawn("Wolf_Timber", -1, 2, PawnMode.Enemy, hold: true),
			stage.Spawn("Wolf_Timber", 2, 2, PawnMode.Enemy, hold: true),
			stage.Spawn("Wolf_Timber", -2, -2, PawnMode.Enemy, hold: true)
		};
		await stage.Beat();

		bear.mindState.meleeThreat = wolves[0];
		bear.mindState.lastMeleeThreatHarmTick = Find.TickManager.TicksGame;
		bear.jobs.CheckForJobOverride();

		await stage.Until("the bear roars", () => Stage.OnCooldown(bear, "Ordo_Intimidate"), 200);
		await stage.Step(1);
		stage.Check("a wolf is terrified", wolves.Any(Stage.Terrified));
		foreach (var wolf in wolves)
		{
			wolf.stances.stunner.StopStun();
		}

		await stage.Linger(180);
	}

	private static async Task ChargeFromAfar(
		ShowcaseStage stage
	)
	{
		stage.Colonist(8, 0, dressed: true);
		var muffalo = stage.Spawn("Muffalo", -11, 0, PawnMode.Manhunter);

		await stage.Until("the muffalo charges", () => Stage.OnCooldown(muffalo, "Ordo_Charge"), 400);
		await stage.Until("the muffalo lands", () => muffalo.Spawned, 200);
		await stage.Linger();
	}

	private static async Task IntimidateWaits(
		ShowcaseStage stage
	)
	{
		var colonist = stage.Colonist(8, 0, dressed: true);
		var goose = stage.Spawn("Goose", -8, 0, PawnMode.Manhunter);

		await stage.Until("the goose roars", () => Stage.OnCooldown(goose, "Ordo_Intimidate"), 600);
		stage.Check("it roared in range", goose.Position.DistanceTo(colonist.Position) <= 10.5f);
		await stage.Linger();
	}

	private static async Task FireBurstFirst(
		ShowcaseStage stage
	)
	{
		// Within Fire Burst's 2.9 cells: any further and Detonate is the only ability that reaches
		stage.Colonist(2, 0, dressed: true);
		var boomrat = stage.Spawn("Boomrat", 0, 0, PawnMode.Manhunter);

		await stage.Until("the boomrat sprays fire", () => Stage.OnCooldown(boomrat, "Ordo_FireBurst") || boomrat.Dead, 300);
		stage.Check("it sprayed fire rather than detonating", !boomrat.Dead && !Stage.OnCooldown(boomrat, "Ordo_Detonate"));
		await stage.Linger(180);
	}

	private static async Task BellySlide(
		ShowcaseStage stage
	)
	{
		var penguins = new[]
		{
			stage.Spawn("Penguin", -7, 3, PawnMode.Trained),
			stage.Spawn("Penguin", -7, 0, PawnMode.Trained),
			stage.Spawn("Penguin", -7, -3, PawnMode.Trained)
		};
		var destinations = new[] { stage.Cell(7, 3), stage.Cell(7, 0), stage.Cell(7, -3) };
		await stage.Beat();

		for (var i = 0; i < penguins.Length; i++)
		{
			stage.Cast(penguins[i], "Ordo_BellySlide", destinations[i]);
		}

		await stage.Until("the penguins arrive", () => Enumerable.Range(0, penguins.Length).All(i => penguins[i].Spawned && penguins[i].Position == destinations[i]), 300);
		await stage.Linger();
	}

	private static async Task Charge(
		ShowcaseStage stage
	)
	{
		// Deer, because knockback doesn't move anything bigger than the charger, and a cow is bigger than a walrus
		var walrus = stage.Spawn("Walrus", -8, 0, PawnMode.Trained);
		var enemies = new[]
		{
			stage.Spawn("Deer", 2, 1, PawnMode.Enemy, hold: true),
			stage.Spawn("Deer", 3, 0, PawnMode.Enemy, hold: true),
			stage.Spawn("Deer", 2, -1, PawnMode.Enemy, hold: true)
		};
		var starts = enemies.Select(enemy => enemy.Position).ToList();
		var landing = stage.Cell(2, 0);
		await stage.Beat();
		stage.Cast(walrus, "Ordo_Charge", landing);

		await stage.Until("the walrus lands", () => walrus.Spawned && walrus.Position == landing, 300);
		await stage.Step(1);
		stage.Check("the walrus hit something", enemies.Any(Stage.Hurt));
		stage.Check("the herd was knocked back", Enumerable.Range(0, enemies.Length).All(i => enemies[i].Position != starts[i]));
		await stage.Linger();
	}

	private static async Task Kick(
		ShowcaseStage stage
	)
	{
		var horse = stage.Spawn("Horse", -3, 0, PawnMode.Trained);
		var target = stage.Spawn("Cow", 0, 0, PawnMode.Enemy, hold: true);
		var start = target.Position;
		await stage.Beat();
		stage.Cast(horse, "Ordo_Kick", target);

		await stage.Until("the target is knocked back", () => target.Position != start, 300);
		await stage.Linger();
	}

	private static async Task PoisonBite(
		ShowcaseStage stage
	)
	{
		var cobra = stage.Spawn("Cobra", -3, 0, PawnMode.Trained);
		var target = stage.Spawn("Cow", 0, 0, PawnMode.Enemy, hold: true);
		await stage.Beat();
		stage.Cast(cobra, "Ordo_PoisonBite", target);

		await stage.Until("the target is poisoned", () => Stage.HasHediff(target, "ToxicBuildup"), 300);
		await stage.Linger();
	}

	private static async Task Spit(
		ShowcaseStage stage
	)
	{
		var alpaca = stage.Spawn("Alpaca", -4, 0, PawnMode.Trained);
		var target = stage.Spawn("Cow", 1, 0, PawnMode.Enemy, hold: true);
		await stage.Beat();
		stage.Cast(alpaca, "Ordo_Spit", target);

		await stage.Until("the target is spat on", () => Stage.HasHediff(target, "Ordo_SpitInEyes"), 200);
		await stage.Linger();
	}

	private static async Task Howl(
		ShowcaseStage stage
	)
	{
		var leader = stage.Spawn("Wolf_Timber", 0, 0, PawnMode.Trained);
		var pack = new[]
		{
			stage.Spawn("Wolf_Timber", -4, 3, PawnMode.Trained, hold: true),
			stage.Spawn("Wolf_Timber", 4, 3, PawnMode.Trained, hold: true),
			stage.Spawn("Wolf_Timber", -4, -3, PawnMode.Trained, hold: true),
			stage.Spawn("Wolf_Timber", 4, -3, PawnMode.Trained, hold: true)
		};
		await stage.Beat();
		stage.Cast(leader, "Ordo_Howl");

		await stage.Until("the pack is rallied", () => pack.All(wolf => Stage.HasHediff(wolf, "Ordo_Rallied")), 120);
		await stage.Linger();
	}

	// Only stunned for long enough to stand around for the roar, so that the terrified ones can run for it
	private static async Task Intimidate(
		ShowcaseStage stage
	)
	{
		var goose = stage.Spawn("Goose", 0, 0, PawnMode.Trained);
		var cows = new List<Pawn>();
		for (var i = 0; i < 10; i++)
		{
			var angle = i * Math.PI / 5;
			var cow = stage.Spawn("Cow", (int)Math.Round(6 * Math.Cos(angle)), (int)Math.Round(4 * Math.Sin(angle)), PawnMode.Enemy);
			cow.stances.stunner.StunFor(80, null, false, false);
			cows.Add(cow);
		}

		await stage.Step(20);
		stage.Cast(goose, "Ordo_Intimidate");

		await stage.Until("the cows panic", () => cows.Any(Stage.Terrified), 180);
		await stage.Linger(180);
	}

	private static async Task HiddenDragon(
		ShowcaseStage stage
	)
	{
		stage.Colonist(6, 0, dressed: true);
		var tiger = stage.Spawn("Tiger", -6, 0, PawnMode.Manhunter);

		await stage.Until("the tiger vanishes", () => tiger.IsPsychologicallyInvisible(), 200);
		await stage.Until("the tiger strikes", () => !tiger.IsPsychologicallyInvisible(), 900);
		await stage.Linger();
	}

	private static async Task Regenerate(
		ShowcaseStage stage
	)
	{
		var wolverine = stage.Spawn("Wolverine", 0, 0, PawnMode.Trained);
		await stage.Beat();
		stage.HurtBelow(wolverine, 0.6f);
		var hurtHealth = wolverine.health.summaryHealth.SummaryHealthPercent;

		await stage.Until("the wolverine starts healing", () => Stage.HasHediff(wolverine, "Ordo_Regenerating"), 60);
		await stage.Until("the wolverine heals", () => wolverine.health.summaryHealth.SummaryHealthPercent > hurtHealth + 0.05f, 300);
		await stage.Linger();
	}

	private static async Task Withdraw(
		ShowcaseStage stage
	)
	{
		var tortoise = stage.Spawn("Tortoise", 0, 0, PawnMode.Trained);
		await stage.Beat();
		stage.HurtBelow(tortoise, 0.75f);

		await stage.Until("the tortoise withdraws", () => Stage.HasHediff(tortoise, "Ordo_Withdrawn"), 60);
		await stage.Linger(180);
	}

	// A wall takes three bites and ten seconds between each, so this one starts out worn down enough for one bite to finish it
	private static async Task Gnaw(
		ShowcaseStage stage
	)
	{
		var beaver = stage.Spawn("Alphabeaver", -4, 0, PawnMode.Trained);
		for (var z = -4; z <= 4; z++)
		{
			if (z != 0)
			{
				stage.Place(ThingDefOf.Wall, 0, z, ThingDefOf.WoodLog);
			}
		}

		var wall = stage.Place(ThingDefOf.Wall, 0, 0, ThingDefOf.WoodLog);
		wall.HitPoints = 60;
		await stage.Beat();
		stage.Cast(beaver, "Ordo_Gnaw", wall);

		await stage.Until("the wall is eaten through", () => wall.Destroyed, 400);
		await stage.Linger();
	}

	private static async Task TrunkBlast(
		ShowcaseStage stage
	)
	{
		var elephant = stage.Spawn("Elephant", -6, 0, PawnMode.Trained);
		var target = stage.Spawn("Cow", 0, 0, PawnMode.Enemy, hold: true);
		var start = target.Position;

		// The spray lands around the target, mostly on its far side. Every fire has to go out: the knockback shoves the cow
		// to (1, 0), and a fire left next to it there sets it alight.
		var fireCells = new List<IntVec3>();
		foreach (var (x, z) in new[] { (1, 1), (1, -1) })
		{
			stage.Place(ThingDefOf.WoodLog, x, z);
			fireCells.Add(stage.Cell(x, z));
			FireUtility.TryStartFireIn(stage.Cell(x, z), stage.Map, 0.5f, null);
		}

		await stage.Beat();
		stage.Cast(elephant, "Ordo_TrunkBlast", target);

		await stage.Until("the target is pushed back", () => target.Position != start, 200);
		await stage.Until("the fires go out", () => fireCells.All(cell => cell.GetFirstThing<Fire>(stage.Map) == null), 120);
		await stage.Linger();
		stage.Check("the cow isn't burning", !target.IsBurning());
	}

	private static async Task TrunkThrow(
		ShowcaseStage stage
	)
	{
		var elephant = stage.Spawn("Elephant", -8, 0, PawnMode.Trained);
		var chunk = stage.Place(ThingDefOf.ChunkSlagSteel, -7, 1);
		var target = stage.Spawn("Cow", 6, 0, PawnMode.Enemy, hold: true);
		await stage.Beat();
		stage.Cast(elephant, "Ordo_TrunkThrow", target);

		await stage.Until("the chunk is picked up", () => chunk.Destroyed, 200);
		await stage.Until("the chunk hits", () => Stage.Hurt(target), 200);
		await stage.Linger();
	}

	private static async Task FireBurst(
		ShowcaseStage stage
	)
	{
		var snail = stage.Spawn("LavaSnail", 0, 0, PawnMode.Trained);
		await stage.Beat();
		stage.Cast(snail, "Ordo_FireBurst");

		await stage.Until("the burst goes off", () => Stage.OnCooldown(snail, "Ordo_FireBurst"), 300);
		await stage.Linger(180);
	}

	private static async Task Detonate(
		ShowcaseStage stage
	)
	{
		var boomalope = stage.Spawn("Boomalope", -8, 0, PawnMode.Trained);
		var target = stage.Spawn("Cow", 3, 0, PawnMode.Enemy, hold: true);
		var others = new[]
		{
			stage.Spawn("Cow", 4, 2, PawnMode.Enemy, hold: true),
			stage.Spawn("Cow", 4, -2, PawnMode.Enemy, hold: true)
		};
		await stage.Beat();
		stage.Cast(boomalope, "Ordo_Detonate", target);

		await stage.Until("the boomalope bursts", () => boomalope.Dead, 500);
		await stage.Until("the blast reaches the herd", () => Stage.Hurt(target) || others.Any(Stage.Hurt), 120);
		await stage.Linger(180);
	}
}
