using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.ColonistBarJobs;

// Draws the current job's icon on each colonist bar portrait, where the settings put it, with the job report as its tooltip.
// Clicking the icon jumps the camera to the job's target.
[StaticConstructorOnStartup]
public static class ColonistBarJobIcons
{
    // Near-black, not black: on Waylith a box of pure black (0, 0, 0) draws nothing at any alpha, while any other colour draws
    private static readonly Color Backdrop = new(0.05f, 0.05f, 0.06f);

    // Resolved once per job: the bar draws every frame, a job changes a few times a minute. Jobs are pooled and reused, so a
    // job is known by its loadID (new from each JobMaker.MakeJob), not by reference.
    private static readonly Dictionary<Pawn, (int jobId, JobIcon icon)> Cache = new();

    // Where each colonist's icon was last drawn, and in which frame, for clicks: the bar handles clicks before it draws
    private static readonly Dictionary<Pawn, (Rect rect, int frame)> Drawn = new();

    static ColonistBarJobIcons()
    {
        var harmony = new Harmony(typeof(ColonistBarJobIcons).Namespace);
        harmony.TryPatch(
            AccessTools.Method(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist)),
            "ColonistBarColonistDrawer.DrawColonist",
            "the colonist bar shows no job icons",
            postfix: new HarmonyMethod(typeof(ColonistBarJobIcons), nameof(Postfix_DrawColonist))
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.HandleClicks)),
            "ColonistBarColonistDrawer.HandleClicks",
            "clicking a job icon selects the colonist instead of jumping to the job's target",
            prefix: new HarmonyMethod(typeof(ColonistBarJobIcons), nameof(Prefix_HandleClicks))
        );
    }

    public static void ClearCache()
    {
        Cache.Clear();
        Drawn.Clear();
    }

    // The job the icon is for: with MultiFloors, a pawn taking the stairs to work on another level shows that work, not the stair
    public static Job ShownJob(
        Pawn pawn
    ) => MultiFloorsLevels.JobAfterStairs(pawn, pawn.CurJob) ?? pawn.CurJob;

    public static JobIcon IconFor(
        Pawn pawn
    )
    {
        var job = ShownJob(pawn);
        var jobId = job?.loadID ?? -1;
        if (Cache.TryGetValue(pawn, out var cached) && cached.jobId == jobId)
        {
            return cached.icon;
        }

        // Pawns that left the bar (died, left the map for good) stay until the cache is cleared; keep it small
        if (Cache.Count > 256)
        {
            ClearCache();
        }

        var icon = JobIconResolver.For(job);
        Cache[pawn] = (jobId, icon);
        return icon;
    }

    private static void Postfix_DrawColonist(
        [HarmonyArgument(0)] Rect rect,
        [HarmonyArgument(1)] Pawn colonist
    )
    {
        if (colonist == null || colonist.Dead)
        {
            return;
        }

        // In the activity row the row's own hooks draw it; the corner is the fallback when they couldn't be installed
        if (ColonistBarJobsMod.Settings.Placement == IconPlacement.ActivityRow && ActivityRow.Hooked)
        {
            return;
        }

        var icon = IconFor(colonist);
        if (!icon.IsEmpty)
        {
            DrawWithTooltip(ColonistBarJobsMod.Settings.IconRect(rect), icon, colonist);
        }
    }

    // Draws the icon with its tooltip. On the bar (clickable) it's also remembered for clicks; on a mech's portrait in the
    // control group gizmo, clicks belong to the gizmo.
    public static void DrawWithTooltip(
        Rect rect,
        JobIcon icon,
        Pawn pawn,
        bool clickable = true
    )
    {
        if (Event.current.type == EventType.Repaint)
        {
            Draw(rect, icon);
            if (clickable)
            {
                Drawn[pawn] = (rect, RealTime.frameCount);
            }
        }

        TooltipHandler.TipRegion(rect, new TipSignal(() => JobDetails.Tooltip(pawn, ShownJob(pawn), clickable), pawn.thingIDNumber ^ 0x0B4A));
    }

    // Whether a click at this point of the screen lands on the colonist's icon as last drawn
    public static bool IsOverIcon(
        Pawn colonist,
        Vector2 point
    ) => Drawn.TryGetValue(colonist, out var drawn) && drawn.frame >= RealTime.frameCount - 2 && drawn.rect.Contains(point);

    // A left click on the icon jumps to the job's target, and is used up so the colonist isn't selected or dragged. Double
    // clicks too: vanilla's double click would jump to the colonist instead.
    private static void Prefix_HandleClicks(
        Pawn colonist
    )
    {
        var current = Event.current;
        if (current.type != EventType.MouseDown || current.button != 0 || !IsOverIcon(colonist, current.mousePosition))
        {
            return;
        }

        var target = JobDetails.JumpTarget(colonist, ShownJob(colonist));
        if (!target.IsValid)
        {
            return;
        }

        current.Use();
        CameraJumper.TryJump(target);
    }

    public static void Draw(
        Rect rect,
        JobIcon icon
    )
    {
        // The bar fades portraits of pawns on other maps through GUI.color; keep that fade
        var alpha = GUI.color.a;
        var old = GUI.color;
        var opacity = ColonistBarJobsMod.Settings.BackdropOpacity;
        if (opacity > 0f)
        {
            Widgets.DrawBoxSolid(rect, new Color(Backdrop.r, Backdrop.g, Backdrop.b, opacity * alpha));
        }

        GUI.color = new Color(1f, 1f, 1f, alpha);

        var inner = rect.ContractedBy(1f);
        if (icon.Texture != null)
        {
            GUI.DrawTexture(inner, icon.Texture, ScaleMode.ScaleToFit);
        }
        else
        {
            Widgets.DefIcon(inner, icon.Def, icon.Stuff, alpha: alpha);
        }

        GUI.color = old;
    }
}
