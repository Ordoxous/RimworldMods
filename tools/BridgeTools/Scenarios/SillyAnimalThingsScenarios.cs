using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ordo.SillyAnimalThings;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// One scenario per mechanism and per case that behaves differently, not one per animal: the tag scenario covers which
// animals get what. Roaming animals get their own cases, because the game treats player-owned ones as peaceful.
public static class SillyAnimalThingsScenarios
{
	private const string BellySlide = "Ordo_BellySlide";

	private const string Charge = "Ordo_Charge";

	private const string Detonate = "Ordo_Detonate";

	private const string FireBurst = "Ordo_FireBurst";

	private const string Gnaw = "Ordo_Gnaw";

	private const string HiddenDragon = "Ordo_HiddenDragon";

	private const string Howl = "Ordo_Howl";

	private const string Intimidate = "Ordo_Intimidate";

	private const string Kick = "Ordo_Kick";

	private const string PoisonBite = "Ordo_PoisonBite";

	private const string Regenerate = "Ordo_Regenerate";

	private const string Spit = "Ordo_Spit";

	private const string TrunkBlast = "Ordo_TrunkBlast";

	private const string TrunkThrow = "Ordo_TrunkThrow";

	private const string Withdraw = "Ordo_Withdraw";

	private const string Rallied = "Ordo_Rallied";

	private const string Regenerating = "Ordo_Regenerating";

	private const string SpitInEyes = "Ordo_SpitInEyes";

	private const string Withdrawn = "Ordo_Withdrawn";

	public static readonly List<Scenario> All = new()
	{
		new("tags_grant_abilities", TagsGrantAbilities),
		new("gizmo_needs_training", GizmoNeedsTraining),

		new("dash_belly_slide", DashBellySlide),
		new("dash_charge_player", context => ChargeAsPlayer(context, "Walrus")),
		new("dash_charge_player_roamer", context => ChargeAsPlayer(context, "Muffalo")),
		new("dash_charge_manhunter", context => CastsAtColonist(context, "Walrus", Charge, 8, hurtsColonist: true)),

		new("strike_kick_player", StrikeKickPlayer),
		new("strike_poison_bite_player", StrikePoisonBitePlayer),
		new("strike_kick_manhunter", context => CastsAtColonist(context, "Horse", Kick, 4, hurtsColonist: true)),
		new("strike_poison_bite_manhunter", context => CastsAtColonist(context, "Rat", PoisonBite, 4, hurtsColonist: true)),

		new("roar_howl_player", RoarHowlPlayer),
		new("roar_howl_manhunter", RoarHowlManhunter),
		new("roar_intimidate_player", context => IntimidateAsPlayer(context, "Cobra")),
		new("roar_intimidate_player_roamer", context => IntimidateAsPlayer(context, "Goose")),
		new("roar_intimidate_manhunter", context => CastsAtColonist(context, "Goose", Intimidate, 5, hurtsColonist: false)),

		new("hurt_regenerate_trained", context => AutoCastsWhenHurt(context, "Wolverine", PawnMode.Trained, 0.6f, Regenerating)),
		new("hurt_regenerate_wild", context => AutoCastsWhenHurt(context, "Wolverine", PawnMode.Wild, 0.6f, Regenerating)),
		new("hurt_regenerate_untrained", HurtRegenerateUntrained),
		new("hurt_regenerate_heals", HurtRegenerateHeals),
		new("hurt_withdraw_trained", context => AutoCastsWhenHurt(context, "Tortoise", PawnMode.Trained, 0.75f, Withdrawn)),
		new("hurt_withdraw_wild", context => AutoCastsWhenHurt(context, "Tortoise", PawnMode.Wild, 0.75f, Withdrawn)),

		new("combat_hidden_dragon_player", CombatHiddenDragonPlayer),
		new("combat_hidden_dragon_ambush", CombatHiddenDragonAmbush),
		new("combat_hidden_dragon_too_close", CombatHiddenDragonTooClose),

		new("detonate_player", DetonatePlayer),
		new("detonate_manhunter", DetonateManhunter),
		new("gnaw_player", GnawPlayer),
		new("gnaw_rejects_stone", GnawRejectsStone),
		new("gnaw_hungry_wild", GnawHungryWild),
		new("gnaw_hungry_player_ignores", GnawHungryPlayerIgnores),
		new("trunk_blast", TrunkBlastPushesAndDouses),
		new("trunk_throw_at_pawn", TrunkThrowAtPawn),
		new("trunk_throw_at_cell", TrunkThrowAtCell),
		new("trunk_throw_needs_chunk", TrunkThrowNeedsChunk),
		new("spit", SpitBlinds),
		new("fire_burst_player", FireBurstPlayer, FireBurst),
		new("fire_burst_manhunter", context => CastsAtColonist(context, "LavaSnail", FireBurst, 2, hurtsColonist: false), FireBurst)
	};

