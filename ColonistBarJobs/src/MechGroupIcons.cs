using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.ColonistBarJobs;

// Mechs aren't on the colonist bar, so their job icons go on their portraits in the mechanitor's control group gizmos
// (Biotech). A postfix on the gizmo's drawing works the portraits' grid out as it does and draws each mech's icon in its
// portrait's bottom right corner, with the job report as tooltip. Clicks stay the gizmo's: a click on a portrait selects the
// mech and jumps to it.
[StaticConstructorOnStartup]
public static class MechGroupIcons
{
    // From MechanitorControlGroupGizmo.GizmoOnGUI: the gizmo's size, its padding, and the label row above the portraits
    private const float GizmoHeight = 75f;
    private const float Padding = 6f;
    private const float LabelRowHeight = 30f;

    // The icon's share of a portrait's width, and its smallest size
    private const float IconShare = 0.45f;
    private const float MinIconSize = 10f;

    static MechGroupIcons()
    {
        if (!ModsConfig.BiotechActive)
        {
            return;
        }

        new Harmony(typeof(MechGroupIcons).Namespace).TryPatch(
            AccessTools.Method(typeof(MechanitorControlGroupGizmo), nameof(MechanitorControlGroupGizmo.GizmoOnGUI)),
            "MechanitorControlGroupGizmo.GizmoOnGUI",
            "mechs show no job icons",
            postfix: new HarmonyMethod(typeof(MechGroupIcons), nameof(Postfix_GizmoOnGUI))
        );
    }

    private static void Postfix_GizmoOnGUI(
        MechanitorControlGroupGizmo __instance,
        Vector2 topLeft,
        float maxWidth
    )
    {
        if (!ColonistBarJobsMod.Settings.ShowOnMechs)
        {
            return;
        }

        var mechs = __instance.controlGroup.MechsForReading;
        if (mechs.Count == 0)
        {
            return;
        }

        var portraits = PortraitArea(new Rect(topLeft.x, topLeft.y, __instance.GetWidth(maxWidth), GizmoHeight));
        var (columns, rows, size) = Grid(portraits, mechs.Count);
        var marginX = (portraits.width - columns * size) / 2f;
        var marginY = (portraits.height - rows * size) / 2f;
        for (var i = 0; i < mechs.Count; i++)
        {
            var mech = mechs[i];
            var icon = ColonistBarJobIcons.IconFor(mech);
            if (icon.IsEmpty)
            {
                continue;
            }

            var portrait = new Rect(portraits.x + i % columns * size + marginX, portraits.y + i / columns * size + marginY, size, size);
            ColonistBarJobIcons.DrawWithTooltip(IconRect(portrait), icon, mech, clickable: false);
        }
    }

    // The area under the gizmo's label that holds the portraits
    public static Rect PortraitArea(
        Rect gizmo
    )
    {
        var inner = gizmo.ContractedBy(Padding);
        return new Rect(inner.x, inner.y + LabelRowHeight, inner.width, inner.height - LabelRowHeight);
    }

    // As the gizmo sizes its portraits: the largest square, stepping down a pixel at a time, that fits them all in a grid of as
    // many columns and rows as fit. It fills them row by row and centres the whole grid, empty rows included.
    public static (int columns, int rows, float size) Grid(
        Rect area,
        int count
    )
    {
        for (var size = area.height; size > 0f; size--)
        {
            var columns = Mathf.FloorToInt(area.width / size);
            var rows = Mathf.FloorToInt(area.height / size);
            if (columns * rows >= count)
            {
                return (columns, rows, size);
            }
        }

        return (Mathf.Max(count, 1), 1, 1f);
    }

    // The bottom right corner of a mech's portrait
    public static Rect IconRect(
        Rect portrait
    )
    {
        var size = Mathf.Round(Mathf.Max(portrait.width * IconShare, MinIconSize));
        return new Rect(portrait.xMax - size, portrait.yMax - size, size, size);
    }
}
