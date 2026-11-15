using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using Ordo.ColonistBarJobs;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Scenarios;

// Most scenarios build jobs with JobMaker and ask the resolver for their icon, which is the whole decision the bar's drawing
// relies on; one has a real colonist take jobs, for the cache. Drawing itself needs a windowed profile and is checked by eye.
public static class ColonistBarJobsScenarios
{
    // Suites: "setup" for defs, textures and the patch, "resolve" for which icon a job gets, "cache" for following job changes
    public static readonly List<Scenario> All = new()
    {
        new("setup_defs_textures_and_patch", "setup", DefsTexturesAndPatch),
        new("resolve_job_mapping_beats_target", "resolve", JobMappingBeatsTarget),
        new("resolve_work_type_mapping", "resolve", WorkTypeMapping),
        new("resolve_target_thing", "resolve", TargetThing),
        new("resolve_blueprint_shows_what_it_builds", "resolve", BlueprintShowsWhatItBuilds),
        new("resolve_nothing_to_show", "resolve", NothingToShow),
        new("cache_follows_job", "cache", CacheFollowsJob)
    };

    private static JobIconDef Icon(
        string defName
    ) => DefDatabase<JobIconDef>.GetNamed(defName);

    private static WorkGiverDef GiverFor(
        WorkTypeDef workType
    ) => DefDatabase<WorkGiverDef>.AllDefsListForReading.First(giver => giver.workType == workType);

