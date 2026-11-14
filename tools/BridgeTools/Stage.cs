using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

public enum PawnMode
{
	Wild,

	// Owned by the player, but with no trainables learned
	Tamed,

	Trained,

	Manhunter,

	// In a hostile faction. Animals like this flee rather than fight, so stun them to use them as targets
	Enemy
}

public static class StageSites
{
	// Half the width of the clear ground a stage gets, centred on its origin. The showcase films all of its width.
	public const int Radius = 16;

	private const int SearchStride = 6;

	// Leaves the stages alone on the map. An empty colony would otherwise get events, such as the Stranger in Black who
	// comes to rescue it, and any pawn left around could stray into a stage. The storyteller comes back when the save is loaded.
	public static void ClearOfVisitors(
		Map map
	)
	{
		Find.Storyteller.storytellerComps.Clear();
		Find.Storyteller.incidentQueue.Clear();

		foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
		{
			pawn.Destroy();
		}
	}

	public static List<IntVec3> FindAndClear(
		Map map
	)
	{
		var sites = new List<IntVec3>();
		for (var x = Radius; x < map.Size.x - Radius; x += SearchStride)
		{
			for (var z = Radius; z < map.Size.z - Radius; z += SearchStride)
			{
				var origin = new IntVec3(x, 0, z);
				if (sites.All(site => Math.Max(Math.Abs(site.x - x), Math.Abs(site.z - z)) > Radius * 2)
				    && IsUsable(origin, map))
				{
					Clear(origin, map);
					sites.Add(origin);
				}
			}
		}

		return sites;
	}

	private static bool IsUsable(
		IntVec3 origin,
		Map map
	)
	{
		return CellRect.CenteredOn(origin, Radius).Cells.All(
			cell => cell.InBounds(map)
			        && !cell.Fogged(map)
			        && !cell.Roofed(map)
			        && cell.GetTerrain(map).passability != Traversability.Impassable
			        && cell.GetEdifice(map)?.def.building.isNaturalRock != true
		);
	}

	// Stages run on a throwaway copy of the save, so anything in the way can just go
	private static void Clear(
		IntVec3 origin,
		Map map
	)
	{
		foreach (var thing in CellRect.CenteredOn(origin, Radius).Cells.SelectMany(cell => cell.GetThingList(map).ToList()).Distinct().ToList())
		{
			if (thing is not Pawn && thing.def.destroyable)
			{
				thing.Destroy();
			}
		}
	}
}

// Sets up a site, passes time and notes what went wrong. Tests and the showcase differ in how time passes.
public abstract class Stage
{
	private const int HoldTicks = 5000;

	private const int FlyerWaitTicks = 200;

	private const int StepSize = 5;

	public readonly Map Map;

	public readonly List<string> Failures = new();

	protected readonly IRimBridgeContext Bridge;

	protected readonly IntVec3 Origin;

	protected readonly CancellationToken CancellationToken;

	private readonly List<Thing> _spawned = new();

	protected Stage(
		IRimBridgeContext bridge,
		Map map,
		IntVec3 origin,
		CancellationToken cancellationToken
	)
	{
		Bridge = bridge;
		Map = map;
		Origin = origin;
		CancellationToken = cancellationToken;
	}

	public abstract Task Step(
		int ticks
	);

	public IntVec3 Cell(
		int x,
		int z
	)
	{
		return Origin + new IntVec3(x, 0, z);
	}

