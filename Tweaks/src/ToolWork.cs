using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

// Which stats a kind of work uses, so a tool can be chosen for it. Names, not def references, so a row can name another mod's
// defs and still load without it; missing names are skipped.
public class ToolWorkDef : Def
{
    public List<string> workTypes = new();
    public List<string> jobs = new();
    public List<string> stats = new();
}

// Which stats a job or work type uses, and how well a tool helps with them. Shared by switching and fetching.
public static class ToolStats
{
    private static Dictionary<JobDef, HashSet<StatDef>> _byJob;
    private static Dictionary<WorkTypeDef, HashSet<StatDef>> _byWorkType;

    private static readonly HashSet<StatDef> None = new();

    // The stats of the job's recipe, else of its ToolWorkDef row. Not its work type's: that would swap tools for a work type's
    // side jobs too (a scalpel for feeding patients, a hammer for delivering materials). Jobs no row names fall back to Simple
    // Sidearms' choice by the toil's skill.
    public static void ForJob(Job job, HashSet<StatDef> into)
    {
        if (job == null)
        {
            return;
        }

        Build();
        var recipe = job.RecipeDef;
        if (recipe != null)
        {
            AddRecipe(recipe, into);
            return;
        }

        if (_byJob.TryGetValue(job.def, out var stats))
        {
            into.UnionWith(stats);
        }
    }

    public static HashSet<StatDef> ForWorkType(WorkTypeDef workType)
    {
        Build();
        return _byWorkType.TryGetValue(workType, out var stats) ? stats : None;
    }

    // The sum of the tool's offsets on these stats; -1 if any of them is a penalty, so a tool that hurts the work is never used
    // for it. 0 when it doesn't help.
    public static float Score(ThingDef tool, ICollection<StatDef> stats)
    {
        var offsets = tool.equippedStatOffsets;
        if (offsets == null || stats.Count == 0)
        {
            return 0f;
        }

        var sum = 0f;
        foreach (var offset in offsets)
        {
            if (!stats.Contains(offset.stat))
            {
                continue;
            }

            if (offset.value < 0f)
            {
                return -1f;
            }

            sum += offset.value;
        }

        return sum;
    }

    public static bool SlowsWalking(ThingDef tool) =>
        tool.equippedStatOffsets != null && tool.equippedStatOffsets.Any(offset => offset.stat == StatDefOf.MoveSpeed && offset.value < 0f);

    private static void Build()
    {
        if (_byJob != null)
        {
            return;
        }

        _byJob = new Dictionary<JobDef, HashSet<StatDef>>();
        _byWorkType = new Dictionary<WorkTypeDef, HashSet<StatDef>>();
        foreach (var row in DefDatabase<ToolWorkDef>.AllDefsListForReading)
        {
            var stats = new HashSet<StatDef>();
            foreach (var stat in row.stats.Select(DefDatabase<StatDef>.GetNamedSilentFail).Where(stat => stat != null))
            {
                AddWithFactors(stat, stats);
            }

            foreach (var job in row.jobs.Select(DefDatabase<JobDef>.GetNamedSilentFail).Where(job => job != null))
            {
                Add(_byJob, job, stats);
            }

            foreach (var workType in row.workTypes.Select(DefDatabase<WorkTypeDef>.GetNamedSilentFail).Where(workType => workType != null))
            {
                Add(_byWorkType, workType, stats);
            }
        }

        // Bill work types (cooking, smithing, surgery...) take theirs from every recipe their bill givers offer
        var surgeries = DefDatabase<RecipeDef>.AllDefsListForReading.Where(recipe => recipe.IsSurgery).ToList();
        foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
        {
            var stats = new HashSet<StatDef>();
            foreach (var giver in workType.workGiversByPriority.Where(giver => typeof(WorkGiver_DoBill).IsAssignableFrom(giver.giverClass)))
            {
                foreach (var bench in giver.fixedBillGiverDefs ?? Enumerable.Empty<ThingDef>())
                {
                    foreach (var recipe in bench.AllRecipes)
                    {
                        AddRecipe(recipe, stats);
                    }
                }

                if (giver.billGiversAllHumanlikes || giver.billGiversAllHumanlikesCorpses || giver.billGiversAllAnimals || giver.billGiversAllMechanoids)
                {
                    foreach (var recipe in surgeries)
                    {
                        AddRecipe(recipe, stats);
                    }
                }
            }

            if (stats.Count > 0)
            {
                Add(_byWorkType, workType, stats);
            }
        }
    }

    private static void AddRecipe(RecipeDef recipe, HashSet<StatDef> into)
    {
        if (recipe.workSpeedStat != null)
        {
            AddWithFactors(recipe.workSpeedStat, into);
        }

        if (recipe.efficiencyStat != null)
        {
            AddWithFactors(recipe.efficiencyStat, into);
        }

        if (recipe.IsSurgery)
        {
            into.Add(StatDefOf.MedicalSurgerySuccessChance);
        }
    }

    // A speed stat's factors (WorkSpeedGlobal, GeneralLaborSpeed...) count too, so a general-speed tool helps everywhere
    private static void AddWithFactors(StatDef stat, HashSet<StatDef> into)
    {
        into.Add(stat);
        if (stat.statFactors != null)
        {
            into.UnionWith(stat.statFactors);
        }
    }

    private static void Add<T>(Dictionary<T, HashSet<StatDef>> map, T key, HashSet<StatDef> stats)
    {
        if (!map.TryGetValue(key, out var existing))
        {
            map[key] = existing = new HashSet<StatDef>();
        }

        existing.UnionWith(stats);
    }
}
