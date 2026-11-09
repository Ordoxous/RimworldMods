using System;
using System.Collections.Generic;
using System.Linq;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

// Pawn inspection and control that the built-in bridge tools don't cover
public sealed class PawnTools
{
	[Tool("ordo/list_pawns", Description = "List spawned pawns on the current map with their abilities")]
	public static object ListPawns(
		[ToolParameter(Description = "Only pawns whose race defName contains this")] string race = null
	)
	{
		var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned
			.Where(pawn => race == null || pawn.def.defName.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0)
			.Select(pawn => new
			{
				id = pawn.GetUniqueLoadID(),
				race = pawn.def.defName,
				faction = pawn.Faction?.Name,
				position = pawn.Position.ToString(),
				abilities = pawn.abilities?.abilities.Select(ability => ability.def.defName).ToList()
			})
			.ToList();
		return new { success = true, mainThread = UnityData.IsInMainThread, pawns };
	}

	[Tool("ordo/spawn", Description = "Spawn a pawn kind at a cell as wild, trained (player-owned, every trainable learned) or manhunter, returning its id")]
	public static object Spawn(
		string kind,
		int x,
		int z,
		[ToolParameter(Description = "wild, trained, manhunter or enemy (hostile faction, so it fights through the normal combat AI)")] string mode = "wild"
	)
	{
		var cell = new IntVec3(x, 0, z);
		if (!cell.Walkable(Find.CurrentMap))
		{
			return new { success = false, message = $"{cell} is not walkable" };
		}

		var pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind));
		GenSpawn.Spawn(pawn, cell, Find.CurrentMap);
		switch (mode)
		{
			case "wild":
				break;
			case "trained":
				pawn.SetFaction(Faction.OfPlayer);
				Train(pawn.GetUniqueLoadID());
				break;
			case "manhunter":
				MakeManhunter(pawn.GetUniqueLoadID());
				break;
			case "enemy":
				pawn.SetFaction(Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: true));
				break;
			default:
				throw new ArgumentException($"Unknown mode {mode}");
		}

		return new
		{
			success = true,
			id = pawn.GetUniqueLoadID(),
			abilities = pawn.abilities?.abilities.Select(ability => ability.def.defName).ToList()
		};
	}

	[Tool("ordo/pawn_state", Description = "Read a pawn's abilities, hediffs, health, job, mental state and trainables")]
	public static object PawnState(
		string pawnId
	)
	{
		var pawn = Resolve(pawnId);
		return new
		{
			success = true,
			id = pawn.GetUniqueLoadID(),
			race = pawn.def.defName,
			faction = pawn.Faction?.Name,
			dead = pawn.Dead,
			spawned = pawn.Spawned,
			downed = pawn.Downed,
			position = pawn.Spawned ? pawn.Position.ToString() : null,
			health = pawn.health.summaryHealth.SummaryHealthPercent,
			job = pawn.CurJobDef?.defName,
			mentalState = pawn.MentalStateDef?.defName,
			stunned = pawn.stances?.stunner?.Stunned,
			staggered = pawn.stances?.stagger?.Staggered,
			invisible = pawn.IsPsychologicallyInvisible(),
			hediffs = pawn.health.hediffSet.hediffs.Select(hediff => hediff.def.defName).ToList(),
			learned = pawn.training == null
				? new List<string>()
				: DefDatabase<TrainableDef>.AllDefs.Where(def => pawn.training.HasLearned(def)).Select(def => def.defName).ToList(),
			abilities = pawn.abilities?.abilities.Select(ability => new
			{
				def = ability.def.defName,
				canCast = ability.CanCast.Accepted,
				reason = ability.CanCast.Reason,
				cooldownTicks = ability.CooldownTicksRemaining
			}).ToList()
		};
	}

	[Tool("ordo/select", Description = "Select any pawn, including ones the built-in select_pawn rejects")]
	public static object Select(
		string pawnId
	)
	{
		var pawn = Resolve(pawnId);
		Find.Selector.ClearSelection();
		Find.Selector.Select(pawn);
		return new { success = true, selected = Find.Selector.SelectedPawns.Count };
	}

	[Tool("ordo/train", Description = "Instantly teach a tamed animal every trainable its race can learn")]
	public static object Train(
		string pawnId
	)
	{
		return new { success = true, learned = TrainAll(Resolve(pawnId)) };
	}

	public static List<string> TrainAll(
		Pawn pawn
	)
	{
		var learned = new List<string>();
		foreach (var def in DefDatabase<TrainableDef>.AllDefs)
		{
			if (pawn.training.CanAssignToTrain(def).Accepted)
			{
				pawn.training.Train(def, null, true);
				learned.Add(def.defName);
			}
		}

		return learned;
	}

	[Tool("ordo/make_manhunter", Description = "Send an animal into a permanent manhunter state")]
	public static object MakeManhunter(
		string pawnId
	)
	{
		var pawn = Resolve(pawnId);
		var started = pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
		return new { success = started, mentalState = pawn.MentalStateDef?.defName };
	}

	[Tool("ordo/damage", Description = "Deal damage to a pawn, for testing abilities that react to being hurt")]
	public static object Damage(
		string pawnId,
		float amount,
		string damageDef = "Blunt"
	)
	{
		var pawn = Resolve(pawnId);
		var before = pawn.health.summaryHealth.SummaryHealthPercent;
		pawn.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed(damageDef), amount));
		return new { success = true, before, after = pawn.health.summaryHealth.SummaryHealthPercent };
	}

	[Tool("ordo/ability_targets", Description = "For an area ability cast on the caster's own cell, show which pawns its target checks accept and how often each effect comp passes over 1000 rolls")]
	public static object AbilityTargets(
		string pawnId,
		string ability
	)
	{
		var pawn = Resolve(pawnId);
		var found = pawn.abilities.abilities.Find(candidate => candidate.def.defName == ability);
		var targets = GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, found.def.EffectRadius, true)
			.OfType<Pawn>()
			.Select(other => new
			{
				id = other.GetUniqueLoadID(),
				hostile = pawn.HostileTo(other),
				canTarget = found.verb.targetParams.CanTarget(other),
				hearing = other.health.capacities.GetLevel(PawnCapacityDefOf.Hearing),
				passRates = found.EffectComps.Select(comp => new
				{
					comp = comp.GetType().Name,
					rate = Enumerable.Range(0, 1000).Count(_ => comp.Valid((LocalTargetInfo)other, false)) / 1000f
				}).ToList()
			})
			.ToList();
		return new
		{
			success = true,
			radius = found.def.EffectRadius,
			hasAreaOfEffect = found.def.HasAreaOfEffect,
			canUseAoeToGetTargets = found.def.canUseAoeToGetTargets,
			targets
		};
	}

	[Tool("ordo/things_at", Description = "List the things in a cell with the ids other tools take")]
	public static object ThingsAt(
		int x,
		int z
	)
	{
		var things = new IntVec3(x, 0, z).GetThingList(Find.CurrentMap)
			.Where(thing => thing.def.category != ThingCategory.Mote)
			.Select(thing => new { id = thing.GetUniqueLoadID(), def = thing.def.defName, stuff = thing.Stuff?.defName, hitPoints = thing.HitPoints })
			.ToList();
		return new { success = true, things };
	}

	[Tool("ordo/set_food", Description = "Set a pawn's food need, for testing hunger behaviour")]
	public static object SetFood(
		string pawnId,
		[ToolParameter(Description = "Fraction of a full stomach, from 0 to 1")] float level
	)
	{
		var food = Resolve(pawnId).needs.food;
		food.CurLevelPercentage = level;
		return new { success = true, level = food.CurLevelPercentage, category = food.CurCategory.ToString() };
	}

	[Tool("ordo/cast", Description = "Order a pawn to cast one of its abilities through its normal job, skipping any confirmation dialog")]
	public static object Cast(
		string pawnId,
		string ability,
		[ToolParameter(Description = "Target pawn or thing id. Without this or a cell, the pawn targets itself")] string targetId = null,
		int x = -1,
		int z = -1,
		bool resetCooldown = true
	)
	{
		var pawn = Resolve(pawnId);
		var found = pawn.abilities?.abilities.Find(candidate => candidate.def.defName == ability);
		if (found == null)
		{
			return new { success = false, message = $"{pawn.def.defName} has no ability {ability}" };
		}

		// Not StartCooldown(0): that leaves the ability on cooldown until the next tick
		if (resetCooldown)
		{
			found.cooldownEndTick = 0;
		}

		// The gizmo casts these straight away, without validating the pawn as a target
		if (!found.def.targetRequired)
		{
			if (!found.CanCast.Accepted)
			{
				return new { success = false, message = found.CanCast.Reason };
			}

			found.QueueCastingJob(pawn, pawn);
			return new { success = true, job = pawn.CurJobDef?.defName };
		}

		var target = targetId != null
			? new LocalTargetInfo(ResolveThing(targetId))
			: x >= 0
				? new LocalTargetInfo(new IntVec3(x, 0, z))
				: new LocalTargetInfo(pawn);
		var canCast = found.CanCast;
		var valid = found.verb.ValidateTarget(target, false);
		if (!canCast.Accepted || !valid)
		{
			return new { success = false, message = canCast.Reason, canCast = canCast.Accepted, valid };
		}

		// The same entry point as the gizmo, since verbs like jumps issue their own job from here
		found.verb.OrderForceTarget(target);
		return new { success = true, job = pawn.CurJobDef?.defName };
	}

	private static Pawn Resolve(
		string pawnId
	)
	{
		return ResolveThing(pawnId) as Pawn ?? throw new InvalidOperationException($"{pawnId} is not a pawn");
	}

	private static Thing ResolveThing(
		string thingId
	)
	{
		return Find.CurrentMap.listerThings.AllThings.Find(thing => thing.GetUniqueLoadID() == thingId)
		       ?? throw new InvalidOperationException($"No spawned thing with id {thingId}");
	}
}
