using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.ColonistBarJobs;

// Draws the current job's icon on each colonist bar portrait, where the settings put it, with the job report as its tooltip
[StaticConstructorOnStartup]
public static class ColonistBarJobIcons
{
    // Near-black, not black: on Waylith a box of pure black (0, 0, 0) draws nothing at any alpha, while any other colour draws
    private static readonly Color Backdrop = new(0.05f, 0.05f, 0.06f);

    // Resolved once per job: the bar draws every frame, a job changes a few times a minute. Jobs are pooled and reused, so a
    // job is known by its loadID (new from each JobMaker.MakeJob), not by reference.
    private static readonly Dictionary<Pawn, (int jobId, JobIcon icon)> Cache = new();

    static ColonistBarJobIcons()
    {
        var harmony = new Harmony(typeof(ColonistBarJobIcons).Namespace);
        harmony.Patch(
            AccessTools.Method(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist)),
            postfix: new HarmonyMethod(typeof(ColonistBarJobIcons), nameof(Postfix_DrawColonist))
        );
    }

    public static void ClearCache() => Cache.Clear();

    public static JobIcon IconFor(
        Pawn pawn
    )
    {
        var job = pawn.CurJob;
        var jobId = job?.loadID ?? -1;
        if (Cache.TryGetValue(pawn, out var cached) && cached.jobId == jobId)
        {
            return cached.icon;
        }

        // Pawns that left the bar (died, left the map for good) stay until the cache is cleared; keep it small
        if (Cache.Count > 256)
        {
            Cache.Clear();
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

    public static void DrawWithTooltip(
        Rect rect,
        JobIcon icon,
        Pawn colonist
    )
    {
        if (Event.current.type == EventType.Repaint)
        {
            Draw(rect, icon);
        }

        TooltipHandler.TipRegion(rect, new TipSignal(() => Tooltip(colonist), colonist.thingIDNumber ^ 0x0B4A));
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

    private static string Tooltip(
        Pawn pawn
    )
    {
        var report = pawn.jobs?.curDriver?.GetReport();
        return report.NullOrEmpty() ? pawn.CurJobDef?.LabelCap ?? "" : report.CapitalizeFirst();
    }
}