	private static async Task TagsGrantAbilities(
		ScenarioContext context
	)
	{
		var tags = DefDatabase<AnimalTagDef>.AllDefsListForReading;
		context.Check("at least one tag is loaded", tags.Count > 0);

		foreach (var ability in DefDatabase<AbilityDef>.AllDefsListForReading.Where(def => def.defName.StartsWith("Ordo_")))
		{
			context.Check($"{ability.defName} belongs to a tag", tags.Any(tag => tag.abilities.Contains(ability)));
		}

		foreach (var race in tags.SelectMany(tag => tag.races).Distinct())
		{
			var kinds = DefDatabase<PawnKindDef>.AllDefsListForReading.Where(kind => kind.race == race).ToList();
			context.Check($"{race.defName} has a pawn kind", kinds.Count > 0);

			foreach (var tag in tags.Where(tag => tag.races.Contains(race)))
			{
				foreach (var ability in tag.abilities)
				{
					foreach (var kind in kinds)
					{
						context.Check($"{kind.defName} kind has {ability.defName}", kind.abilities?.Contains(ability) == true);
					}

					foreach (var requires in ability.comps.OfType<CompProperties_AbilityRequiresTrainable>())
					{
						context.Check($"{race.defName} can learn {requires.trainableDef.defName}", race.race.specialTrainables?.Contains(requires.trainableDef) == true);
					}

					if (kinds.Count > 0)
					{
						var pawn = context.Spawn(kinds[0].defName, 0, 0, PawnMode.Wild, hold: true);
						context.Check($"a spawned {race.defName} has {ability.defName}", pawn.abilities?.abilities.Any(owned => owned.def == ability) == true);
						pawn.Destroy();
					}
				}
			}
		}

		await context.Step(1);
	}

	private static async Task GizmoNeedsTraining(
		ScenarioContext context
	)
	{
		var pawn = context.Spawn("Tiger", 0, 0, PawnMode.Tamed, hold: true);
		context.Check("an untrained tamed animal has no gizmo", !HasGizmo(pawn, HiddenDragon));

		PawnTools.TrainAll(pawn);
		context.Check("a trained animal has the gizmo", HasGizmo(pawn, HiddenDragon));
		await context.Step(1);
	}

	private static bool HasGizmo(
		Pawn pawn,
		string abilityDef
	)
	{
		return pawn.abilities.GetGizmos().OfType<Command_Ability>().Any(gizmo => gizmo.Ability.def.defName == abilityDef);
	}

	private static async Task DashBellySlide(
		ScenarioContext context
	)
	{
		var penguin = context.Spawn("Penguin", -5, 0, PawnMode.Trained);
		var destination = context.Cell(5, 0);
		context.Cast(penguin, BellySlide, destination);

		await context.Until("the penguin reaches its destination", () => penguin.Spawned && penguin.Position == destination, 200);
		context.Check("the slide is on cooldown", ScenarioContext.OnCooldown(penguin, BellySlide));
	}

