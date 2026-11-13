using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MU;
using Ordo.MechUpgradesUi;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Scenarios;

// Most of the mod is decisions over Mechanoid Upgrades' data, so most scenarios call those decisions directly. The "ai" suite
// goes through the think tree, and "end_to_end" lets a mech make the whole trip. Upgrades are picked at runtime from MU's own
// defs by MU's own rules, so the scenarios don't depend on MU's content staying the same.
public static class MechUpgradesUiScenarios
{
	private const string MechKind = "Mech_Lifter";

	private const string UpgraderDef = "MU_MechUpgrader";

	private const string StorageDef = "MU_UpgradesStorage";

	// Further than a storage's 11.9-cell link range from the upgrader at the origin, and still on the site
	private const int UnlinkedX = 14;

	// Long enough to walk across the site, without fabrication
	private const int TripTicks = 1500;

	// Suites: "setup" for the patches, "policy" for the database and filters, "operations" for what an upgrader would do,
	// "ai" for the think tree's trips, "player" for manual inserts and "settings" for the mod's options
	public static readonly List<Scenario> All = new()
	{
		new("patches_applied", "setup", PatchesApplied),
		new("policy_database", "policy", PolicyDatabase),
		new("policy_keep_and_want", "policy", KeepAndWant),

		new("operations_point_budget", "operations", PointBudget),
		new("operations_exclusions", "operations", Exclusions),
		new("operations_removals_run_first", "operations", RemovalsRunFirst),
		new("operations_linked_storage_only", "operations", LinkedStorageOnly),

		new("ai_no_policy_no_trip", "ai", NoPolicyNoTrip),
		new("ai_unpowered_no_trip", "ai", UnpoweredNoTrip),
		new("ai_default_for_unassigned", "ai settings", DefaultForUnassigned),
		new("ai_interval", "ai settings", Interval),
		new("ai_end_to_end", "ai end_to_end", EndToEnd),
		new("ai_stock_gone_on_arrival", "ai", StockGoneOnArrival),

		new("player_insert_dialog", "player settings", context => ManualInsert(context, showDialog: true)),
		new("player_insert_no_dialog", "player settings", context => ManualInsert(context, showDialog: false))
	};

	private static Task PatchesApplied(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		context.Check($"a player mech's comp is {site.Mech.AllComps.OfType<CompUpgradableMechanoid>().FirstOrDefault()?.GetType().Name}", site.Comp != null);
		context.Check($"{UpgraderDef} spawned as {site.Upgrader.GetType().Name}", site.Upgrader is Building_AutoMechUpgrader);
		context.Check($"{UpgraderDef} is labelled {site.Upgrader.def.label}", site.Upgrader.def.label == "Auto mech upgrader");

		var columns = DefDatabase<PawnTableDef>.GetNamed("Mechs").columns.Select(column => column.defName).ToList();
		foreach (var column in new[] { "MechUpgradesUi_UpgradePolicy", "MechUpgradesUi_Upgrades", "MechUpgradesUi_Work" })
		{
			context.Check($"the Mechs table has {column}", columns.Contains(column));
		}

		context.Check("the Mechanoid think tree has JobGiver_OptimizeUpgrades", Nodes(DefDatabase<ThinkTreeDef>.GetNamed("Mechanoid").thinkRoot).Any(node => node is JobGiver_OptimizeUpgrades));
		return Task.CompletedTask;
	}

