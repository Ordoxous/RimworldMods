using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.ColonistBarJobs;

// Maps jobs or work types to an icon, for jobs whose target isn't the right picture (see Defs/JobIcons.xml)
public class JobIconDef : Def
{
    public List<JobDef> jobs = new();
    public List<WorkTypeDef> workTypes = new();
    [NoTranslate] public string texPath;
    public ThingDef thingIcon;

    [Unsaved] private Texture2D texture;
    [Unsaved] private bool textureLoaded;

    // Loaded on first use, on the main thread, and null when the path doesn't exist
    public Texture2D Texture
    {
        get
        {
            if (!textureLoaded && texPath != null)
            {
                texture = ContentFinder<Texture2D>.Get(texPath, reportFailure: false);
                textureLoaded = true;
            }

            return texture;
        }
    }

    public override IEnumerable<string> ConfigErrors()
    {
        foreach (var error in base.ConfigErrors())
        {
            yield return error;
        }

        if (texPath == null && thingIcon == null)
        {
            yield return "has neither texPath nor thingIcon";
        }

        if (jobs.Count == 0 && workTypes.Count == 0)
        {
            yield return "matches no jobs or work types";
        }
    }
}

// What the colonist bar draws for a job: a texture, or a def's icon (with its stuff, for the colour)
public readonly struct JobIcon
{
    public readonly Texture2D Texture;
    public readonly BuildableDef Def;
    public readonly ThingDef Stuff;
    public readonly JobIconDef Source;

    public JobIcon(
        Texture2D texture,
        BuildableDef def,
        ThingDef stuff,
        JobIconDef source
    )
    {
        Texture = texture;
        Def = def;
        Stuff = stuff;
        Source = source;
    }

    public bool IsEmpty => Texture == null && Def == null;

    public static readonly JobIcon None = default;

    public override string ToString() =>
        IsEmpty ? "none" : Texture != null ? $"texture {Source?.texPath}" : Stuff != null ? $"{Def.defName} ({Stuff.defName})" : Def.defName;
}

public static class JobIconResolver
{
    private static Dictionary<JobDef, JobIconDef> byJob;
    private static Dictionary<WorkTypeDef, JobIconDef> byWorkType;

    // Rebuilds the lookups from the defs; call after a def reload
    public static void Reset()
    {
        byJob = null;
        byWorkType = null;
    }

    private static void EnsureLookups()
    {
        if (byJob != null)
        {
            return;
        }

        byJob = new Dictionary<JobDef, JobIconDef>();
        byWorkType = new Dictionary<WorkTypeDef, JobIconDef>();
        foreach (var def in DefDatabase<JobIconDef>.AllDefsListForReading)
        {
            foreach (var job in def.jobs)
            {
                byJob[job] = def;
            }

            foreach (var workType in def.workTypes)
            {
                byWorkType[workType] = def;
            }
        }
    }

    public static JobIcon For(
        Pawn pawn
    ) => For(pawn?.CurJob);

    // The job's own mapping, then its work type's, then the thing it targets
    public static JobIcon For(
        Job job
    )
    {
        if (job == null)
        {
            return JobIcon.None;
        }

        EnsureLookups();

        if (byJob.TryGetValue(job.def, out var jobIcon) && FromDef(jobIcon) is { IsEmpty: false } fromJob)
        {
            return fromJob;
        }

        if (job.workGiverDef?.workType is { } workType
            && byWorkType.TryGetValue(workType, out var workTypeIcon)
            && FromDef(workTypeIcon) is { IsEmpty: false } fromWorkType)
        {
            return fromWorkType;
        }

        return FromThing(job.targetA.Thing);
    }

    private static JobIcon FromDef(
        JobIconDef def
    )
    {
        if (def.Texture is { } texture)
        {
            return new JobIcon(texture, null, null, def);
        }

        return def.thingIcon != null ? new JobIcon(null, def.thingIcon, null, def) : JobIcon.None;
    }

    // The icon of what the job works on. Pawns and corpses get none: their icons are the race's, which says little.
    public static JobIcon FromThing(
        Thing thing
    )
    {
        switch (thing)
        {
            case null:
            case Pawn:
            case Corpse:
                return JobIcon.None;
            case Blueprint or Frame when thing.def.entityDefToBuild is { } built:
                // A blueprint holds its material in stuffToUse; a frame is made of it
                var stuff = thing is Blueprint_Build blueprint ? blueprint.stuffToUse : thing.Stuff;
                return HasIcon(built) ? new JobIcon(null, built, stuff, null) : JobIcon.None;
            case MinifiedThing { InnerThing: { } inner }:
                return HasIcon(inner.def) ? new JobIcon(null, inner.def, inner.Stuff, null) : JobIcon.None;
            default:
                return HasIcon(thing.def) ? new JobIcon(null, thing.def, thing.Stuff, null) : JobIcon.None;
        }
    }

    private static bool HasIcon(
        BuildableDef def
    ) => def.uiIcon != null && def.uiIcon != BaseContent.BadTex;
}
