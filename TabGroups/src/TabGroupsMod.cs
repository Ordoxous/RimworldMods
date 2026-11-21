using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.TabGroups;

public class TabGroupsSettings : ModSettings
{
    // Buttons the player moved, by defName: the group's defName, or "" for on the bar. Buttons not here use the defaults.
    public Dictionary<string, string> assignments = new();

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref assignments, "assignments", LookMode.Value, LookMode.Value);
        assignments ??= new Dictionary<string, string>();
    }
}

public class TabGroupsMod : Mod
{
    public static TabGroupsSettings Settings;
    private Vector2 scroll;

    public TabGroupsMod(
        ModContentPack content
    ) : base(content)
    {
        Settings = GetSettings<TabGroupsSettings>();
    }

    public override string SettingsCategory() => "Tab Groups";

    // Moves a button into a group, or onto the bar with null. Choosing its default forgets the choice.
    public static void Assign(
        MainButtonDef button,
        MainButtonDef group
    )
    {
        if (TabGroups.DefaultGroupOf(button) == group)
            Settings.assignments.Remove(button.defName);
        else
            Settings.assignments[button.defName] = group?.defName ?? "";
        TabGroups.Rebuild();
    }

    public override void DoSettingsWindowContents(
        Rect inRect
    )
    {
        var top = new Listing_Standard();
        top.Begin(inRect);
        top.Label("Ordo_TabGroups_Assignments".Translate());
        if (top.ButtonText("Ordo_TabGroups_Reset".Translate(), widthPct: 0.3f))
        {
            Settings.assignments.Clear();
            TabGroups.Rebuild();
        }
        top.Gap(6f);
        var used = top.CurHeight;
        top.End();

        const float rowHeight = 30f;
        var buttons = TabGroups.Groupable.ToList();
        var outRect = new Rect(inRect.x, inRect.y + used, inRect.width, inRect.height - used);
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, buttons.Count * rowHeight);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var row = new Rect(0f, i * rowHeight, viewRect.width, rowHeight);
            if (i % 2 == 1)
                Widgets.DrawLightHighlight(row);
            if (button.Icon != null)
                GUI.DrawTexture(new Rect(row.x + 2f, row.y + 3f, 24f, 24f), button.Icon);

            Text.Anchor = TextAnchor.MiddleLeft;
            var source = button.modContentPack?.Name ?? "";
            Widgets.Label(new Rect(row.x + 32f, row.y, row.width * 0.6f - 32f, row.height),
                $"{button.LabelCap} <color=#999999>({button.defName}, {source})</color>");
            Text.Anchor = TextAnchor.UpperLeft;

            var group = TabGroups.GroupFor(button);
            var choice = group?.LabelCap.ToString() ?? "Ordo_TabGroups_None".Translate().ToString();
            if (Settings.assignments.ContainsKey(button.defName))
                choice += $" ({"Ordo_TabGroups_Changed".Translate()})";
            if (Widgets.ButtonText(new Rect(row.x + row.width * 0.6f, row.y + 2f, row.width * 0.4f, rowHeight - 4f), choice))
            {
                var options = new List<FloatMenuOption> { new("Ordo_TabGroups_None".Translate(), () => Assign(button, null)) };
                options.AddRange(TabGroups.Groups.Select(g => new FloatMenuOption(g.LabelCap, () => Assign(button, g))));
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
        Widgets.EndScrollView();
    }
}