	private static Task PolicyDatabase(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var database = Database;
		var before = database.GetPolicies().ToList();
		try
		{
			var first = database.DefaultPolicy();
			var created = database.TryCreate();
			context.Check("a new policy goes to the end of the list", database.GetPolicies().Last() == created);
			context.Check("the default policy is the first", database.DefaultPolicy() == database.GetPolicies()[0] && database.DefaultPolicy() == first);

			database.SetDefaultPolicy(created);
			context.Check("SetDefaultPolicy moves the policy to the front", database.GetPolicies()[0] == created && database.DefaultPolicy() == created);
			context.Check("SetDefaultPolicy keeps the other policies", database.GetPolicies().Contains(first));

			site.Comp.CurrentUpgradePolicy = created;
			context.Check("deleting a policy a living mech uses is refused", !database.TryDelete(created).Accepted && database.GetPolicies().Contains(created));

			site.Mech.Kill(null);
			context.Check("the mech died", site.Mech.Dead);
			var deleted = database.TryDelete(created);
			context.Check($"deleting a policy only a dead mech uses is accepted: {deleted.Reason}", deleted.Accepted && !database.GetPolicies().Contains(created));
			context.Check("deleting clears the dead mech's policy", site.Comp.CurrentUpgradePolicy == null);
		}
		finally
		{
			Restore(database, before);
		}

		return Task.CompletedTask;
	}

	// A policy allowing A and not B, for a mech with B: B is dropped and A is wanted, in the same trip
	private static Task KeepAndWant(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		var installed = site.Comp.AddUpgrade(b);
		using var policy = new TempPolicy(site, a);
		var keep = site.Comp.UpgradesToKeep();
		var want = site.Comp.WantedUpgrades(keep);
		context.Check($"{b.defName} isn't kept: keep=[{Names(keep)}]", !keep.Contains(installed));
		context.Check($"{a.defName} is wanted: want=[{string.Join(",", want.Select(def => def.defName))}]", want.Contains(a));
		context.Check($"{b.defName} isn't wanted", !want.Contains(b));
		return Task.CompletedTask;
	}

	private static Task PointBudget(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var max = MechUpgradeUtility.MaxUpgradePoints(site.Mech);
		var candidates = Compatible(site.Mech)
			.Where(def => def.upgradePoints <= max)
			.Aggregate(
				new List<MechUpgradeDef>(),
				(chosen, def) =>
				{
					if (def.CanAdd(site.Mech.def, chosen.Select(chosenDef => MechUpgradeUtility.MakeUpgrade(chosenDef)).ToList()).Accepted)
					{
						chosen.Add(def);
					}

					return chosen;
				}
			);
		if (candidates.Sum(def => def.upgradePoints) <= max)
		{
			context.Fail($"no set of compatible upgrades for {MechKind} is worth more than its {max} points");
			return Task.CompletedTask;
		}

		Stock(site.Storage, candidates);
		using var policy = new TempPolicy(site, candidates.ToArray());
		var operations = Operations(site);
		var added = operations.Where(operation => operation.type == UpgradeOperationType.Add).ToList();
		var points = added.Sum(operation => operation.upgrade.def.upgradePoints);
		context.Check($"adds {points} points, within the mech's {max}: [{Describe(operations)}]", points <= max);
		context.Check($"adds something: [{Describe(operations)}]", added.Count > 0);

		// It fills greedily in defName order, so whatever was skipped must not have fitted
		var left = max - points;
		var skipped = candidates.Where(def => added.All(operation => operation.upgrade.def != def)).ToList();
		var wasted = skipped.Where(def => def.upgradePoints <= left).Select(def => def.defName).ToList();
		context.Check($"skipped [{string.Join(",", wasted)}] though {left} points were left", wasted.Count == 0);
		return Task.CompletedTask;
	}

	private static Task Exclusions(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var compatible = Compatible(site.Mech).ToList();
		var pair = compatible
			.SelectMany(a => compatible.Where(b => string.CompareOrdinal(a.defName, b.defName) < 0 && a.exclusionTags.Intersect(b.exclusionTags).Any()).Select(b => (a, b)))
			.FirstOrDefault(pair => pair.a.upgradePoints + pair.b.upgradePoints <= MechUpgradeUtility.MaxUpgradePoints(site.Mech));
		if (pair.a == null)
		{
			context.Fail($"no two upgrades for {MechKind} share an exclusion tag and fit together");
			return Task.CompletedTask;
		}

		Stock(site.Storage, new[] { pair.a, pair.b });
		using var policy = new TempPolicy(site, pair.a, pair.b);
		var operations = Operations(site);
		var added = operations.Count(operation => operation.type == UpgradeOperationType.Add);
		context.Check($"only one of {pair.a.defName} and {pair.b.defName}, which share a tag, is added: [{Describe(operations)}]", added == 1);
		return Task.CompletedTask;
	}