	private static async Task ChargeAsPlayer(
		ScenarioContext context,
		string kind
	)
	{
		var charger = context.Spawn(kind, -5, 0, PawnMode.Trained);
		var enemy = context.Spawn("Cow", 4, 0, PawnMode.Enemy, hold: true);
		var friend = context.Spawn("Cow", 3, 1, PawnMode.Trained, hold: true);
		var wild = context.Spawn("Cow", 3, -1, PawnMode.Wild, hold: true);
		var landing = context.Cell(3, 0);
		context.Cast(charger, Charge, landing);

		await context.Until("the charger lands", () => charger.Spawned && charger.Position == landing, 200);
		await context.Step(1);

		context.Check("the enemy is hurt", ScenarioContext.Hurt(enemy));
		context.Check("the enemy is staggered", ScenarioContext.Staggered(enemy));
		context.Check("the friend is unhurt", !ScenarioContext.Hurt(friend));
		context.Check("the friend isn't staggered", !ScenarioContext.Staggered(friend));
		context.Check("the wild animal is unhurt", !ScenarioContext.Hurt(wild));
	}

	// A manhunter should use the ability by itself on the colonist it's going after
	private static async Task CastsAtColonist(
		ScenarioContext context,
		string kind,
		string ability,
		int distance,
		bool hurtsColonist
	)
	{
		var colonist = context.Colonist(distance, 0);
		var manhunter = context.Spawn(kind, 0, 0, PawnMode.Manhunter);

		await context.Until($"the manhunter casts {ability}", () => ScenarioContext.OnCooldown(manhunter, ability), 300);
		if (hurtsColonist)
		{
			await context.Until("the colonist is hurt", () => ScenarioContext.Hurt(colonist), 300);
		}
	}

	private static async Task StrikeKickPlayer(
		ScenarioContext context
	)
	{
		var horse = context.Spawn("Horse", 0, 0, PawnMode.Trained);

		// Big enough to survive the kick, and no bigger than the horse, which wouldn't budge it
		var target = context.Spawn("Cow", 3, 0, PawnMode.Enemy, hold: true);
		var start = target.Position;
		context.Cast(horse, Kick, target);

		if (!await context.Until("the target is hurt", () => ScenarioContext.Hurt(target), 300))
		{
			context.Fail(ScenarioContext.Describe(target));
		}

		context.Check("the target is staggered", ScenarioContext.Staggered(target));
		context.Check("the target is knocked back", target.Position != start);
		context.Check("the kick is on cooldown", ScenarioContext.OnCooldown(horse, Kick));
	}

	// A rat, because a cobra's natural bite is venomous too and would hide a failure
	private static async Task StrikePoisonBitePlayer(
		ScenarioContext context
	)
	{
		var rat = context.Spawn("Rat", 0, 0, PawnMode.Trained);
		var target = context.Spawn("Cow", 3, 0, PawnMode.Enemy, hold: true);
		context.Cast(rat, PoisonBite, target);

		await context.Until("the target is poisoned", () => ScenarioContext.HasHediff(target, "ToxicBuildup"), 300);
		context.Check("the bite is on cooldown", ScenarioContext.OnCooldown(rat, PoisonBite));
	}

	private static async Task RoarHowlPlayer(
		ScenarioContext context
	)
	{
		var wolf = context.Spawn("Wolf_Timber", 0, 0, PawnMode.Trained);
		var ally = context.Spawn("Wolf_Timber", 5, 0, PawnMode.Trained, hold: true);
		var farAlly = context.Spawn("Wolf_Timber", -16, 0, PawnMode.Trained, hold: true);
		var enemy = context.Spawn("Cow", 0, 5, PawnMode.Enemy, hold: true);
		var wild = context.Spawn("Cow", 0, -5, PawnMode.Wild, hold: true);
		context.Cast(wolf, Howl);

		await context.Until("the caster is rallied", () => ScenarioContext.HasHediff(wolf, Rallied), 120);
		context.Check("the nearby ally is rallied", ScenarioContext.HasHediff(ally, Rallied));
		context.Check("the ally out of range isn't rallied", !ScenarioContext.HasHediff(farAlly, Rallied));
		context.Check("the enemy isn't rallied", !ScenarioContext.HasHediff(enemy, Rallied));
		context.Check("the wild animal isn't rallied", !ScenarioContext.HasHediff(wild, Rallied));
	}

