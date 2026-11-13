using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MU;
using Ordo.BridgeTools.Scenarios;
using Ordo.MechUpgradesUi;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Showcase;

// The policy dialog, the Mechs tab and a mech upgrading itself. Upgrades are picked at runtime by the scenarios' rules: on the
// current Mechanoid Upgrades these are the double cargo cell (wanted), the mini-burner (unwanted) and the small cargo cell,
// which conflicts with the double one. The checks only decide whether a take is worth keeping.
public static class MechUpgradesUiShowcase
{
	private const string MechKind = "Mech_Lifter";

	// MU's upgrade takes 2500 ticks per point. Cut short for the clip, which says so.
	private const int ShortFabricationTicks = 60;

	public static readonly List<ShowcaseClip> All = new()
	{
		ShowcaseClip.Feature("policy_dialog", "Upgrade Policies",
			"Choose which upgrades a policy allows, as for apparel. The preview shows what a mech type gets, and what's skipped and why",
			PolicyDialog, showUi: true),
		ShowcaseClip.Feature("mechs_tab", "The Mechs Tab",
			"Assign policies in the Mechs tab. A marker shows which mechs will change their upgrades on their next trip",
			MechsTab, showUi: true, frame: ShowcaseFrame.Window),
		ShowcaseClip.Feature("upgrade_trip", "Upgrading Itself",
			"A mech fetches what its policy allows from an auto upgrader, and has the rest removed. Upgrade time shortened here",
			UpgradeTrip, showUi: true, frame: ShowcaseFrame.Window)
	};

	private static WorldComponent_UpgradeDatabase Database => Find.World.GetComponent<WorldComponent_UpgradeDatabase>();

	// The dialog pauses the game, so this clip waits in real time
	private static async Task PolicyDialog(
		ShowcaseStage stage
	)
	{
		var mechanitor = stage.Mechanitor(-6, 3);
		var lifter = Lifter(stage, mechanitor, -4, 3);
		var comp = lifter.GetComp<CompUpgradableMechWithPolicy>();
		var (wanted, unwanted) = MechUpgradesUiScenarios.TwoCompatible(stage, lifter);
		var conflicting = Conflicting(lifter, wanted);
		if (wanted == null || conflicting == null)
		{
			stage.Fail("no upgrades to show for a lifter");
			return;
		}

		var policy = NewPolicy("Lifter loadout");
		comp.CurrentUpgradePolicy = policy;
		var dialog = new Dialog_ManageUpgradePolicies(policy, comp);
		Find.WindowStack.Add(dialog);
		try
		{
			await stage.Hold(2f);
			Allow(policy, wanted);
			await stage.Hold(2f);
			Allow(policy, conflicting);
			await stage.Hold(2.5f);
			Allow(policy, unwanted);
			await stage.Hold(3f);

			var skipped = new List<SkippedUpgrade>();
			var gets = Dialog_ManageUpgradePolicies.Preview(policy, lifter.def, MechUpgradeUtility.MaxUpgradePoints(lifter), skipped);
			stage.Check("the preview skips the conflicting upgrade", skipped.Any(skip => skip.Upgrade.def == conflicting) && gets.Count == 2);
		}
		finally
		{
			dialog.Close(false);
			comp.CurrentUpgradePolicy = null;
			Database.GetPolicies().Remove(policy);
		}
	}