	// MU runs operations from the end of the list, so the removals must be last for them to run first
	private static Task RemovalsRunFirst(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		site.Comp.AddUpgrade(b);
		Stock(site.Storage, new[] { a });
		using var policy = new TempPolicy(site, a);
		var operations = Operations(site);
		context.Check($"one addition and one removal: [{Describe(operations)}]", operations.Count == 2);
		context.Check($"the removal is last: [{Describe(operations)}]", operations.LastOrDefault()?.type == UpgradeOperationType.Remove && operations.LastOrDefault()?.upgrade.def == b);
		return Task.CompletedTask;
	}

	private static Task LinkedStorageOnly(
		ScenarioContext context
	)
	{
		var site = Site.Set(context, linkedStorage: false);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		Stock(site.Storage, new[] { a });
		var linked = site.Upgrader.TryGetComp<CompAffectedByFacilities>().LinkedFacilitiesListForReading;
		context.Check($"the far storage isn't linked: linked=[{string.Join(",", linked.Select(thing => thing.def.defName))}]", !linked.Contains(site.Storage));
		using var policy = new TempPolicy(site, a);
		var operations = Operations(site);
		context.Check($"nothing is offered from an unlinked storage: [{Describe(operations)}]", operations.Count == 0);
		return Task.CompletedTask;
	}

	private static async Task NoPolicyNoTrip(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		var installed = site.Comp.AddUpgrade(b);
		Stock(site.Storage, new[] { a });
		await WithSettings(
			false,
			null,
			async () =>
			{
				Due(site.Mech);
				await NoTrip(context, site);
				context.Check($"the unmanaged mech keeps its upgrades: [{Names(site.Comp.upgrades)}]", site.Comp.upgrades.SequenceEqual(new[] { installed }));
			}
		);
	}

	private static async Task UnpoweredNoTrip(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		Stock(site.Storage, new[] { a });
		Stage.Power(site.Upgrader, false);
		using var policy = new TempPolicy(site, a);

		// Asked directly as well: a trip to an unpowered upgrader would be dropped by the enter job at once, so the think tree
		// alone can't see whether the job giver checked
		Due(site.Mech);
		context.Check("no trip is offered to an unpowered upgrader", new JobGiver_OptimizeUpgrades().TryGiveJob(site.Mech) == null);
		Due(site.Mech);
		await NoTrip(context, site);
	}

	private static async Task DefaultForUnassigned(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		Stock(site.Storage, new[] { a });
		var database = Database;

		// Made before the snapshot if there was none, so it survives the restore
		database.DefaultPolicy();
		var before = database.GetPolicies().ToList();
		var defaultFilter = new ThingFilter();
		defaultFilter.CopyAllowancesFrom(database.DefaultPolicy().Filter);
		try
		{
			// The default policy is whatever is first, so allow A on it for the scenario and put it back afterwards
			database.DefaultPolicy().Filter.SetDisallowAll();
			database.DefaultPolicy().Filter.SetAllow(a.linkedThingDef, true);
			await WithSettings(
				true,
				null,
				async () =>
				{
					context.Check("the unassigned mech acts on the default policy", site.Comp.CurrentUpgradePolicy == null && site.Comp.EffectiveUpgradePolicy == database.DefaultPolicy());
					Due(site.Mech);
					await context.Until($"the unassigned mech heads for the upgrader: {Stage.Describe(site.Mech)}", () => HeadingIn(site), TripTicks);
				}
			);
		}
		finally
		{
			Restore(database, before);
			database.DefaultPolicy().Filter.CopyAllowancesFrom(defaultFilter);
		}
	}

