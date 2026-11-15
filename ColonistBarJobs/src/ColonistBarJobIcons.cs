using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.ColonistBarJobs;

// Draws the current job's icon in the top right of each colonist bar portrait, with the job report as its tooltip
[StaticConstructorOnStartup]
public static class ColonistBarJobIcons
{
    // The icon's share of the portrait's width
    private const float SizeFactor = 0.4f;

    private static readonly Color Backdrop = new(0f, 0f, 0f, 0.45f);

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

    // The rect the icon takes in a portrait's rect
    public static Rect IconRect(
        Rect portrait
    )
    {
        var size = Mathf.Round(portrait.width * SizeFactor);
        return new Rect(portrait.xMax - size - 1f, portrait.y + 1f, size, size);
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

        var icon = IconFor(colonist);
        if (icon.IsEmpty)
        {
            return;
        }

        var iconRect = IconRect(rect);
        if (Event.current.type == EventType.Repaint)
        {
            Draw(iconRect, icon);
        }

        TooltipHandler.TipRegion(iconRect, new TipSignal(() => Tooltip(colonist), colonist.thingIDNumber ^ 0x0B4A));
    }

    public static void Draw(
        Rect rect,
        JobIcon icon
    )
    {
        // The bar fades portraits of pawns on other maps through GUI.color; keep that fade
        var alpha = GUI.color.a;
        var old = GUI.color;
        GUI.color = new Color(Backdrop.r, Backdrop.g, Backdrop.b, Backdrop.a * alpha);
        GUI.DrawTexture(rect, BaseContent.WhiteTex);
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
