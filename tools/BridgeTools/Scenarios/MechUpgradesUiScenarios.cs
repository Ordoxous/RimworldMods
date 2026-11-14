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
		new("player_insert_no_dialog", "player settings", context => ManualInsert(context, showDialog: false)),

		new("ui_policy_dialog", "ui", PolicyDialog),
		new("ui_preview_matches_operations", "ui", PreviewMatchesOperations),
		new("ui_preview_skips", "ui", PreviewSkips),
		new("ui_load_upgrade_set", "ui", LoadUpgradeSet),
		new("ui_mech_gizmos", "ui settings", MechGizmos),
		new("ui_upgrades_status", "ui", UpgradesStatus),
		new("ui_upgrader_inspect", "ui", UpgraderInspect),
		new("ui_upgrader_no_storage", "ui", UpgraderNoStorage),
		new("ui_status_unusable_upgrader", "ui settings", UnusableUpgrader),
		new("ui_alert_not_in_stock", "ui settings", NotInStockAlert),
		new("ui_dialog_defaults", "ui", DialogDefaults),
		new("ui_use_default_confirmation", "ui", UseDefaultConfirmation)
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

	// The dialog is only constructed, not opened: what's checked is what it starts on and what it hides
	private static Task PolicyDialog(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var before = Database.GetPolicies().ToList();
		try
		{
			var fromHeader = new Dialog_ManageUpgradePolicies(null, null);
			context.Check("opened from the column header, it starts on the default policy", fromHeader.SelectedPolicy != null && fromHeader.SelectedPolicy == Database.DefaultPolicy());

			var other = Database.TryCreate();
			site.Comp.CurrentUpgradePolicy = other;
			var fromMech = new Dialog_ManageUpgradePolicies(site.Comp.CurrentUpgradePolicy, site.Comp);
			context.Check("opened from a mech, it starts on the mech's policy", fromMech.SelectedPolicy == other);
			site.Comp.CurrentUpgradePolicy = null;

			var hidden = Dialog_ManageUpgradePolicies.HiddenSpecialFilters;
			var upgrades = ThingCategoryDef.Named("MU_Upgrades").ThisAndChildCategoryDefs.ToList();
			var tierFilters = DefDatabase<SpecialThingFilterDef>.AllDefs.Where(filter => upgrades.Contains(filter.parentCategory)).ToList();
			context.Check($"MU has special filters on upgrades to hide: [{string.Join(",", tierFilters.Select(filter => filter.defName))}]", tierFilters.Count > 0);
			context.Check($"every special filter on upgrades is hidden: [{string.Join(",", hidden.Select(filter => filter.defName))}]", tierFilters.All(hidden.Contains));
			context.Check("no other special filter is hidden", hidden.All(tierFilters.Contains));
		}
		finally
		{
			site.Comp.CurrentUpgradePolicy = null;
			Restore(Database, before);
		}

		return Task.CompletedTask;
	}

	// With everything the policy allows in stock, the preview must pick what the upgrader would
	private static Task PreviewMatchesOperations(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var max = MechUpgradeUtility.MaxUpgradePoints(site.Mech);
		Stock(site.Storage, Compatible(site.Mech));

		// The storage may not take them all, so allow only what it holds
		var allowed = site.StorageComp.Upgrades.Select(upgrade => upgrade.def).Distinct().ToArray();
		using var policy = new TempPolicy(site, allowed);

		var preview = Dialog_ManageUpgradePolicies.Preview(site.Comp.CurrentUpgradePolicy, site.Mech.def, max, new List<SkippedUpgrade>())
			.Select(upgrade => upgrade.def.defName)
			.ToList();
		var added = Operations(site)
			.Where(operation => operation.type == UpgradeOperationType.Add)
			.Select(operation => operation.upgrade.def.defName)
			.ToList();
		context.Check($"the preview [{string.Join(",", preview)}] is what the upgrader adds [{string.Join(",", added)}]", preview.Count > 0 && preview.SequenceEqual(added));
		return Task.CompletedTask;
	}

	private static Task PreviewSkips(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var compatible = Compatible(site.Mech).ToList();
		var pair = compatible
			.SelectMany(a => compatible.Where(b => string.CompareOrdinal(a.defName, b.defName) < 0 && a.exclusionTags.Intersect(b.exclusionTags).Any()).Select(b => (a, b)))
			.FirstOrDefault();
		if (pair.a == null)
		{
			context.Fail($"no two upgrades for {MechKind} share an exclusion tag");
			return Task.CompletedTask;
		}

		using var policy = new TempPolicy(site, pair.a, pair.b);
		var upgradePolicy = site.Comp.CurrentUpgradePolicy;

		var skipped = new List<SkippedUpgrade>();
		var gets = Dialog_ManageUpgradePolicies.Preview(upgradePolicy, site.Mech.def, null, skipped);
		context.Check($"with no point limit, it gets the first of {pair.a.defName} and {pair.b.defName}, which share a tag: [{Names(gets)}]", gets.Count == 1 && gets[0].def == pair.a);
		context.Check($"the second is skipped with a reason: [{string.Join(",", skipped.Select(skip => $"{skip.Upgrade.def.defName}: {skip.Reason}"))}]", skipped.Count == 1 && skipped[0].Upgrade.def == pair.b && !skipped[0].Reason.NullOrEmpty());

		skipped.Clear();
		gets = Dialog_ManageUpgradePolicies.Preview(upgradePolicy, site.Mech.def, 0, skipped);
		var noPoints = "MU_Upgrades_Ui_Skip_NoPoints".Translate().ToString();
		context.Check($"with no points, it gets nothing: [{Names(gets)}]", gets.Count == 0);
		context.Check($"and both are skipped for points: [{string.Join(",", skipped.Select(skip => skip.Reason))}]", skipped.Count == 2 && skipped.All(skip => skip.Reason == noPoints));

		// Upgrades the mech type can't have at all aren't listed, as they're meant for other mechs
		var wrongMech = DefDatabase<MechUpgradeDef>.AllDefs.FirstOrDefault(def => def.linkedThingDef != null && UpgradePlanner.UpgradeDefOf(def.linkedThingDef) == def && !def.CanAdd(site.Mech.def).Accepted);
		if (wrongMech != null)
		{
			upgradePolicy.Filter.SetAllow(wrongMech.linkedThingDef, true);
			skipped.Clear();
			gets = Dialog_ManageUpgradePolicies.Preview(upgradePolicy, site.Mech.def, null, skipped);
			context.Check($"{wrongMech.defName}, which a {MechKind} can't have, isn't listed", gets.All(upgrade => upgrade.def != wrongMech) && skipped.All(skip => skip.Upgrade.def != wrongMech));
		}

		return Task.CompletedTask;
	}

	private static Task LoadUpgradeSet(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		var other = Compatible(site.Mech).First(def => def != a && def != b);
		using var policy = new TempPolicy(site, other);
		var upgradePolicy = site.Comp.CurrentUpgradePolicy;
		upgradePolicy.LoadFrom(new UpgradeSet { name = "scenario", upgrades = new List<MechUpgradeDef> { a, b, null } });
		var allowed = upgradePolicy.AllowedUpgradeDefs.ToList();
		context.Check($"the policy allows exactly {a.defName} and {b.defName}: [{string.Join(",", allowed.Select(def => def.defName))}]", allowed.Count == 2 && allowed.Contains(a) && allowed.Contains(b));
		return Task.CompletedTask;
	}

	private static async Task MechGizmos(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		await WithSettings(
			useDefaultForUnassigned: false,
			showDialogOnManualInsert: null,
			() =>
			{
				var gizmos = site.Comp.CompGetGizmosExtra().ToList();
				context.Check("a controlled mech has the policy gizmo", gizmos.OfType<Command_UpgradePolicy>().Count() == 1);
				context.Check("with no policy, there's no check-now gizmo", CheckNow(gizmos) == null);

				var (a, _) = TwoCompatible(context, site.Mech);
				if (a == null)
				{
					return Task.CompletedTask;
				}

				using var policy = new TempPolicy(site, a);
				gizmos = site.Comp.CompGetGizmosExtra().ToList();
				var label = gizmos.OfType<Command_UpgradePolicy>().FirstOrDefault()?.Label;
				context.Check($"the policy gizmo names the policy: {label}", label != null && label.Contains(site.Comp.CurrentUpgradePolicy.label));

				var checkNow = CheckNow(gizmos);
				context.Check("with a policy, there's a check-now gizmo", checkNow != null);
				var notInStock = "MU_Upgrades_Ui_Gizmo_CheckNow_NotInStock".Translate().ToString();
				context.Check($"with {a.defName} not in stock, it's disabled and says so: {checkNow?.disabledReason}", checkNow is { Disabled: true } && checkNow.disabledReason == notInStock);

				Stock(site.Storage, new[] { a });
				UpgradeStatus.Invalidate();
				var enabled = CheckNow(site.Comp.CompGetGizmosExtra().ToList());
				context.Check($"with it in stock, it's enabled: {enabled?.disabledReason}", enabled is { Disabled: false });
				site.Mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
				checkNow?.action();
				context.Check($"check now makes the check due: next={site.Mech.mindState.nextApparelOptimizeTick}, now={Find.TickManager.TicksGame}", site.Mech.mindState.nextApparelOptimizeTick <= Find.TickManager.TicksGame);
				return Task.CompletedTask;
			}
		);

		static Command_Action CheckNow(
			List<Gizmo> gizmos
		)
		{
			var label = "MU_Upgrades_Ui_Gizmo_CheckNow".Translate().ToString();
			return gizmos.OfType<Command_Action>().FirstOrDefault(gizmo => gizmo.defaultLabel == label);
		}
	}

	private static Task UpgradesStatus(
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

		var status = UpgradeStatus.Compute(site.Comp);
		context.Check($"with nothing in stock, {b.defName} is still to be removed: [{Names(status.Removals)}]", status.Removals.SequenceEqual(new[] { installed }));
		context.Check($"and {a.defName} is wanted but not in stock: adds=[{Names(status.Additions)}] unstocked=[{string.Join(",", status.Unstocked.Select(def => def.defName))}]", status.Additions.Count == 0 && status.Unstocked.SequenceEqual(new[] { a }));
		context.Check("which is a change", status.HasChanges);
		context.Check($"its line says wanted: {Lines(status)}", State(status, a) == UpgradeState.Wanted);

		Stock(site.Storage, new[] { a });
		status = UpgradeStatus.Compute(site.Comp);
		context.Check($"with {a.defName} in stock, the next trip adds it: [{Names(status.Additions)}]", status.Additions.Select(upgrade => upgrade.def).SequenceEqual(new[] { a }) && status.Unstocked.Count == 0);
		context.Check($"the lines say adding and removing: {Lines(status)}", State(status, a) == UpgradeState.Adding && State(status, b) == UpgradeState.Removing);
		context.Check($"points after the trip: {status.PointsAfter}", status.PointsAfter == a.upgradePoints);

		// The policy menus describe policies the mech doesn't have
		var keepsB = Database.TryCreate();
		keepsB.Filter.SetDisallowAll();
		keepsB.Filter.SetAllow(b.linkedThingDef, true);
		var other = UpgradeStatus.Compute(site.Comp, keepsB);
		context.Check($"under a policy allowing {b.defName}, it's installed and nothing changes: {Lines(other)}", State(other, b) == UpgradeState.Installed && State(other, a) == null && !other.HasChanges);

		var conflicting = Compatible(site.Mech).FirstOrDefault(def => def != a && def.exclusionTags.Intersect(a.exclusionTags).Any());
		if (conflicting != null)
		{
			var both = Database.TryCreate();
			both.Filter.SetDisallowAll();
			both.Filter.SetAllow(a.linkedThingDef, true);
			both.Filter.SetAllow(conflicting.linkedThingDef, true);
			var withConflict = UpgradeStatus.Compute(site.Comp, both);
			var skippedLine = withConflict.Lines.FirstOrDefault(line => line.Def == conflicting);
			context.Check($"{conflicting.defName}, which conflicts with {a.defName}, is skipped with a reason: {Lines(withConflict)}", skippedLine.State == UpgradeState.Skipped && !skippedLine.Reason.NullOrEmpty());
		}

		var tooltip = UpgradeStatus.Tooltip(site.Comp);
		context.Check($"the tooltip names the policy and both upgrades: {tooltip}", new[] { site.Comp.CurrentUpgradePolicy.label, a.label, b.label }.All(label => tooltip.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0));

		site.Comp.CurrentUpgradePolicy = null;
		var settings = MechUpgradesUiMod.Settings;
		if (!settings.UseDefaultPolicyForUnassigned)
		{
			context.Check("with no policy, nothing changes", !UpgradeStatus.Compute(site.Comp).HasChanges);
		}

		return Task.CompletedTask;
	}

	private static UpgradeState? State(
		UpgradeStatus status,
		MechUpgradeDef def
	)
	{
		return status.Lines.Where(line => line.Def == def).Select(line => (UpgradeState?)line.State).FirstOrDefault();
	}

	private static string Lines(
		UpgradeStatus status
	)
	{
		return string.Join(", ", status.Lines.Select(line => $"{line.Def.defName}={line.State}{(line.Reason.NullOrEmpty() ? "" : $" ({line.Reason})")}"));
	}

	private static Task UpgraderInspect(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		Stock(site.Storage, new[] { a, b });
		var text = site.Upgrader.GetInspectString();
		var expected = "MU_Upgrades_Ui_Upgrader_Stock".Translate(1, 2).ToString();
		context.Check($"the upgrader reports one linked storage holding two upgrades: {text}", text.Contains(expected));
		return Task.CompletedTask;
	}

	private static Task UpgraderNoStorage(
		ScenarioContext context
	)
	{
		var site = Site.Set(context, linkedStorage: false);
		var text = site.Upgrader.GetInspectString();
		var expected = "MU_Upgrades_Ui_Upgrader_NoStorage".Translate().ToString();
		context.Check($"an upgrader with no linked storage says so: {text}", text.Contains(expected));
		return Task.CompletedTask;
	}

	// The status mustn't promise a trip to an upgrader the mech won't go to, and says why
	private static Task UnusableUpgrader(
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
		var settings = MechUpgradesUiMod.Settings;
		var showBlocked = settings.ShowBlockedAlert;
		try
		{
			settings.ShowBlockedAlert = true;
			var usable = UpgradeStatus.Compute(site.Comp);
			context.Check($"powered, the next trip adds {a.defName} there: [{Names(usable.Additions)}]", usable.HasChanges && usable.Upgrader == site.Upgrader && usable.Blocker == null);

			// Not forbidding: vanilla's CaresAboutForbidden is false for colony mechs that need a mechanitor, so a forbidden
			// upgrader is still used, by the job giver and the status alike
			foreach (var (fault, apply, undo) in new (string, Action, Action)[]
			         {
				         ("unpowered", () => Stage.Power(site.Upgrader, false), () => Stage.Power(site.Upgrader, true))
			         })
			{
				apply();
				try
				{
					UpgradeStatus.Invalidate();
					var status = UpgradeStatus.Compute(site.Comp);
					context.Check($"{fault}: nothing is added, and it isn't a change: {Lines(status)}", status.Additions.Count == 0 && !status.HasChanges && State(status, a) == UpgradeState.Wanted);
					context.Check($"{fault}: the blocker names why: {status.Blocker}", status.Blocker != null && status.Blocker.Contains("MU_Upgrades_Ui_Upgrader_NoPower".Translate().ToString()));

					var checkNow = site.Comp.CompGetGizmosExtra().OfType<Command_Action>().FirstOrDefault(gizmo => gizmo.defaultLabel == "MU_Upgrades_Ui_Gizmo_CheckNow".Translate().ToString());
					context.Check($"{fault}: check now is disabled with the blocker: {checkNow?.disabledReason}", checkNow is { Disabled: true } && checkNow.disabledReason == status.Blocker);

					var blocked = new Alert_UpgradePolicyBlocked();
					var report = blocked.GetReport();
					context.Check($"{fault}: the blocked alert names the mech", report.active && report.AllCulprits.Any(target => target.Thing == site.Mech));
					var notInStock = new Alert_UpgradesNotInStock();
					notInStock.GetReport();
					context.Check($"{fault}: and the not-in-stock alert leaves it to the blocked one", !notInStock.WantedBy.Values.Any(mechs => mechs.Contains(site.Mech)));
				}
				finally
				{
					undo();
				}
			}

			UpgradeStatus.Invalidate();
			settings.ShowBlockedAlert = false;
			Stage.Power(site.Upgrader, false);
			context.Check("with the blocked alert turned off, it's not shown", !new Alert_UpgradePolicyBlocked().GetReport().active);
			Stage.Power(site.Upgrader, true);
		}
		finally
		{
			settings.ShowBlockedAlert = showBlocked;
			UpgradeStatus.Invalidate();
		}

		return Task.CompletedTask;
	}

	private static Task NotInStockAlert(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, _) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		using var policy = new TempPolicy(site, a);
		var settings = MechUpgradesUiMod.Settings;
		var showNotInStock = settings.ShowNotInStockAlert;
		try
		{
			settings.ShowNotInStockAlert = true;
			var alert = new Alert_UpgradesNotInStock();
			var report = alert.GetReport();
			context.Check($"with {a.defName} not in stock, the alert names the mech", report.active && report.AllCulprits.Any(target => target.Thing == site.Mech));
			context.Check($"and lists {a.defName} as wanted by it", alert.WantedBy.TryGetValue(a, out var mechs) && mechs.Contains(site.Mech));
			var explanation = alert.GetExplanation().ToString();
			context.Check($"the explanation names the upgrade and the mech: {explanation}", explanation.Contains(a.LabelCap) && explanation.Contains(site.Mech.LabelShort));

			Stock(site.Storage, new[] { a });
			UpgradeStatus.Invalidate();
			alert.GetReport();
			context.Check("in stock, it's no longer listed", !alert.WantedBy.ContainsKey(a) || !alert.WantedBy[a].Contains(site.Mech));

			settings.ShowNotInStockAlert = false;
			context.Check("turned off, the alert isn't shown", !alert.GetReport().active);
		}
		finally
		{
			settings.ShowNotInStockAlert = showNotInStock;
			UpgradeStatus.Invalidate();
		}

		return Task.CompletedTask;
	}

	private static Task DialogDefaults(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var before = Database.GetPolicies().ToList();
		try
		{
			var fromMech = new Dialog_ManageUpgradePolicies(null, site.Comp);
			context.Check($"opened from a mech, it shows that mech's type: {fromMech.MechDef?.defName}", fromMech.MechDef == site.Mech.def);

			var policy = Database.TryCreate();
			site.Comp.CurrentUpgradePolicy = policy;
			var mechDef = Dialog_ManageUpgradePolicies.DefaultMechDef(policy);
			context.Check($"for a policy only the lifter follows, it defaults to lifters: {mechDef?.defName}", mechDef == site.Mech.def);

			var usedBy = Dialog_ManageUpgradePolicies.UsedBy(policy);
			context.Check($"used by names one lifter: {usedBy}", usedBy == "MU_Upgrades_Ui_Policy_UsedBy".Translate(1, site.Mech.def.LabelCap).ToString());

			site.Comp.CurrentUpgradePolicy = null;
			var unused = Dialog_ManageUpgradePolicies.UsedBy(policy);
			context.Check($"with no mech on it, it says so: {unused}", unused.StartsWith("MU_Upgrades_Ui_Policy_UsedByNone".Translate().ToString()));
		}
		finally
		{
			site.Comp.CurrentUpgradePolicy = null;
			Restore(Database, before);
		}

		return Task.CompletedTask;
	}

	private static Task UseDefaultConfirmation(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var (a, b) = TwoCompatible(context, site.Mech);
		if (a == null)
		{
			return Task.CompletedTask;
		}

		var before = Database.GetPolicies().ToList();
		try
		{
			site.Comp.AddUpgrade(b);
			var allowsA = Database.TryCreate();
			allowsA.Filter.SetDisallowAll();
			allowsA.Filter.SetAllow(a.linkedThingDef, true);
			Database.SetDefaultPolicy(allowsA);

			var text = MechUpgradesUiMod.UseDefaultConfirmation();
			context.Check($"the warning names the unassigned mech that would lose {b.defName}: {text}", text.Contains(site.Mech.LabelShort) && text.Contains(allowsA.label));

			allowsA.Filter.SetAllow(b.linkedThingDef, true);
			text = MechUpgradesUiMod.UseDefaultConfirmation();
			context.Check($"once the default allows it, the mech isn't named: {text}", !text.Contains(site.Mech.LabelShort));
		}
		finally
		{
			Restore(Database, before);
		}

		return Task.CompletedTask;
	}

	// A site left standing for looking at the UI, and later for the showcase: one lifter with a policy that wants a change and
	// stock to make it, and one with no policy
	internal static object Demo(
		ScenarioContext context
	)
	{
		var site = Site.Set(context);
		var second = context.ControlledMech(MechKind, site.Mechanitor, -4, -6);
		var (a, b) = TwoCompatible(context, site.Mech);
		var compatible = Compatible(site.Mech).ToList();
		var pair = compatible
			.SelectMany(x => compatible.Where(y => string.CompareOrdinal(x.defName, y.defName) < 0 && x.exclusionTags.Intersect(y.exclusionTags).Any()).Select(y => (x, y)))
			.FirstOrDefault();
		if (a == null || pair.x == null)
		{
			return new { success = false, failures = context.Failures };
		}

		site.Comp.AddUpgrade(b);
		var policy = Database.TryCreate();
		policy.label = "Lifter loadout";
		policy.Filter.SetDisallowAll();
		foreach (var def in new[] { a, pair.x, pair.y }.Distinct())
		{
			policy.Filter.SetAllow(def.linkedThingDef, true);
		}

		Stock(site.Storage, new[] { a, pair.x }.Distinct());
		site.Comp.CurrentUpgradePolicy = policy;

		// Not due for a day, so the status can be looked at before the trip
		site.Mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
		second.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
		return new
		{
			success = context.Failures.Count == 0,
			failures = context.Failures,
			withPolicy = site.Mech.GetUniqueLoadID(),
			withoutPolicy = second.GetUniqueLoadID(),
			upgrader = site.Upgrader.GetUniqueLoadID(),
			policy = policy.label,
			installed = b.defName,
			stocked = new[] { a.defName, pair.x.defName }.Distinct(),
			allowed = new[] { a.defName, pair.x.defName, pair.y.defName }.Distinct()
		};
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
	internal static IEnumerable<MechUpgradeDef> Compatible(
		Pawn mech
	)
	{
		return DefDatabase<MechUpgradeDef>.AllDefs
			.Where(def => def.linkedThingDef?.GetCompProperties<CompProperties_MechUpgrade>()?.upgradeDef == def)
			.Where(def => def.CanAdd(mech.def).Accepted)
			.OrderBy(def => def.defName);
	}

	// Two upgrades the mech can have together, that each fit on their own
	internal static (MechUpgradeDef, MechUpgradeDef) TwoCompatible(
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

	internal static void Stock(
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