    private static Task DefsTexturesAndPatch(
        ScenarioContext context
    )
    {
        var defs = DefDatabase<JobIconDef>.AllDefsListForReading;
        context.Check($"the mod's JobIconDefs loaded: {defs.Count}", defs.Count >= 10);

        var missing = defs.Where(def => def.texPath != null && def.Texture == null).Select(def => def.texPath).ToList();
        context.Check($"every texPath has a texture; missing: [{string.Join(", ", missing)}]", missing.Count == 0);

        var noIcon = defs.Where(def => def.thingIcon != null && (def.thingIcon.uiIcon == null || def.thingIcon.uiIcon == BaseContent.BadTex))
            .Select(def => def.thingIcon.defName).ToList();
        context.Check($"every thingIcon has an icon; without: [{string.Join(", ", noIcon)}]", noIcon.Count == 0);

        var method = AccessTools.Method(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist));
        var owners = Harmony.GetPatchInfo(method)?.Postfixes.Select(patch => patch.owner).ToList() ?? new List<string>();
        context.Check($"DrawColonist has the mod's postfix; postfix owners: [{string.Join(", ", owners)}]", owners.Contains("Ordo.ColonistBarJobs"));
        return Task.CompletedTask;
    }

    // Deconstructing targets the building, but the building's icon would read as building it
    private static Task JobMappingBeatsTarget(
        ScenarioContext context
    )
    {
        var wall = context.Place(ThingDefOf.Wall, 0, 0, ThingDefOf.WoodLog);
        var job = JobMaker.MakeJob(JobDefOf.Deconstruct, wall);
        job.workGiverDef = GiverFor(WorkTypeDefOf.Construction);

        var icon = JobIconResolver.For(job);
        context.Check($"deconstructing shows the deconstruct icon, not the wall: {icon}", icon.Source == Icon("Ordo_JobIcon_Deconstruct"));
        return Task.CompletedTask;
    }

    // Mining targets plain rock; the work type's icon says more
    private static Task WorkTypeMapping(
        ScenarioContext context
    )
    {
        var rock = context.Place(DefDatabase<ThingDef>.GetNamed("Granite"), 0, 0);
        var job = JobMaker.MakeJob(JobDefOf.Mine, rock);
        job.workGiverDef = GiverFor(WorkTypeDefOf.Mining);

        var icon = JobIconResolver.For(job);
        context.Check($"mining shows the mining icon: {icon}", icon.Source == Icon("Ordo_JobIcon_Mining"));

        // The same job from no work giver (an order, or a think tree job) has no work type to go by
        job.workGiverDef = null;
        var unmapped = JobIconResolver.For(job);
        context.Check($"without a work giver it shows the rock: {unmapped}", unmapped.Source == null && unmapped.Def == rock.def);
        return Task.CompletedTask;
    }

    private static Task TargetThing(
        ScenarioContext context
    )
    {
        var steel = context.Place(ThingDefOf.Steel, 0, 0, stackCount: 20);
        var haul = JobMaker.MakeJob(JobDefOf.HaulToCell, steel, context.Cell(3, 0));
        haul.workGiverDef = GiverFor(WorkTypeDefOf.Hauling);
        var hauled = JobIconResolver.For(haul);
        context.Check($"hauling steel shows steel: {hauled}", hauled.Def == ThingDefOf.Steel && hauled.Texture == null);

        var parka = context.Place(ThingDefOf.Apparel_Parka, 1, 0, ThingDefOf.Cloth);
        var wear = JobMaker.MakeJob(JobDefOf.Wear, parka);
        var worn = JobIconResolver.For(wear);
        context.Check($"putting on a cloth parka shows a cloth parka: {worn}", worn.Def == ThingDefOf.Apparel_Parka && worn.Stuff == ThingDefOf.Cloth);
        return Task.CompletedTask;
    }

    private static Task BlueprintShowsWhatItBuilds(
        ScenarioContext context
    )
    {
        var wall = context.Blueprint(ThingDefOf.Wall, 0, 0, ThingDefOf.WoodLog);
        var job = JobMaker.MakeJob(JobDefOf.HaulToContainer, context.Place(ThingDefOf.WoodLog, 1, 0, stackCount: 5), wall);
        job.workGiverDef = GiverFor(WorkTypeDefOf.Construction);

        // Delivering targets the wood (A), so check the blueprint itself too
        var delivering = JobIconResolver.For(job);
        context.Check($"delivering wood shows wood: {delivering}", delivering.Def == ThingDefOf.WoodLog);

        var fromBlueprint = JobIconResolver.FromThing(wall);
        context.Check($"a wooden wall blueprint shows a wooden wall: {fromBlueprint}",
            fromBlueprint.Def == ThingDefOf.Wall && fromBlueprint.Stuff == ThingDefOf.WoodLog);
        return Task.CompletedTask;
    }

    private static Task NothingToShow(
        ScenarioContext context
    )
    {
        context.Check("no job shows nothing", JobIconResolver.For((Job)null).IsEmpty);

        var walk = JobIconResolver.For(JobMaker.MakeJob(JobDefOf.Goto, context.Cell(2, 2)));
        context.Check($"walking to a cell shows nothing: {walk}", walk.IsEmpty);

        var other = context.Colonist(0, 0);
        var attack = JobIconResolver.For(JobMaker.MakeJob(JobDefOf.AttackMelee, other));
        context.Check($"attacking a pawn shows nothing: {attack}", attack.IsEmpty);
        return Task.CompletedTask;
    }

    // A miner gets the mining icon once at work, and none once drafted: the cache follows the pawn's job
    private static async Task CacheFollowsJob(
        ScenarioContext context
    )
    {
        ColonistBarJobIcons.ClearCache();
        var rock = context.Place(DefDatabase<ThingDef>.GetNamed("Granite"), 3, 0);
        context.Map.designationManager.AddDesignation(new Designation(rock, DesignationDefOf.Mine));
        var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);

        if (!await context.Until("the miner starts mining", () => miner.CurJobDef == JobDefOf.Mine, 600))
        {
            return;
        }

        var mining = ColonistBarJobIcons.IconFor(miner);
        context.Check($"the bar shows the mining icon while mining: {mining}", mining.Source == Icon("Ordo_JobIcon_Mining"));

        miner.drafter.Drafted = true;
        await context.Step(10);
        var drafted = ColonistBarJobIcons.IconFor(miner);
        context.Check($"once drafted ({miner.CurJobDef?.defName}) the bar shows nothing: {drafted}", drafted.IsEmpty);
    }
}
