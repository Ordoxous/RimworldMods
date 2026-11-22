using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Ordo.TabGroups;
using RimWorld;
using Verse;
using Groups = Ordo.TabGroups.TabGroups;
using Patches = Ordo.TabGroups.Patches;

namespace Ordo.BridgeTools.Scenarios;

// Drawing the group buttons is checked by eye in a windowed profile; these check membership, hiding, what a click opens and
// the settings. Each scenario that changes the settings puts them back in a finally.
public static class TabGroupsScenarios
{
    // Suites: "setup" for the defs and patches, "bar" for what's shown, "open" for what a group opens, "settings" for moving
    // buttons between groups
    public static readonly List<Scenario> All = new()
    {
        new("setup_groups_and_patches", "setup", GroupsAndPatches),
        new("bar_members_hidden_hotkeys_kept", "bar", MembersHiddenHotkeysKept),
        new("bar_group_hidden_when_no_member_shows", "bar", GroupHiddenWhenNoMemberShows),
        new("bar_mechs_override_respected", "bar", MechsOverrideRespected, requiresMod: "ludeon.rimworld.biotech"),
        new("open_click_always_lists", "open", ClickAlwaysOpensList),
        new("open_menu_lists_members", "open", MenuListsMembers),
        new("open_research_bar_shown", "open", ResearchBarShown),
        new("settings_move_and_reset", "settings", MoveAndReset),
        new("settings_hidden_and_late_buttons", "settings", HiddenAndLateButtons)
    };

    private static MainButtonDef Button(
        string defName
    ) => DefDatabase<MainButtonDef>.GetNamed(defName);

    private static MainButtonDef Colonists => Button("Ordo_TabGroup_Colonists");
    private static MainButtonDef Knowledge => Button("Ordo_TabGroup_Knowledge");
    private static MainButtonDef World => Button("Ordo_TabGroup_World");
    private static MainButtonDef Tools => Button("Ordo_TabGroup_Tools");

    private static string Names(
        IEnumerable<MainButtonDef> defs
    ) => string.Join(", ", defs.Select(def => def.defName));

    private static void RestoreSettings(
        Dictionary<string, string> saved
    )
    {
        TabGroupsMod.Settings.assignments = saved;
        // Assign saves at once, so the file is put back too
        TabGroupsMod.Settings.Write();
        Groups.Rebuild();
    }

    private static Task GroupsAndPatches(
        ScenarioContext context
    )
    {
        context.Check($"five groups loaded: {Names(Groups.Groups)}", Groups.Groups.Count() == 5);
        var other = Button("Ordo_TabGroup_Other");
        context.Check($"Other has no loaded members here, so it's hidden: {Names(Groups.Members(other))}", Groups.Members(other).Count > 0 || !other.Worker.Visible);
        context.Check($"Colonists holds Work, Schedule and Assign: {Names(Groups.Members(Colonists))}",
            new[] { "Work", "Schedule", "Assign" }.All(name => Groups.GroupFor(Button(name)) == Colonists));
        context.Check($"Knowledge holds Research: {Names(Groups.Members(Knowledge))}", Groups.GroupFor(Button("Research")) == Knowledge);
        context.Check($"World holds World, Quests and Factions, World first: {Names(Groups.Members(World))}",
            new[] { "World", "Quests", "Factions" }.All(name => Groups.GroupFor(Button(name)) == World) && Groups.Members(World)[0].defName == "World");
        context.Check($"Tools holds History: {Names(Groups.Members(Tools))}", Groups.GroupFor(Button("History")) == Tools);
        context.Check("Architect, Menu and Inspect stay on the bar",
            Groups.GroupFor(Button("Architect")) == null && Groups.GroupFor(Button("Menu")) == null && Groups.GroupFor(MainButtonDefOf.Inspect) == null);

        var getter = AccessTools.PropertyGetter(typeof(MainButtonWorker), nameof(MainButtonWorker.Visible));
        context.Check("MainButtonWorker.Visible has the mod's postfix",
            Harmony.GetPatchInfo(getter)?.Postfixes.Any(patch => patch.owner == "Ordo.TabGroups") == true);
        context.Check($"every worker override of Visible is patched: {string.Join(", ", Patches.Patched.Select(t => t.Name))}",
            Patches.Patched.Contains(typeof(MainButtonWorker)) && !Patches.Patched.Contains(typeof(MainButtonWorker_TabGroup)));
        return Task.CompletedTask;
    }

