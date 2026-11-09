using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using Verse;

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
		new("detonate", "Ordo_Detonate", "Boomalope", Detonate)
	};

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
		var walrus = stage.Spawn("Walrus", -8, 0, PawnMode.Trained);
		var enemies = new[]
		{
			stage.Spawn("Cow", 2, 1, PawnMode.Enemy, hold: true),
			stage.Spawn("Cow", 3, 0, PawnMode.Enemy, hold: true),
			stage.Spawn("Cow", 2, -1, PawnMode.Enemy, hold: true)
		};
		var landing = stage.Cell(2, 0);
		await stage.Beat();
		stage.Cast(walrus, "Ordo_Charge", landing);

		await stage.Until("the walrus lands", () => walrus.Spawned && walrus.Position == landing, 300);
		await stage.Step(1);
		stage.Check("the walrus hit something", enemies.Any(Stage.Hurt));
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

		// The spray lands around the target, mostly on its far side
		var fireCells = new List<IntVec3>();
		foreach (var (x, z) in new[] { (1, 1), (1, -1), (2, 0) })
		{
			stage.Place(ThingDefOf.WoodLog, x, z);
			fireCells.Add(stage.Cell(x, z));
			FireUtility.TryStartFireIn(stage.Cell(x, z), stage.Map, 0.5f, null);
		}

		await stage.Beat();
		stage.Cast(elephant, "Ordo_TrunkBlast", target);

		await stage.Until("the target is pushed back", () => target.Position != start, 200);
		await stage.Until("a fire goes out", () => fireCells.Any(cell => cell.GetFirstThing<Fire>(stage.Map) == null), 120);
		await stage.Linger();
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