	private static async Task RoarHowlManhunter(
		ScenarioContext context
	)
	{
		context.Colonist(10, 0);
		var wolf = context.Spawn("Wolf_Timber", 0, 0, PawnMode.Manhunter);
		var packmate = context.Spawn("Wolf_Timber", 0, 3, PawnMode.Manhunter);

		await context.Until("a wolf howls", () => ScenarioContext.OnCooldown(wolf, Howl) || ScenarioContext.OnCooldown(packmate, Howl), 300);
		await context.Until("the pack is rallied", () => ScenarioContext.HasHediff(wolf, Rallied) && ScenarioContext.HasHediff(packmate, Rallied), 60);
	}

	// The roar only lands 40% of the time, so with this many enemies a total miss is about one run in 2000
	private static async Task IntimidateAsPlayer(
		ScenarioContext context,
		string kind
	)
	{
		var caster = context.Spawn(kind, 0, 0, PawnMode.Trained);
		var enemies = new List<Pawn>();
		for (var i = 0; i < 15; i++)
		{
			enemies.Add(context.Spawn("Cow", -7 + i, 4, PawnMode.Enemy, hold: true));
		}

		var friend = context.Spawn("Cow", 2, -2, PawnMode.Trained, hold: true);
		var wild = context.Spawn("Cow", -2, -2, PawnMode.Wild, hold: true);
		context.Cast(caster, Intimidate);

		await context.Until("the roar goes off", () => ScenarioContext.OnCooldown(caster, Intimidate), 120);
		await context.Step(1);
		context.Check("at least one enemy is terrified", enemies.Any(ScenarioContext.Terrified));
		context.Check("the friend isn't terrified", !ScenarioContext.Terrified(friend));
		context.Check("the wild animal isn't terrified", !ScenarioContext.Terrified(wild));
	}

	private static async Task AutoCastsWhenHurt(
		ScenarioContext context,
		string kind,
		PawnMode mode,
		float belowHealth,
		string hediff
	)
	{
		var pawn = context.Spawn(kind, 0, 0, mode);
		context.HurtBelow(pawn, belowHealth);
		await context.Until($"{kind} gets {hediff}", () => ScenarioContext.HasHediff(pawn, hediff), 60);
	}

	private static async Task HurtRegenerateUntrained(
		ScenarioContext context
	)
	{
		var pawn = context.Spawn("Wolverine", 0, 0, PawnMode.Tamed);
		context.HurtBelow(pawn, 0.6f);
		await context.Step(60);
		context.Check("an untrained player animal doesn't cast it", !ScenarioContext.HasHediff(pawn, Regenerating));
	}

	private static async Task HurtRegenerateHeals(
		ScenarioContext context
	)
	{
		var pawn = context.Spawn("Wolverine", 0, 0, PawnMode.Trained);
		context.HurtBelow(pawn, 0.6f);
		var hurtHealth = pawn.health.summaryHealth.SummaryHealthPercent;
		await context.Until("the wolverine heals", () => pawn.health.summaryHealth.SummaryHealthPercent > hurtHealth + 0.05f, 300);
	}

	private static async Task CombatHiddenDragonPlayer(
		ScenarioContext context
	)
	{
		var tiger = context.Spawn("Tiger", 0, 0, PawnMode.Trained);
		context.Cast(tiger, HiddenDragon);

		await context.Until("the tiger vanishes", () => tiger.IsPsychologicallyInvisible(), 120);
		await context.Step(30);
		context.Check("the invisibility survives the cast", tiger.IsPsychologicallyInvisible());
		context.Check("it's on cooldown", ScenarioContext.OnCooldown(tiger, HiddenDragon));
	}