    private static Task MembersHiddenHotkeysKept(
        ScenarioContext context
    )
    {
        var work = Button("Work");
        context.Check("Work is hidden from the bar", !work.Worker.Visible);
        context.Check("but would show if it weren't grouped", Groups.WouldShow(work));
        context.Check("Architect is still shown", Button("Architect").Worker.Visible);
        context.Check("the Colonists group is shown", Colonists.Worker.Visible && !Colonists.Worker.Disabled);

        // MainButtonsRoot checks the hotkey of every button in this list, visible or not
        var all = Find.MainButtonsRoot.allButtonsInOrder;
        context.Check("hidden Work is still in the bar's hotkey list", all.Contains(work));
        context.Check("so are the groups", all.Contains(Colonists) && all.Contains(Tools));

        var shown = all.Where(def => def.Worker.Visible).ToList();
        context.Check($"the bar shows: {Names(shown)}", !shown.Contains(work) && shown.Contains(Colonists) && shown.Contains(Button("Menu")));
        return Task.CompletedTask;
    }

    private static Task GroupHiddenWhenNoMemberShows(
        ScenarioContext context
    )
    {
        var saved = new Dictionary<string, string>(TabGroupsMod.Settings.assignments);
        try
        {
            foreach (var member in Groups.Members(Tools).ToList())
                TabGroupsMod.Assign(member, null);
            context.Check($"Tools is empty: {Names(Groups.Members(Tools))}", Groups.Members(Tools).Count == 0);
            context.Check("so it's hidden", !Tools.Worker.Visible);
            context.Check("and History is back on the bar", Button("History").Worker.Visible);
        }
        finally
        {
            RestoreSettings(saved);
        }
        context.Check("restored, Tools is shown again", Tools.Worker.Visible);
        return Task.CompletedTask;
    }

    // Biotech's Mechs worker overrides Visible without calling base: hidden while the colony has no mechs
    private static Task MechsOverrideRespected(
        ScenarioContext context
    )
    {
        var mechs = Button("Mechs");
        context.Check("Mechs is in Colonists", Groups.GroupFor(mechs) == Colonists);
        context.Check($"its worker's override is patched: {mechs.Worker.GetType().Name}", Patches.Patched.Contains(typeof(MainButtonWorker_ToggleMechTab)));
        context.Check("without mechs it wouldn't show, so the group leaves it out",
            !Groups.WouldShow(mechs) && !Groups.ShownMembers(Colonists).Contains(mechs));
        context.Check("and it stays off the bar", !mechs.Worker.Visible);
        return Task.CompletedTask;
    }

    private static void CloseMenus()
    {
        foreach (var menu in Find.WindowStack.Windows.OfType<FloatMenu>().ToList())
            Find.WindowStack.TryRemove(menu, false);
    }

    // A click never opens a tab by itself: it lists the members, the same every time, and the label stays the group's
    private static Task ClickAlwaysOpensList(
        ScenarioContext context
    )
    {
        var tabs = Find.MainTabsRoot;
        tabs.EscapeCurrentTab(false);
        CloseMenus();
        try
        {
            Colonists.Worker.InterfaceTryActivate();
            var menu = Find.WindowStack.WindowOfType<FloatMenu>();
            var first = menu?.options.Select(option => option.Label).ToList() ?? new List<string>();
            context.Check($"clicking Colonists lists its members: [{string.Join(" | ", first)}]",
                first.Count == Groups.ShownMembers(Colonists).Count && first.Count > 0);
            context.Check($"and opens no tab: {tabs.OpenTab?.defName}", tabs.OpenTab == null);

            menu.options.First(option => option.Label.StartsWith("Schedule")).action();
            CloseMenus();
            context.Check($"choosing Schedule opens it: {tabs.OpenTab?.defName}", tabs.OpenTab == Button("Schedule"));
            context.Check("the group knows its member is open, for the highlight",
                ((MainButtonWorker_TabGroup)Colonists.Worker).OpenMember() == Button("Schedule"));

            Colonists.Worker.InterfaceTryActivate();
            var again = Find.WindowStack.WindowOfType<FloatMenu>()?.options.Select(option => option.Label.StripTags()).ToList() ?? new List<string>();
            context.Check($"clicking again lists the same members, not Schedule alone: [{string.Join(" | ", again)}]",
                again.SequenceEqual(first.Select(label => label.StripTags())));
            context.Check($"and leaves Schedule open: {tabs.OpenTab?.defName}", tabs.OpenTab == Button("Schedule"));
        }
        finally
        {
            CloseMenus();
            tabs.EscapeCurrentTab(false);
        }
        return Task.CompletedTask;
    }