	public Pawn Spawn(
		string kind,
		int x,
		int z,
		PawnMode mode,
		bool hold = false
	)
	{
		// Always an adult. Body size shrinks with youth, and knockback skips anything bigger than the caster, so a young caster
		// would make whether a target moves come down to chance.
		var kindDef = DefDatabase<PawnKindDef>.GetNamed(kind);
		var adultAge = kindDef.RaceProps.lifeStageAges.Last().minAge + 0.5f;
		var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kindDef, fixedBiologicalAge: adultAge, fixedChronologicalAge: adultAge));

		// Generated pawns can arrive with old injuries, and health has to start at 100% to spot new ones
		pawn.health.RemoveAllHediffs();
		GenSpawn.Spawn(pawn, Cell(x, z), Map);
		_spawned.Add(pawn);

		switch (mode)
		{
			case PawnMode.Tamed:
				pawn.SetFaction(Faction.OfPlayer);
				break;
			case PawnMode.Trained:
				pawn.SetFaction(Faction.OfPlayer);
				PawnTools.TrainAll(pawn);
				break;
			case PawnMode.Manhunter:
				pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true);
				break;
			case PawnMode.Enemy:
				pawn.SetFaction(Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: true));
				break;
		}

		if (hold)
		{
			pawn.stances.stunner.StunFor(HoldTicks, null, false, false);
		}

		return pawn;
	}

	// A drafted colonist, who stays put and is what manhunters go after. Unarmed so they don't win the fight, and by
	// default unclothed because apparel can absorb a hit and leave no injury to check for.
	public Pawn Colonist(
		int x,
		int z,
		bool dressed = false
	)
	{
		var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
		pawn.health.RemoveAllHediffs();
		pawn.equipment?.DestroyAllEquipment();
		if (!dressed)
		{
			pawn.apparel?.DestroyAll();
		}

		GenSpawn.Spawn(pawn, Cell(x, z), Map);
		_spawned.Add(pawn);
		pawn.drafter.Drafted = true;
		return pawn;
	}

	public Thing Place(
		ThingDef def,
		int x,
		int z,
		ThingDef stuff = null,
		int stackCount = 1
	)
	{
		var thing = ThingMaker.MakeThing(def, stuff);
		thing.stackCount = Math.Min(stackCount, def.stackLimit);
		if (def.CanHaveFaction)
		{
			thing.SetFaction(Faction.OfPlayer);
		}

		GenSpawn.Spawn(thing, Cell(x, z), Map);
		_spawned.Add(thing);
		return thing;
	}

	// A colonist who can do the given work and does nothing else, undrafted so they go and do it. Generated colonists can be
	// incapable of a work type, so this keeps generating until one is capable.
	public Pawn Worker(
		int x,
		int z,
		params WorkTypeDef[] work
	)
	{
		for (var attempt = 0; attempt < 20; attempt++)
		{
			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			if (work.Any(pawn.WorkTypeIsDisabled))
			{
				pawn.Discard(true);
				continue;
			}

			pawn.health.RemoveAllHediffs();
			pawn.equipment?.DestroyAllEquipment();
			GenSpawn.Spawn(pawn, Cell(x, z), Map);
			_spawned.Add(pawn);
			pawn.workSettings.EnableAndInitialize();
			foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
			{
				pawn.workSettings.SetPriority(workType, work.Contains(workType) ? 1 : 0);
			}

			return pawn;
		}

		throw new InvalidOperationException($"No colonist capable of {string.Join(", ", work.Select(workType => workType.defName))} in 20 tries");
	}

	// A blueprint the player placed, as the Architect places it. Floors take no stuff.
	public Blueprint_Build Blueprint(
		BuildableDef def,
		int x,
		int z,
		ThingDef stuff = null,
		Rot4 rotation = default
	)
	{
		var blueprint = GenConstruct.PlaceBlueprintForBuild(def, Cell(x, z), Map, rotation, Faction.OfPlayer, stuff);
		_spawned.Add(blueprint);
		return blueprint;
	}

	// Replaces the selection, as clicking and shift-clicking would. CleanUp clears it again.
	public static void Select(
		params Thing[] things
	)
	{
		Find.Selector.ClearSelection();
		foreach (var thing in things)
		{
			Find.Selector.Select(thing, playSound: false, forceDesignatorDeselect: false);
		}
	}

	// The gizmo grid's buttons for the current selection, in the grid's order: the selection's own gizmos and the reverse
	// designators (such as Cancel) that apply to it, by Order. Gizmos that group with one another show as one button, the first.
	public static List<Gizmo> SelectionGizmos()
	{
		var shown = new List<Gizmo>();
		foreach (var selectable in Find.Selector.SelectedObjects.OfType<ISelectable>())
		{
			var reverse = selectable is Thing thing
				? Find.ReverseDesignatorDatabase.AllDesignators.Select(designator => (Gizmo)designator.CreateReverseDesignationGizmo(thing)).Where(gizmo => gizmo != null)
				: Enumerable.Empty<Gizmo>();
			foreach (var gizmo in reverse.Concat(selectable.GetGizmos()).Where(gizmo => gizmo.Visible))
			{
				// A command that isn't groupable says so in GroupsWith
				if (!shown.Any(other => other.GroupsWith(gizmo)))
				{
					shown.Add(gizmo);
				}
			}
		}

		// Stable, as the grid's sort is
		return shown.OrderBy(gizmo => gizmo.Order).ToList();
	}

	// Clicks a gizmo that opens a float menu and returns the menu, or fails if it's disabled or opens none. Close the menu, or
	// pick from it with Choose: the runner fails a scenario that leaves one open.
	public FloatMenu OpenMenu(
		Command_Action command
	)
	{
		if (command.Disabled)
		{
			Fail($"{command.Label} is disabled: {command.disabledReason}");
			return null;
		}

		var before = Find.WindowStack.Windows.OfType<FloatMenu>().ToList();
		command.action();
		var menu = Find.WindowStack.Windows.OfType<FloatMenu>().LastOrDefault(window => !before.Contains(window));
		if (menu == null)
		{
			Fail($"{command.Label} opened no float menu");
		}

		return menu;
	}

	public static List<string> MenuLabels(
		FloatMenu menu
	)
	{
		return menu?.options.Select(option => option.Label).ToList() ?? new List<string>();
	}

	// Picks an option by label as a click would, then closes the menu
	public void Choose(
		FloatMenu menu,
		string label
	)
	{
		if (menu == null)
		{
			return;
		}

		var option = menu.options.Find(candidate => candidate.Label == label);
		if (option == null)
		{
			Fail($"no menu option \"{label}\" in [{string.Join(", ", MenuLabels(menu))}]");
		}
		else if (option.Disabled)
		{
			Fail($"menu option \"{label}\" is disabled");
		}
		else
		{
			option.Chosen(false, menu);
		}

		if (Find.WindowStack.IsOpen(menu))
		{
			menu.Close(false);
		}
	}

	// A player-owned building, facing the given way. Facilities link to what's in range as they spawn, so place the building
	// they serve first.
	public Building Build(
		ThingDef def,
		int x,
		int z,
		Rot4 rotation = default,
		bool powered = false
	)
	{
		var building = (Building)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
		building.SetFaction(Faction.OfPlayer);
		GenSpawn.Spawn(building, Cell(x, z), Map, rotation);
		_spawned.Add(building);
		if (powered)
		{
			Power(building, true);
		}

		return building;
	}

	// A building on no power net keeps whatever power state it's given, so there's no need for a generator and conduits
	public static void Power(
		Thing building,
		bool on
	)
	{
		var power = building.TryGetComp<CompPowerTrader>()
		            ?? throw new InvalidOperationException($"{building.def.defName} takes no power");
		power.PowerOn = on;
	}

	// A drafted colonist with a mechlink, to oversee mechs. Drafted so they stay put: mechs stay controlled wherever their
	// overseer is, and only drafted orders need command range.
	public Pawn Mechanitor(
		int x,
		int z
	)
	{
		var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
		pawn.health.RemoveAllHediffs();

		// Installed before spawning, which skips the "mechlink installed" letter
		pawn.health.AddHediff(HediffDefOf.MechlinkImplant, pawn.health.hediffSet.GetBrain());
		GenSpawn.Spawn(pawn, Cell(x, z), Map);
		_spawned.Add(pawn);
		pawn.drafter.Drafted = true;
		return pawn;
	}

	// A player mech overseen by the mechanitor, so it's player-controlled and thinks with the colony mech think tree. Full of
	// energy, so it doesn't go to recharge first.
	public Pawn ControlledMech(
		string kind,
		Pawn mechanitor,
		int x,
		int z
	)
	{
		var kindDef = DefDatabase<PawnKindDef>.GetNamed(kind);
		var mech = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kindDef, Faction.OfPlayer));
		mech.health.RemoveAllHediffs();
		GenSpawn.Spawn(mech, Cell(x, z), Map);
		_spawned.Add(mech);
		mechanitor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
		if (mech.needs?.energy is { } energy)
		{
			energy.CurLevelPercentage = 1f;
		}

		Check($"{mech.LabelShort} is controlled by {mechanitor.LabelShort}", mech.IsColonyMechPlayerControlled);
		return mech;
	}

	public static Ability Ability(
		Pawn pawn,
		string defName
	)
	{
		return pawn.abilities?.abilities.Find(ability => ability.def.defName == defName)
		       ?? throw new InvalidOperationException($"{pawn.def.defName} has no ability {defName}");
	}

	// The same entry points as the ability's gizmo
	public void Cast(
		Pawn pawn,
		string abilityDef,
		LocalTargetInfo target = default
	)
	{
		var ability = Ability(pawn, abilityDef);
		if (!ability.CanCast.Accepted)
		{
			Fail($"{abilityDef} can't be cast: {ability.CanCast.Reason}");
			return;
		}

		if (!ability.def.targetRequired)
		{
			ability.QueueCastingJob(pawn, pawn);
			return;
		}

		if (!ability.verb.ValidateTarget(target, false))
		{
			Fail($"{abilityDef} rejected its target");
			return;
		}

		ability.verb.OrderForceTarget(target);
	}

	public static bool OnCooldown(
		Pawn pawn,
		string abilityDef
	)
	{
		return Ability(pawn, abilityDef).CooldownTicksRemaining > 0;
	}

	public static bool HasHediff(
		Pawn pawn,
		string hediffDef
	)
	{
		return pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed(hediffDef));
	}

	// Any injury counts, not just a drop in summary health: a hit that takes off a minor part, such as a tail, leaves summary health
	// at 100%. Spawned pawns start with no hediffs, so any injury is new.
	public static bool Hurt(
		Pawn pawn
	)
	{
		return pawn.Dead
		       || pawn.health.summaryHealth.SummaryHealthPercent < 1f
		       || pawn.health.hediffSet.hediffs.Any(hediff => hediff is Hediff_Injury or Hediff_MissingPart);
	}

	public static bool Staggered(
		Pawn pawn
	)
	{
		// A dead pawn has no stances
		return pawn.stances?.stagger?.Staggered == true;
	}

	public static bool Terrified(
		Pawn pawn
	)
	{
		return pawn.MentalStateDef == MentalStateDefOf.Terror;
	}

	// Enough to work out what a pawn was up to from a failure message alone
	public static string Describe(
		Pawn pawn
	)
	{
		var hediffs = string.Join(",", pawn.health.hediffSet.hediffs.Select(hediff => hediff.def.defName));
		var target = pawn.CurJob?.targetA.Thing?.LabelShort ?? pawn.CurJob?.targetA.Cell.ToString();
		return $"{pawn.LabelShort} at {pawn.PositionHeld} dead={pawn.Dead} job={pawn.CurJobDef?.defName} target={target} mental={pawn.MentalStateDef?.defName} hediffs=[{hediffs}]";
	}

	// Injures a pawn in small hits until it's just below a fraction of its health, as a fight would
	public void HurtBelow(
		Pawn pawn,
		float health
	)
	{
		for (var hit = 0; hit < 40 && !pawn.Dead && pawn.health.summaryHealth.SummaryHealthPercent >= health; hit++)
		{
			pawn.TakeDamage(new DamageInfo(DamageDefOf.Cut, 4f));
		}

		Check($"{pawn.def.defName} was hurt below {health:P0}", !pawn.Dead && pawn.health.summaryHealth.SummaryHealthPercent < health);
	}

	// Steps until the condition holds, failing if it never does
	public async Task<bool> Until(
		string description,
		Func<bool> condition,
		int maxTicks,
		int stepSize = StepSize
	)
	{
		for (var elapsed = 0; elapsed < maxTicks; elapsed += stepSize)
		{
			if (condition())
			{
				return true;
			}

			await Step(stepSize);
		}

		if (condition())
		{
			return true;
		}

		Fail($"never happened within {maxTicks} ticks: {description}");
		return false;
	}

	public void Check(
		string description,
		bool passed
	)
	{
		if (!passed)
		{
			Fail(description);
		}
	}

	public void Fail(
		string message
	)
	{
		Failures.Add(message);
	}

	// Passes time directly, so waiting for things to land isn't part of what a stage records
	public virtual async Task CleanUp()
	{
		Find.Selector.ClearSelection();

		var rect = CellRect.CenteredOn(Origin, StageSites.Radius + 4).ClipInsideMap(Map);
		for (var elapsed = 0; elapsed < FlyerWaitTicks && Map.listerThings.AllThings.Any(thing => thing is PawnFlyer); elapsed += StepSize)
		{
			await Bridge.Game.StepTicksAsync(StepSize, null, CancellationToken);
		}

		foreach (var thing in _spawned.Where(thing => !thing.Destroyed))
		{
			if (thing is Pawn { Dead: true } dead)
			{
				if (dead.Corpse is { Destroyed: false } corpse)
				{
					corpse.Destroy();
				}

				continue;
			}

			thing.Destroy();
		}

		foreach (var thing in rect.Cells.SelectMany(cell => cell.GetThingList(Map).ToList()).Distinct().ToList())
		{
			// An explosion takes several ticks to spread, and would otherwise reach whatever the next stage puts here
			// Blueprints can be replaced by new ones, and frames are what a delivered blueprint becomes, so neither is in what was spawned
			if (thing is Fire or Projectile or Corpse or Explosion or RimWorld.Blueprint or Frame || thing.def.category is ThingCategory.Item or ThingCategory.Filth)
			{
				thing.Destroy();
			}
		}
	}
}