	private static async Task MechsTab(
		ShowcaseStage stage
	)
	{
		stage.Build(ThingDef.Named("MU_MechUpgrader"), 4, 2, Rot4.South, powered: true);
		var storage = stage.Build(ThingDef.Named("MU_UpgradesStorage"), 8, 2);
		var mechanitor = stage.Mechanitor(-6, 3);
		var first = Lifter(stage, mechanitor, -3, 2);
		var second = Lifter(stage, mechanitor, -1, 2);
		var (wanted, _) = MechUpgradesUiScenarios.TwoCompatible(stage, first);
		if (wanted == null)
		{
			return;
		}

		MechUpgradesUiScenarios.Stock(storage, new[] { wanted, wanted });
		var policy = NewPolicy("Lifter loadout");
		Allow(policy, wanted);
		var firstComp = first.GetComp<CompUpgradableMechWithPolicy>();
		var secondComp = second.GetComp<CompUpgradableMechWithPolicy>();
		firstComp.CurrentUpgradePolicy = policy;
		NotDue(first);

		Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainButtonDef>.GetNamed("Mechs"), false);
		try
		{
			await stage.Hold(3f);
			secondComp.CurrentUpgradePolicy = policy;
			NotDue(second);
			await stage.Hold(3.5f);
			stage.Check("the second lifter's next trip would change it", UpgradeStatus.Compute(secondComp).HasChanges);
		}
		finally
		{
			Find.MainTabsRoot.EscapeCurrentTab(false);
			firstComp.CurrentUpgradePolicy = null;
			secondComp.CurrentUpgradePolicy = null;
			Database.GetPolicies().Remove(policy);
		}
	}

	// The lifter starts with an upgrade its policy doesn't allow, and the one it does is in the storage
	private static async Task UpgradeTrip(
		ShowcaseStage stage
	)
	{
		var upgrader = (Building_AutoMechUpgrader)stage.Build(ThingDef.Named("MU_MechUpgrader"), 0, 1, Rot4.South, powered: true);
		var storage = stage.Build(ThingDef.Named("MU_UpgradesStorage"), 4, 1);
		var mechanitor = stage.Mechanitor(-5, 4);
		var lifter = Lifter(stage, mechanitor, 8, -1);
		var comp = lifter.GetComp<CompUpgradableMechWithPolicy>();
		var (wanted, unwanted) = MechUpgradesUiScenarios.TwoCompatible(stage, lifter);
		if (wanted == null)
		{
			return;
		}

		comp.AddUpgrade(unwanted);
		MechUpgradesUiScenarios.Stock(storage, new[] { wanted });
		var policy = NewPolicy("Lifter loadout");
		Allow(policy, wanted);
		comp.CurrentUpgradePolicy = policy;
		NotDue(lifter);
		ShowUpgrades(lifter);
		try
		{
			await stage.Beat();
			await stage.Beat();
			lifter.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame;

			if (!await stage.Until("the lifter goes into the upgrader", () => upgrader.Occupant == lifter, 2000))
			{
				return;
			}

			Find.Selector.ClearSelection();
			Find.Selector.Select(upgrader, false, false);
			for (var elapsed = 0; elapsed < 1500 && upgrader.Occupant == lifter; elapsed += 5)
			{
				if (upgrader.fabricationTicksLeft > ShortFabricationTicks)
				{
					upgrader.fabricationTicksLeft = ShortFabricationTicks;
				}

				await stage.Step(5);
			}

			stage.Check("the lifter came out", lifter.Spawned);
			stage.Check("it has the wanted upgrade and not the other", comp.upgrades.Any(u => u.def == wanted) && comp.upgrades.All(u => u.def != unwanted));
			ShowUpgrades(lifter);
			await stage.Linger(240);
		}
		finally
		{
			Find.Selector.ClearSelection();
			comp.CurrentUpgradePolicy = null;
			Database.GetPolicies().Remove(policy);
		}
	}

	// A controlled lifter with no upgrades, not due a policy check for a day
	private static Pawn Lifter(
		ShowcaseStage stage,
		Pawn mechanitor,
		int x,
		int z
	)
	{
		var lifter = stage.ControlledMech(MechKind, mechanitor, x, z);
		var comp = lifter.GetComp<CompUpgradableMechWithPolicy>();
		foreach (var upgrade in comp.upgrades.ToList())
		{
			comp.RemoveUpgrade(upgrade);
		}

		NotDue(lifter);
		return lifter;
	}

	private static void NotDue(
		Pawn mech
	)
	{
		mech.mindState.nextApparelOptimizeTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
	}

	// An upgrade the lifter could have that shares an exclusion tag with the given one
	private static MechUpgradeDef Conflicting(
		Pawn mech,
		MechUpgradeDef upgrade
	)
	{
		return upgrade == null
			? null
			: MechUpgradesUiScenarios.Compatible(mech).FirstOrDefault(def => def != upgrade && def.exclusionTags.Intersect(upgrade.exclusionTags).Any());
	}

	private static UpgradePolicy NewPolicy(
		string label
	)
	{
		var policy = Database.TryCreate();
		policy.label = label;
		policy.Filter.SetDisallowAll();
		return policy;
	}

	private static void Allow(
		UpgradePolicy policy,
		MechUpgradeDef upgrade
	)
	{
		policy.Filter.SetAllow(upgrade.linkedThingDef, true);
	}

	// Selects the mech with MU's upgrades tab open, so its upgrades are on screen
	private static void ShowUpgrades(
		Pawn mech
	)
	{
		Find.Selector.ClearSelection();
		Find.Selector.Select(mech, false, false);
		InspectPaneUtility.OpenTab(typeof(ITab_MechUpgrades));
	}
}