	// Called directly, because the think tree would send the mech straight off and the point is what happens to the next check
	private static Task Interval(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		Stock(site.Storage, new[] { a });
		using var policy = new TempPolicy(site, a);
		var giver = new JobGiver_OptimizeUpgrades();
		Due(site.Mech);
		var now = Find.TickManager.TicksGame;
		var first = giver.TryGiveJob(site.Mech);
		context.Check("a due mech with something to gain gets a trip", first?.def == JobDefOf.EnterBuilding && first.targetA.Thing == site.Upgrader);

		var interval = MechUpgradesUiMod.Settings.PolicyCheckIntervalHours * GenDate.TicksPerHour;
		var next = site.Mech.mindState.nextApparelOptimizeTick;
		context.Check($"the next check is {next - now} ticks away, within 20% of {interval}", next - now >= (int)(interval * 0.8f) && next - now <= (int)(interval * 1.2f));
		context.Check("no second trip before the next check", giver.TryGiveJob(site.Mech) == null);

		site.Mech.mindState.nextApparelOptimizeTick = now;
		context.Check("a trip again once the check is due", giver.TryGiveJob(site.Mech) != null);
		return Task.CompletedTask;
	}

	// The one scenario that relies on MU doing the work: the mech walks over, goes in with no dialog, swaps the unwanted upgrade
	// for the wanted one, and comes out
	private static async Task EndToEnd(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		site.Comp.AddUpgrade(b);
		Stock(site.Storage, new[] { a });
		using var policy = new TempPolicy(site, a);
		Due(site.Mech);
		if (!await context.Until($"the mech goes into the upgrader: {Stage.Describe(site.Mech)}", () => site.Upgrader.Occupant == site.Mech, TripTicks))
		{
			return;
		}

		context.Check("no dialog opened", Find.WindowStack.WindowOfType<Dialog_ConfigureUpgrades>() == null);
		context.Check($"the upgrader is working: {site.Upgrader.State}", site.Upgrader.State == UpgraderState.Working);
		context.Check($"the item left the storage: [{Names(site.StorageComp.Upgrades)}]", site.StorageComp.Upgrades.All(upgrade => upgrade.def != a));

		var fabrication = 2500 * (a.upgradePoints + b.upgradePoints);
		if (!await context.Until("the mech comes out", () => site.Mech.Spawned, fabrication + 500, stepSize: 250))
		{
			return;
		}

		context.Check($"the mech has {a.defName} and not {b.defName}: [{Names(site.Comp.upgrades)}]", site.Comp.upgrades.Any(upgrade => upgrade.def == a) && site.Comp.upgrades.All(upgrade => upgrade.def != b));
		context.Check($"the removed {b.defName} went back into storage: [{Names(site.StorageComp.Upgrades)}]", site.StorageComp.Upgrades.Any(upgrade => upgrade.def == b));
		context.Check("the upgrader has nothing left to do", site.Upgrader.State == UpgraderState.Inactive);
	}

	// The stock runs out while the mech is on its way, as when another mech gets there first. The trip has nothing left to do, so
	// the mech shouldn't go in and wait for a mechanitor behind MU's dialog.
	private static async Task StockGoneOnArrival(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		Stock(site.Storage, new[] { a });
		using var policy = new TempPolicy(site, a);
		Due(site.Mech);
		if (!await context.Until($"the mech sets off: {Stage.Describe(site.Mech)}", () => HeadingIn(site), TripTicks))
		{
			return;
		}

		site.StorageComp.innerContainer.ClearAndDestroyContents();
		if (!await context.Until($"the trip ends: {Stage.Describe(site.Mech)}", () => site.Upgrader.Occupant == site.Mech || site.Mech.CurJobDef != JobDefOf.EnterBuilding, TripTicks))
		{
			return;
		}

		context.Check("no dialog opened", Find.WindowStack.WindowOfType<Dialog_ConfigureUpgrades>() == null);
		context.Check($"the mech didn't go in: {site.Upgrader.State}", site.Upgrader.Occupant != site.Mech);
	}

	// A player-ordered insert, as MU's float menu option and gizmo make: MU's dialog when the setting is on, the policy otherwise
	private static async Task ManualInsert(
		ScenarioContext context,
		bool showDialog
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return;
		}

