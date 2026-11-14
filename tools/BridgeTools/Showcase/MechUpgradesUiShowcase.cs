using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MU;
using Ordo.BridgeTools.Scenarios;
using Ordo.MechUpgradesUi;
using RimWorld;
using UnityEngine;
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
			"Choose which upgrades a policy allows, as for apparel. The preview shows what changes for this mech, and what's skipped and why",
			PolicyDialog, showUi: true),
		ShowcaseClip.Feature("mechs_tab", "The Mechs Tab",
			"Assign policies in the Mechs tab. A marker shows which mechs will change their upgrades on their next trip",
			MechsTab, showUi: true, frame: ShowcaseFrame.Window),
		ShowcaseClip.Feature("policy_tooltips", "At a Glance",
			"Hover over a policy to see each upgrade's state for that mech, and compare policies before picking one",
			PolicyTooltips, showUi: true, frame: ShowcaseFrame.Window),
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

		// Behind the dialog, so the lifter's own section shows upgrades being added rather than nowhere to get them
		stage.Build(ThingDef.Named("MU_MechUpgrader"), 4, 2, Rot4.South, powered: true);
		var storage = stage.Build(ThingDef.Named("MU_UpgradesStorage"), 8, 2);
		MechUpgradesUiScenarios.Stock(storage, new[] { wanted, unwanted });

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
			stage.Check("and the lifter's own section adds from stock", UpgradeStatus.Compute(comp).Additions.Count == 2);
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

	// Tooltips need a cursor, so this moves RimBridge's virtual one: over the first lifter's policy and upgrades, then down the
	// second lifter's policy menu. The first lifter has a mini-burner its policy removes, the double cargo cell it adds from
	// stock, and the small cargo cell it skips as conflicting. "Burner kit" wants a mini-burner nobody has in stock
	private static async Task PolicyTooltips(
		ShowcaseStage stage
	)
	{
		stage.Build(ThingDef.Named("MU_MechUpgrader"), 4, 2, Rot4.South, powered: true);
		var storage = stage.Build(ThingDef.Named("MU_UpgradesStorage"), 8, 2);
		var mechanitor = stage.Mechanitor(-6, 3);
		var first = Lifter(stage, mechanitor, -3, 2);
		var second = Lifter(stage, mechanitor, -1, 2);
		var (wanted, unwanted) = MechUpgradesUiScenarios.TwoCompatible(stage, first);
		var conflicting = Conflicting(first, wanted);
		if (wanted == null || conflicting == null)
		{
			stage.Fail("no upgrades to show for a lifter");
			return;
		}

		var firstComp = first.GetComp<CompUpgradableMechWithPolicy>();
		var secondComp = second.GetComp<CompUpgradableMechWithPolicy>();
		firstComp.AddUpgrade(unwanted);
		MechUpgradesUiScenarios.Stock(storage, new[] { wanted });

		var loadout = NewPolicy("Lifter loadout");
		Allow(loadout, wanted);
		Allow(loadout, conflicting);
		var burner = NewPolicy("Burner kit");
		Allow(burner, unwanted);
		firstComp.CurrentUpgradePolicy = loadout;
		NotDue(first);

		Find.MainTabsRoot.SetCurrentTab(DefDatabase<MainButtonDef>.GetNamed("Mechs"), false);
		FloatMenu menu = null;
		try
		{
			// The table lays itself out on its first draw
			await stage.Hold(1.5f);
			if (!HoverCell(stage, first, "MechUpgradesUi_UpgradePolicy"))
			{
				return;
			}

			await stage.Hold(4.5f);
			if (!HoverCell(stage, first, "MechUpgradesUi_Upgrades"))
			{
				return;
			}

			await stage.Hold(4f);

			// The column's own menu, opened at the cursor as a click would
			if (!HoverCell(stage, second, "MechUpgradesUi_UpgradePolicy"))
			{
				return;
			}

			await stage.Hold(0.5f);
			var options = UpgradePolicyMenu
				.Options(policy => secondComp.CurrentUpgradePolicy = policy, secondComp)
				.Select((option, i) => option.ToFloatMenuOption(UpgradePolicyMenu.TipId(secondComp, i)))
				.ToList();
			menu = new FloatMenu(options);
			Find.WindowStack.Add(menu);
			await stage.Hold(0.5f);

			// Down the policies, skipping "Edit..."
			var top = 0f;
			for (var i = 0; i < options.Count - 1; i++)
			{
				stage.Hover(new Vector2(menu.windowRect.x + 40f, menu.windowRect.y + top + options[i].RequiredHeight / 2f));
				top += options[i].RequiredHeight - 1f;
				await stage.Hold(3.5f);
			}

			stage.Check("the menu stayed open", Find.WindowStack.IsOpen(menu));
			stage.Check("the tooltips describe each lifter", Mentions(UpgradeStatus.Describe(firstComp, null), wanted) && Mentions(UpgradeStatus.Describe(secondComp, burner), unwanted));
		}
		finally
		{
			ShowcaseStage.ClearHover();
			if (menu != null)
			{
				Find.WindowStack.TryRemove(menu, false);
			}

			Find.MainTabsRoot.EscapeCurrentTab(false);
			firstComp.CurrentUpgradePolicy = null;
			secondComp.CurrentUpgradePolicy = null;
			Database.GetPolicies().Remove(loadout);
			Database.GetPolicies().Remove(burner);
		}
	}

	// Tooltips capitalise the label
	private static bool Mentions(
		string text,
		MechUpgradeDef upgrade
	)
	{
		return text.IndexOf(upgrade.label, System.StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Moves the virtual cursor to the middle of a mech's cell in the open Mechs tab
	private static bool HoverCell(
		ShowcaseStage stage,
		Pawn mech,
		string column
	)
	{
		var cell = MechsTabCell(mech, column);
		if (cell == null)
		{
			stage.Fail($"couldn't find {mech.LabelShort}'s {column} cell in the Mechs tab");
			return false;
		}

		stage.Hover(cell.Value.center);
		return true;
	}

	// Where the open pawn table drew a cell, in UI coordinates. PawnTable keeps its layout private, so it's read by
	// reflection, following PawnTableOnGUI: the window's margin, the header, then the widths and heights before the cell.
	// Assumes the table isn't scrolled, which it isn't with a few mechs
	private static Rect? MechsTabCell(
		Pawn pawn,
		string column
	)
	{
		const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
		if (Find.MainTabsRoot.OpenTab?.TabWindow is not MainTabWindow_PawnTable window
		    || typeof(MainTabWindow_PawnTable).GetField("table", Private)?.GetValue(window) is not PawnTable table
		    || typeof(PawnTable).GetField("cachedColumnWidths", Private)?.GetValue(table) is not List<float> widths
		    || typeof(PawnTable).GetField("cachedRowHeights", Private)?.GetValue(table) is not List<float> heights)
		{
			return null;
		}

		var columnIndex = table.Columns.FindIndex(def => def.defName == column);
		var row = table.PawnsListForReading.IndexOf(pawn);
		if (columnIndex < 0 || row < 0 || columnIndex >= widths.Count || row >= heights.Count)
		{
			return null;
		}

		var margin = (float)(typeof(Window).GetProperty("Margin", Private)?.GetValue(window) ?? 0f);
		var extraTop = (float)(typeof(MainTabWindow_PawnTable).GetProperty("ExtraTopSpace", Private)?.GetValue(window) ?? 0f);
		var x = window.windowRect.x + margin + widths.Take(columnIndex).Sum(width => (int)width);
		var y = window.windowRect.y + margin + extraTop + (int)table.HeaderHeight + heights.Take(row).Sum(height => (int)height);
		return new Rect(x, y, (int)widths[columnIndex], (int)heights[row]);
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