    // The World menu: its main tab first, and choosing one opens it
    private static Task MenuListsMembers(
        ScenarioContext context
    )
    {
        var tabs = Find.MainTabsRoot;
        tabs.EscapeCurrentTab(false);
        CloseMenus();
        ((MainButtonWorker_TabGroup)World.Worker).OpenMenu();
        var menu = Find.WindowStack.WindowOfType<FloatMenu>();
        try
        {
            var labels = menu?.options.Select(option => option.Label).ToList() ?? new List<string>();
            context.Check($"the World menu lists World first, then Quests: [{string.Join(" | ", labels)}]",
                labels.Count == Groups.ShownMembers(World).Count && labels[0].StartsWith("World") && labels.Any(label => label.StartsWith("Quests")));
            menu?.options.FirstOrDefault(option => option.Label.StartsWith("Quests"))?.action();
            context.Check($"choosing Quests opens it: {tabs.OpenTab?.defName}", tabs.OpenTab == Button("Quests"));
        }
        finally
        {
            CloseMenus();
            tabs.EscapeCurrentTab(false);
        }
        return Task.CompletedTask;
    }

    private static Task ResearchBarShown(
        ScenarioContext context
    )
    {
        var research = Button("Research");
        context.Check($"Knowledge shows Research's progress bar: {Knowledge.Worker.ButtonBarPercent} vs {research.Worker.ButtonBarPercent}",
            Knowledge.Worker.ButtonBarPercent == research.Worker.ButtonBarPercent);
        return Task.CompletedTask;
    }

    private static Task MoveAndReset(
        ScenarioContext context
    )
    {
        var saved = new Dictionary<string, string>(TabGroupsMod.Settings.assignments);
        var work = Button("Work");
        var architect = Button("Architect");
        try
        {
            TabGroupsMod.Assign(work, null);
            context.Check("Work moved onto the bar", Groups.GroupFor(work) == null && work.Worker.Visible);
            context.Check("the choice is saved as on the bar", TabGroupsMod.Settings.assignments.TryGetValue("Work", out var none) && none == "");

            TabGroupsMod.Assign(work, Knowledge);
            context.Check($"Work moved into Knowledge: {Names(Groups.Members(Knowledge))}", Groups.GroupFor(work) == Knowledge && !work.Worker.Visible);

            TabGroupsMod.Assign(architect, Tools);
            context.Check("Architect, on the bar by default, moved into Tools", Groups.GroupFor(architect) == Tools && !architect.Worker.Visible);

            TabGroupsMod.Assign(work, Colonists);
            context.Check("moving Work back to its default forgets the choice",
                Groups.GroupFor(work) == Colonists && !TabGroupsMod.Settings.assignments.ContainsKey("Work"));
        }
        finally
        {
            RestoreSettings(saved);
        }
        context.Check("restored, Architect is back on the bar", Groups.GroupFor(architect) == null && architect.Worker.Visible);
        return Task.CompletedTask;
    }

    // A button moved into a group stays there while its mod has it hidden, as Animation Manager is until a game loads,
    // and one a mod adds from code after startup, as Character Editor does, joins the group it was moved to
    private static Task HiddenAndLateButtons(
        ScenarioContext context
    )
    {
        var saved = new Dictionary<string, string>(TabGroupsMod.Settings.assignments);
        var architect = Button("Architect");
        var late = new MainButtonDef
        {
            defName = "Ordo_TabGroupsTest_LateButton",
            label = "late button",
            buttonVisible = true,
            order = 999,
            workerClass = typeof(MainButtonWorker_ToggleTab)
        };
        var remove = AccessTools.Method(typeof(DefDatabase<MainButtonDef>), "Remove");
        try
        {
            architect.buttonVisible = false;
            TabGroupsMod.Assign(architect, Tools);
            context.Check("Architect, hidden by its def, still goes into Tools", Groups.GroupFor(architect) == Tools);
            context.Check("but isn't in its menu while hidden", !Groups.ShownMembers(Tools).Contains(architect));
            architect.buttonVisible = true;
            context.Check("shown again, it's in Tools' menu and off the bar",
                Groups.ShownMembers(Tools).Contains(architect) && !architect.Worker.Visible);
            context.Check("the settings list it", Groups.Groupable.Contains(architect));

            TabGroupsMod.Settings.assignments[late.defName] = Tools.defName;
            Groups.Rebuild();
            DefDatabase<MainButtonDef>.Add(late);
            context.Check($"a button added later joins Tools: {Names(Groups.Members(Tools))}", Groups.GroupFor(late) == Tools);
        }
        finally
        {
            architect.buttonVisible = true;
            if (DefDatabase<MainButtonDef>.GetNamedSilentFail(late.defName) != null)
                remove.Invoke(null, new object[] { late });
            RestoreSettings(saved);
        }
        context.Check("restored, Architect is back on the bar", Groups.GroupFor(architect) == null && architect.Worker.Visible);
        return Task.CompletedTask;
    }
}