		Stock(site.Storage, new[] { a });
		using var policy = new TempPolicy(site, a);

		// Not due, so the mech's own check can't be what sends it
		site.Mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
		await WithSettings(
			null,
			showDialog,
			async () =>
			{
				site.Upgrader.OrderForceTarget(site.Mech);
				context.Check($"the order is player-forced: {Stage.Describe(site.Mech)}", site.Mech.CurJob is { playerForced: true } && site.Mech.CurJobDef == JobDefOf.EnterBuilding);
				if (!await context.Until($"the mech goes into the upgrader: {Stage.Describe(site.Mech)}", () => site.Upgrader.Occupant == site.Mech, TripTicks))
				{
					return;
				}

				var dialog = Find.WindowStack.WindowOfType<Dialog_ConfigureUpgrades>();
				dialog?.Close(false);
				if (showDialog)
				{
					context.Check("MU's dialog opened", dialog != null);
					context.Check($"nothing was applied: {site.Upgrader.State}", site.Upgrader.State == UpgraderState.WaitingForMechanitor);
				}
				else
				{
					context.Check("no dialog opened", dialog == null);
					context.Check($"the policy was applied: {site.Upgrader.State}", site.Upgrader.State == UpgraderState.Working);
				}
			}
		);
	}

	private static WorldComponent_UpgradeDatabase Database => Find.World.GetComponent<WorldComponent_UpgradeDatabase>();

	private static IEnumerable<ThinkNode> Nodes(
		ThinkNode node
	)
	{
		yield return node;
		foreach (var child in node.subNodes.SelectMany(Nodes))
		{
			yield return child;
		}
	}

	// Upgrades that come as items and that MU lets the mech have, in a fixed order
	private static IEnumerable<MechUpgradeDef> Compatible(
		Pawn mech
	)
	{
		return DefDatabase<MechUpgradeDef>.AllDefs
			.Where(def => def.linkedThingDef?.GetCompProperties<CompProperties_MechUpgrade>()?.upgradeDef == def)
			.Where(def => def.CanAdd(mech.def).Accepted)
			.OrderBy(def => def.defName);
	}

	// Two upgrades the mech can have together, that each fit on their own
	private static (MechUpgradeDef, MechUpgradeDef) TwoCompatible(
		Stage context,
		Pawn mech
	)
	{
		var max = MechUpgradeUtility.MaxUpgradePoints(mech);
		var fitting = Compatible(mech).Where(def => def.upgradePoints <= max).ToList();
		foreach (var a in fitting)
		{
			foreach (var b in fitting.Where(b => b != a && a.upgradePoints + b.upgradePoints <= max && a.CanAdd(mech.def, new List<MechUpgrade> { MechUpgradeUtility.MakeUpgrade(b) }).Accepted))
			{
				return (a, b);
			}
		}

		context.Fail($"no two upgrades fit {MechKind}'s {max} points together");
		return (null, null);
	}

	private static void Stock(
		Thing storage,
		IEnumerable<MechUpgradeDef> upgrades
	)
	{
		var container = storage.TryGetComp<CompUpgradesStorage>().innerContainer;
		foreach (var def in upgrades)
		{
			container.TryAdd(ThingMaker.MakeThing(def.linkedThingDef));
		}
	}

	private static List<MechUpgradeOperation> Operations(
		Site site
	)
	{
		var keep = site.Comp.UpgradesToKeep();
		return site.Upgrader.WantedOperations(site.Mech, site.Comp.upgrades, keep, site.Comp.WantedUpgrades(keep));
	}

	private static void Due(
		Pawn mech
	)
	{
		mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;
	}

	private static bool HeadingIn(
		Site site
	)
	{
		return site.Upgrader.Occupant == site.Mech || (site.Mech.CurJobDef == JobDefOf.EnterBuilding && site.Mech.CurJob.targetA.Thing == site.Upgrader);
	}

	private static async Task NoTrip(
		ScenarioContext context,
		Site site
	)
	{
		for (var elapsed = 0; elapsed < TripTicks / 3; elapsed += 50)
		{
			if (HeadingIn(site))
			{
				context.Fail($"the mech set off for the upgrader: {Stage.Describe(site.Mech)}");
				return;
			}

			await context.Step(50);
		}
	}

	// Settings are static and saved to the profile's config, so always put them back
	private static async Task WithSettings(
		bool? useDefaultForUnassigned,
		bool? showDialogOnManualInsert,
		Func<Task> run
	)
	{
		var settings = MechUpgradesUiMod.Settings;
		var (useDefault, showDialog) = (settings.UseDefaultPolicyForUnassigned, settings.ShowDialogOnManualInsert);
		try
		{
			settings.UseDefaultPolicyForUnassigned = useDefaultForUnassigned ?? useDefault;
			settings.ShowDialogOnManualInsert = showDialogOnManualInsert ?? showDialog;
			await run();
		}
		finally
		{
			settings.UseDefaultPolicyForUnassigned = useDefault;
			settings.ShowDialogOnManualInsert = showDialog;
		}
	}

	// The policy list lives in the world, and the test save is reused, so leave it as it was
	private static void Restore(
		WorldComponent_UpgradeDatabase database,
		List<UpgradePolicy> before
	)
	{
		var policies = database.GetPolicies();
		policies.Clear();
		policies.AddRange(before);
	}

	private static string Names(
		IEnumerable<MechUpgrade> upgrades
	)
	{
		return string.Join(",", upgrades.Select(upgrade => upgrade.def.defName));
	}

	private static string Describe(
		IEnumerable<MechUpgradeOperation> operations
	)
	{
		return string.Join(", ", operations.Select(operation => $"{operation.type} {operation.upgrade.def.defName}"));
	}

	// A policy allowing only the given upgrades, assigned to the site's mech, and gone from the database afterwards
	private sealed class TempPolicy : IDisposable
	{
		private readonly Site _site;

		private readonly List<UpgradePolicy> _before;

		public TempPolicy(
			Site site,
			params MechUpgradeDef[] allowed
		)
		{
			_site = site;
			_before = Database.GetPolicies().ToList();
			var policy = Database.TryCreate();
			policy.Filter.SetDisallowAll();
			foreach (var def in allowed)
			{
				policy.Filter.SetAllow(def.linkedThingDef, true);
			}

			site.Comp.CurrentUpgradePolicy = policy;
		}

		public void Dispose()
		{
			_site.Comp.CurrentUpgradePolicy = null;
			Restore(Database, _before);
		}
	}

	// An upgrader facing south at the origin, a storage beside it (or out of range), a drafted mechanitor and their lifter, which
	// starts with no upgrades
	private sealed class Site
	{
		public Building_AutoMechUpgrader Upgrader;

		public Thing Storage;

		public CompUpgradesStorage StorageComp;

		public Pawn Mechanitor;

		public Pawn Mech;

		public CompUpgradableMechWithPolicy Comp;

		public static Site Set(
			ScenarioContext context,
			bool linkedStorage = true
		)
		{
			var upgrader = context.Build(ThingDef.Named(UpgraderDef), 0, 0, Rot4.South, powered: true);
			var storage = context.Build(ThingDef.Named(StorageDef), linkedStorage ? 4 : UnlinkedX, 0);
			var mechanitor = context.Mechanitor(-8, -6);
			var mech = context.ControlledMech(MechKind, mechanitor, -6, -6);
			var comp = mech.GetComp<CompUpgradableMechWithPolicy>();

			// Generated mechs can come with upgrades
			foreach (var upgrade in comp?.upgrades.ToList() ?? new List<MechUpgrade>())
			{
				comp.RemoveUpgrade(upgrade);
			}

			// Wait for a check to be made due, so mechs don't set off before the scenario is ready
			mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
			return new Site
			{
				Upgrader = upgrader as Building_AutoMechUpgrader,
				Storage = storage,
				StorageComp = storage.TryGetComp<CompUpgradesStorage>(),
				Mechanitor = mechanitor,
				Mech = mech,
				Comp = comp
			};
		}
	}
}
