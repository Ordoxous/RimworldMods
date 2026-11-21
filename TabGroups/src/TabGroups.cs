using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.TabGroups;

// A group's default members, by MainButtonDef defName
public class TabGroupExtension : DefModExtension
{
    public List<string> members = new();
}

// Which group each main button is in. Rebuilt from the defs and the settings
// at startup and whenever the settings change.
public static class TabGroups
{
    private static readonly Dictionary<MainButtonDef, MainButtonDef> GroupOf = new();
    private static readonly Dictionary<MainButtonDef, List<MainButtonDef>> MembersOf = new();

    // While true, the Visible postfix leaves grouped buttons alone, so a group can ask whether a member would show
    private static bool readingRaw;

    public static IEnumerable<MainButtonDef> Groups =>
        DefDatabase<MainButtonDef>.AllDefsListForReading.Where(IsGroup);

    public static bool IsGroup(
        MainButtonDef def
    ) => def.workerClass == typeof(MainButtonWorker_TabGroup);

    // The buttons a player can put in a group: any shown on the bar, except groups and the inspect pane
    public static IEnumerable<MainButtonDef> Groupable =>
        DefDatabase<MainButtonDef>.AllDefsListForReading
            .Where(def => def.buttonVisible && !IsGroup(def) && def != MainButtonDefOf.Inspect)
            .OrderBy(def => def.order);

    public static MainButtonDef DefaultGroupOf(
        MainButtonDef button
    ) => Groups.FirstOrDefault(group => group.GetModExtension<TabGroupExtension>()?.members.Contains(button.defName) == true);

    public static MainButtonDef GroupFor(
        MainButtonDef button
    ) => GroupOf.TryGetValue(button, out var group) ? group : null;

    public static bool IsHidden(
        MainButtonDef button
    ) => !readingRaw && GroupOf.ContainsKey(button);

    public static IReadOnlyList<MainButtonDef> Members(
        MainButtonDef group
    ) => MembersOf.TryGetValue(group, out var members) ? members : (IReadOnlyList<MainButtonDef>)new List<MainButtonDef>();

    // Whether the button would be on the bar if it weren't in a group, such as Mechs only with a mechanitor
    public static bool WouldShow(
        MainButtonDef button
    )
    {
        var was = readingRaw;
        readingRaw = true;
        try
        {
            return button.Worker.Visible;
        }
        finally
        {
            readingRaw = was;
        }
    }

    public static List<MainButtonDef> ShownMembers(
        MainButtonDef group
    ) => Members(group).Where(WouldShow).ToList();

    public static void Rebuild()
    {
        GroupOf.Clear();
        MembersOf.Clear();
        var settings = TabGroupsMod.Settings;
        var groups = Groups.ToList();
        foreach (var group in groups)
            MembersOf[group] = new List<MainButtonDef>();

        foreach (var button in Groupable)
        {
            var group = DefaultGroupOf(button);
            if (settings.assignments.TryGetValue(button.defName, out var chosen))
                group = chosen.NullOrEmpty() ? null : groups.FirstOrDefault(g => g.defName == chosen);
            if (group == null)
                continue;
            GroupOf[button] = group;
            MembersOf[group].Add(button);
        }

        // The menu lists the defaults' order first, so each group's main tab is on top (World before Quests), then by order
        foreach (var group in groups)
        {
            var listed = group.GetModExtension<TabGroupExtension>()?.members ?? new List<string>();
            MembersOf[group] = MembersOf[group]
                .OrderBy(button => listed.IndexOf(button.defName) is var i && i >= 0 ? i : int.MaxValue)
                .ThenBy(button => button.order)
                .ToList();
        }
    }
}