	private static async Task CombatHiddenDragonAmbush(
		ScenarioContext context
	)
	{
		var colonist = context.Colonist(12, 0);
		var tiger = context.Spawn("Tiger", 0, 0, PawnMode.Manhunter);

		await context.Until("the tiger vanishes", () => tiger.IsPsychologicallyInvisible(), 200);
		context.Check("it's unseen before striking", !ScenarioContext.Hurt(colonist));

		if (!await context.Until("the attack reveals the tiger", () => !tiger.IsPsychologicallyInvisible(), 900))
		{
			context.Fail($"tiger: {ScenarioContext.Describe(tiger)}; colonist: {ScenarioContext.Describe(colonist)}");
		}

		context.Check("the colonist was hurt", ScenarioContext.Hurt(colonist));
		context.Check("the cooldown keeps running", ScenarioContext.OnCooldown(tiger, HiddenDragon));
	}

	private static async Task CombatHiddenDragonTooClose(
		ScenarioContext context
	)
	{
		context.Colonist(4, 0);
		var tiger = context.Spawn("Tiger", 0, 0, PawnMode.Manhunter);

		await context.Step(120);
		context.Check("a tiger already next to its target doesn't bother hiding", !ScenarioContext.OnCooldown(tiger, HiddenDragon));
	}

	private static async Task DetonatePlayer(
		ScenarioContext context
	)
	{
		var boomalope = context.Spawn("Boomalope", 0, 0, PawnMode.Trained);
		var target = context.Spawn("Cow", 4, 0, PawnMode.Enemy, hold: true);
		context.Cast(boomalope, Detonate, target);

		await context.Until("the boomalope bursts", () => boomalope.Dead, 400);
		await context.Until("the blast reaches the target", () => ScenarioContext.Hurt(target), 120);
	}

	private static async Task DetonateManhunter(
		ScenarioContext context
	)
	{
		var colonist = context.Colonist(6, 0);
		var boomalope = context.Spawn("Boomalope", 0, 0, PawnMode.Manhunter);

		await context.Until("the boomalope bursts", () => boomalope.Dead, 500);
		if (!await context.Until("the blast burns the colonist", () => ScenarioContext.HasHediff(colonist, "Burn"), 120))
		{
			context.Fail($"boomalope: {ScenarioContext.Describe(boomalope)}; colonist: {ScenarioContext.Describe(colonist)}");
		}
	}

	private static async Task GnawPlayer(
		ScenarioContext context
	)
	{
		var beaver = context.Spawn("Alphabeaver", 0, 0, PawnMode.Trained);
		var wall = context.Place(ThingDefOf.Wall, 2, 0, ThingDefOf.WoodLog);
		var wallHealth = wall.HitPoints;
		beaver.needs.food.CurLevelPercentage = 0.3f;
		context.Cast(beaver, Gnaw, wall);

		await context.Until("the wall is damaged", () => wall.Destroyed || wall.HitPoints < wallHealth, 300);
		context.Check("the gnaw restores food", beaver.needs.food.CurLevelPercentage > 0.45f);
	}

	private static async Task GnawRejectsStone(
		ScenarioContext context
	)
	{
		var beaver = context.Spawn("Alphabeaver", 0, 0, PawnMode.Trained);
		var wall = context.Place(ThingDefOf.Wall, 2, 0, ThingDefOf.BlocksGranite);

		await context.Step(1);
		context.Check("a stone wall isn't a valid target", !ScenarioContext.Ability(beaver, Gnaw).verb.ValidateTarget(wall, false));
	}

	private static async Task GnawHungryWild(
		ScenarioContext context
	)
	{
		var beaver = context.Spawn("Alphabeaver", 0, 0, PawnMode.Wild);
		var wall = context.Place(ThingDefOf.Wall, 4, 0, ThingDefOf.WoodLog);
		var wallHealth = wall.HitPoints;
		beaver.needs.food.CurLevelPercentage = 0f;

		await context.Until("the hungry beaver gnaws the wall", () => wall.Destroyed || wall.HitPoints < wallHealth, 900);
	}

