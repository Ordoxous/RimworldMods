using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.FloraTab;

// One thing a plant kind gives when harvested
public sealed class YieldSource
{
    public ThingDef thing;

    // The plant's own harvestedThingDef, scaled by Plant.YieldNow; otherwise a secondary output from a mod
    public bool primary;

    // Secondary outputs: the stack size at full growth, scaled down by growth
    public int amount;

    // The chance this is the thing given, when the mod picks one of several at random
    public float share = 1f;
}

// What a row's ready plants would give of one thing
public sealed class FloraYield
{
    public YieldSource source;
    public float now;

    public ThingDef Thing => source.thing;
}

// Everything a plant kind gives when harvested: its harvestedThingDef, plus secondary outputs that Vanilla Expanded
// Framework adds with VEF.Plants.DualCropExtension (in its Plant.PlantCollected prefix). The extension is read by
// reflection, so the mod doesn't need VEF.
public static class FloraYields
{
    private const string DualCropExtension = "VEF.Plants.DualCropExtension";

    private static readonly Dictionary<ThingDef, List<YieldSource>> SourcesByDef = new();

    public static List<YieldSource> For(ThingDef def)
    {
        if (SourcesByDef.TryGetValue(def, out List<YieldSource> sources))
            return sources;
        sources = new List<YieldSource>();
        if (def.plant.harvestedThingDef != null)
            sources.Add(new YieldSource { thing = def.plant.harvestedThingDef, primary = true });
        if (def.modExtensions != null)
        {
            foreach (DefModExtension extension in def.modExtensions)
            {
                if (extension?.GetType().FullName == DualCropExtension)
                    AddDualCrop(extension, sources);
            }
        }
        SourcesByDef[def] = sources;
        return sources;
    }

    // What one plant would give of a source if harvested now, without the random rounding. For a secondary output this
    // is VEF's (int)(outPutAmount × growth), assuming a harvester with 100% plant harvest yield: VEF gives nothing to
    // animals, and a harvester below 100% has that chance of getting it at all.
    public static float Expected(
        Plant plant,
        YieldSource source)
    {
        if (source.primary)
            return FloraSurvey.ExpectedYield(plant);
        if (!plant.CanYieldNow())
            return 0f;
        return (int)(source.amount * plant.Growth) * source.share;
    }

    private static void AddDualCrop(
        DefModExtension extension,
        List<YieldSource> sources)
    {
        try
        {
            Type type = extension.GetType();
            int amount = Field<int>(type, extension, "outPutAmount");
            if (amount <= 0)
                return;
            bool random = Field<bool>(type, extension, "randomOutput");
            if (random && Field<IList>(type, extension, "randomSecondaryOutput") is { Count: > 0 } choices)
            {
                var things = new List<ThingDef>();
                foreach (object choice in choices)
                {
                    if (choice is ThingDef thing)
                        things.Add(thing);
                }
                // RandomElement picks evenly, so a thing listed twice is twice as likely
                foreach (ThingDef thing in things)
                {
                    YieldSource existing = sources.Find(s => !s.primary && s.thing == thing);
                    if (existing != null)
                        existing.share += 1f / things.Count;
                    else
                        sources.Add(new YieldSource { thing = thing, amount = amount, share = 1f / things.Count });
                }
            }
            else if (Field<ThingDef>(type, extension, "secondaryOutput") is { } thing)
                sources.Add(new YieldSource { thing = thing, amount = amount });
        }
        catch (Exception e)
        {
            Log.WarningOnce($"[FloraTab] Couldn't read {DualCropExtension}: {e.Message}", 0x0F10A7AB);
        }
    }

    private static T Field<T>(
        Type type,
        object instance,
        string name)
    {
        object value = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);
        return value is T typed ? typed : default;
    }

    public static string AmountText(float amount) =>
        amount <= 0f ? "0" : amount < 1f ? amount.ToString("0.#") : Mathf.RoundToInt(amount).ToString();
}
