using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.ColonistBarJobs;

// What the icon's tooltip says beyond the job report, and where clicking the icon jumps to
public static class JobDetails
{
    // The tooltip: the job report, then the bill and how far along the job is, when known, then the click hint
    public static string Tooltip(
        Pawn pawn,
        Job shown,
        bool clickable
    )
    {
        var report = pawn.jobs?.curDriver?.GetReport();
        var text = new StringBuilder(report.NullOrEmpty() ? pawn.CurJobDef?.LabelCap.ToString() ?? "" : report.CapitalizeFirst());
        if (shown == null)
        {
            return text.ToString();
        }

        if (BillLabel(shown) is { } bill)
        {
            text.Append('\n').Append("Ordo_ColonistBarJobs_Bill".Translate(bill));
        }

        if (Progress(pawn, shown) is { } progress)
        {
            text.Append('\n').Append("Ordo_ColonistBarJobs_Progress".Translate(progress.ToStringPercent()));
        }

        if (clickable && JumpTarget(pawn, shown) is { IsValid: true } target)
        {
            var hint = target.HasThing
                ? "Ordo_ColonistBarJobs_ClickToJump".Translate(target.Thing.LabelNoParenthesis)
                : "Ordo_ColonistBarJobs_ClickToJumpThere".Translate();
            text.Append("\n\n").Append(hint.Resolve().Colorize(ColoredText.SubtleGrayColor));
        }

        return text.ToString();
    }

    // The bill a crafting or surgery job works on, with how many it's making ("Make simple meal (3/10)")
    public static string BillLabel(
        Job job
    )
    {
        var bill = job.bill;
        if (bill == null)
        {
            return null;
        }

        return bill is Bill_Production production ? $"{bill.LabelCap} ({production.RepeatInfoText})" : bill.LabelCap;
    }

    // How far along the job is, for those that track it: a bill's work once begun, a frame being built, and the current research
    // project. Not mining, whose report already gives the rock's health ("Digging at granite (82%)"). Null for the rest, and
    // for a job the pawn hasn't started yet (MultiFloors' queued job).
    public static float? Progress(
        Pawn pawn,
        Job job
    )
    {
        var driver = pawn.jobs?.curDriver;
        if (driver == null || driver.job != job)
        {
            return null;
        }

        switch (driver)
        {
            case JobDriver_DoBill doBill when doBill.workLeft > 0f && job.bill?.recipe != null:
                {
                    var total = job.bill.recipe.WorkAmountTotal(job.GetTarget(TargetIndex.B).Thing);
                    return total > 0f ? Mathf.Clamp01(1f - doBill.workLeft / total) : null;
                }
            case JobDriver_Research:
                return Find.ResearchManager.GetProject()?.ProgressPercent;
        }

        return job.targetA.Thing is Frame frame ? Mathf.Clamp01(frame.PercentComplete) : null;
    }

    // The job's target: its thing wherever it is (carried things jump to their carrier), or its cell on the pawn's map. A cell
    // of a job queued for another level (MultiFloors) isn't on the pawn's map, so that has none.
    public static GlobalTargetInfo JumpTarget(
        Pawn pawn,
        Job job
    )
    {
        var target = job?.targetA ?? LocalTargetInfo.Invalid;
        if (target.Thing is { } thing)
        {
            return thing != pawn && thing.SpawnedOrAnyParentSpawned ? new GlobalTargetInfo(thing) : GlobalTargetInfo.Invalid;
        }

        return target.IsValid && job == pawn.CurJob && pawn.Map != null ? new GlobalTargetInfo(target.Cell, pawn.Map) : GlobalTargetInfo.Invalid;
    }
}
