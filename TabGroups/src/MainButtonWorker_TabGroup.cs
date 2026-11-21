using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ordo.TabGroups;

// A group's button: always shows the group's name, and a click (left or right) lists its members. Members are hidden
// from the bar by the Visible postfix but keep their hotkeys, since MainButtonsRoot checks every button's hotkey whether
// it's visible or not.
public class MainButtonWorker_TabGroup : MainButtonWorker
{
    public override bool Visible => base.Visible && TabGroups.Members(def).Any(TabGroups.WouldShow);

    public override bool Disabled => TabGroups.ShownMembers(def).All(member => member.Worker.Disabled);

    // Research's progress bar, when Research is in the group
    public override float ButtonBarPercent =>
        TabGroups.ShownMembers(def).Select(member => member.Worker.ButtonBarPercent).DefaultIfEmpty(0f).Max();

    public override void Activate() => OpenMenu();

    public override void InterfaceTryActivate() => OpenMenu();

    // The member whose tab is open, or World while the world is shown, since it isn't a tab window
    public MainButtonDef OpenMember()
    {
        var open = Find.MainTabsRoot.OpenTab;
        if (open != null && TabGroups.GroupFor(open) == def)
            return open;
        if (WorldRendererUtility.WorldSelected && TabGroups.GroupFor(MainButtonDefOf.World) == def)
            return MainButtonDefOf.World;
        return null;
    }

    public void OpenMenu()
    {
        var open = OpenMember();
        var options = TabGroups.ShownMembers(def)
            .Select(member =>
            {
                var label = member.LabelCap.ToString();
                if (member.hotKey != null && member.hotKey.MainKey != KeyCode.None)
                    label += $" ({member.hotKey.MainKeyLabel})";
                if (member == open)
                    label = label.Colorize(ColorLibrary.Yellow);
                var option = new FloatMenuOption(label, () => member.Worker.InterfaceTryActivate(), member.Icon, Color.white);
                if (member.Worker.Disabled)
                    option.Disabled = true;
                return option;
            })
            .ToList();
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    public override void DoButton(
        Rect rect
    )
    {
        if (Disabled)
        {
            Widgets.DrawAtlas(rect, Widgets.ButtonSubtleAtlas);
            if (Event.current.type == EventType.mouseDown && Mouse.IsOver(rect))
                Event.current.Use();
            return;
        }

        // Right-click too, so it doesn't fall through to MainTabsRoot, which would open the Architect tab
        if (Event.current.type == EventType.mouseDown && Event.current.button == 1 && Mouse.IsOver(rect))
        {
            Event.current.Use();
            OpenMenu();
        }

        Text.Font = GameFont.Small;
        var label = def.LabelCap.ToString();
        if (def.LabelCapWidth > rect.width - 2f)
            label = def.ShortenedLabelCap;
        var textLeftMargin = def.LabelCapWidth > 0.85f * rect.width - 1f ? 2f : -1f;
        if (Widgets.ButtonTextSubtle(rect, label, ButtonBarPercent, textLeftMargin, SoundDefOf.Mouseover_Category, highlight: OpenMember() != null))
            OpenMenu();

        if (Find.MainTabsRoot.OpenTab != def && !Find.WindowStack.NonImmediateDialogWindowOpen)
            UIHighlighter.HighlightOpportunity(rect, def.cachedHighlightTagClosed);
        if (Mouse.IsOver(rect))
            TooltipHandler.TipRegion(rect, Tooltip());
    }

    private string Tooltip()
    {
        var tip = new StringBuilder();
        tip.AppendLine(def.LabelCap.Colorize(ColorLibrary.Yellow));
        if (!def.description.NullOrEmpty())
            tip.AppendLine(def.description);
        tip.AppendLine();
        tip.AppendLine("Ordo_TabGroups_Contains".Translate());
        foreach (var member in TabGroups.ShownMembers(def))
        {
            var line = "  " + member.LabelCap;
            if (member.hotKey != null && member.hotKey.MainKey != KeyCode.None)
                line += $" ({member.hotKey.MainKeyLabel})";
            tip.AppendLine(line);
        }
        return tip.ToString().TrimEnd();
    }
}