	private static async Task GnawHungryPlayerIgnores(
		ScenarioContext context
	)
	{
		var beaver = context.Spawn("Alphabeaver", 0, 0, PawnMode.Trained);
		var wall = context.Place(ThingDefOf.Wall, 4, 0, ThingDefOf.WoodLog);
		var wallHealth = wall.HitPoints;
		beaver.needs.food.CurLevelPercentage = 0f;

		await context.Step(500);
		context.Check("a player's hungry beaver leaves the wall alone", !wall.Destroyed && wall.HitPoints == wallHealth);
	}

	private static async Task TrunkBlastPushesAndDouses(
		ScenarioContext context
	)
	{
		var elephant = context.Spawn("Elephant", 0, 0, PawnMode.Trained);
		var colonist = context.Colonist(5, 0);
		var start = colonist.Position;

		// The spray lands around the target, mostly on its far side, so that's where the fire has to be.
		// Sites are cleared of plants, so it also needs something to burn.
		var fireCell = context.Cell(6, 1);
		context.Place(ThingDefOf.WoodLog, 6, 1);
		FireUtility.TryStartFireIn(fireCell, context.Map, 0.5f, null);
		context.Check("the test fire started", fireCell.GetFirstThing<Fire>(context.Map) != null);
		context.Cast(elephant, TrunkBlast, colonist);

		await context.Until("the blast pushes the target", () => colonist.Position != start, 200);
		await context.Until("the spray puts out the fire", () => fireCell.GetFirstThing<Fire>(context.Map) == null, 120);
		context.Check("the blast is on cooldown", ScenarioContext.OnCooldown(elephant, TrunkBlast));
	}

	private static async Task TrunkThrowAtPawn(
		ScenarioContext context
	)
	{
		var elephant = context.Spawn("Elephant", 0, 0, PawnMode.Trained);
		var chunk = context.Place(ThingDefOf.ChunkSlagSteel, 1, 1);
		var target = context.Spawn("Cow", 8, 0, PawnMode.Enemy, hold: true);
		context.Cast(elephant, TrunkThrow, target);

		await context.Until("the chunk is picked up", () => chunk.Destroyed, 200);
		await context.Until("the chunk hits the target", () => ScenarioContext.Hurt(target), 200);
		context.Check("the throw is on cooldown", ScenarioContext.OnCooldown(elephant, TrunkThrow));
	}

	private static async Task TrunkThrowAtCell(
		ScenarioContext context
	)
	{
		var elephant = context.Spawn("Elephant", 0, 0, PawnMode.Trained);
		var chunk = context.Place(ThingDefOf.ChunkSlagSteel, 1, 1);
		context.Cast(elephant, TrunkThrow, context.Cell(10, 0));

		await context.Until("the chunk is picked up", () => chunk.Destroyed, 200);
		await context.Until(
			"the chunk lands near the target cell",
			() => GenRadial.RadialDistinctThingsAround(context.Cell(10, 0), context.Map, 2.9f, true).Any(thing => thing.def == ThingDefOf.ChunkSlagSteel),
			200
		);
	}

	private static async Task TrunkThrowNeedsChunk(
		ScenarioContext context
	)
	{
		var elephant = context.Spawn("Elephant", 0, 0, PawnMode.Trained);

		await context.Step(1);
		context.Check("the gizmo is disabled with no chunk nearby", ScenarioContext.Ability(elephant, TrunkThrow).GizmoDisabled(out _));
	}

	private static async Task SpitBlinds(
		ScenarioContext context
	)
	{
		var alpaca = context.Spawn("Alpaca", 0, 0, PawnMode.Trained);
		var target = context.Spawn("Cow", 4, 0, PawnMode.Enemy, hold: true);
		context.Cast(alpaca, Spit, target);

		await context.Until("the target is spat on", () => ScenarioContext.HasHediff(target, SpitInEyes), 200);
		context.Check("the spit is on cooldown", ScenarioContext.OnCooldown(alpaca, Spit));
	}

	private static async Task FireBurstPlayer(
		ScenarioContext context
	)
	{
		var snail = context.Spawn("LavaSnail", 0, 0, PawnMode.Trained);
		context.Cast(snail, FireBurst);

		await context.Until("the burst goes off", () => ScenarioContext.OnCooldown(snail, FireBurst), 300);
	}
}
