using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Ordo.Saddlebound;

// One preferred mount species, chosen per ideoligion as vanilla's venerated animals are: one precept per species, in an
// issue that allows several. Its own class rather than Precept_Animal, which vanilla treats as a venerated animal
// everywhere it looks.
public class Precept_PreferredMount : Precept_ThingDef
{
    public override string UIInfoFirstLine => ThingDef == null ? base.UIInfoFirstLine : (string)ThingDef.LabelCap;

    public override string TipLabel => def.issue.LabelCap + ": " + (ThingDef?.LabelCap ?? def.LabelCap);
}

// The species an ideoligion can prefer: animals Giddy-Up lets anyone ride. For generation, favours the faction's own
// mount list (Giddy-Up's FactionRestrictions), animals of the home biome, farm and fighting animals, and bigger bodies.
public class PreceptWorker_PreferredMount : PreceptWorker
{
    private const float FactionAnimalFactor = 10f;
    private const float HomeBiomeFactor = 4f;
    private const float DomesticFactor = 3f;

    public override IEnumerable<PreceptThingChance> ThingDefs =>
        DefDatabase<ThingDef>.AllDefs
            .Where(GiddyUpAdapter.IsMountableRace)
            .Select(race => new PreceptThingChance { def = race, chance = 1f });

    public override AcceptanceReport CanUse(
        ThingDef def,
        Ideo ideo,
        FactionDef generatingFor
    )
    {
        foreach (var precept in ideo.PreceptsListForReading)
        {
            if (precept is Precept_PreferredMount mount && mount.ThingDef == def)
            {
                return false;
            }
        }

        return true;
    }

    public override IEnumerable<PreceptThingChance> ThingDefsForIdeo(
        Ideo ideo,
        FactionDef generatingFor = null
    )
    {
        var factionRaces = GiddyUpAdapter.FactionMountRaces(generatingFor).ToHashSet();
        var biome = HomeBiome(generatingFor);
        var biomeRaces = biome?.AllWildAnimals.Select(kind => kind.race).ToHashSet() ?? new HashSet<ThingDef>();
        foreach (var thing in base.ThingDefsForIdeo(ideo, generatingFor))
        {
            var race = thing.def;
            var chance = Mathf.Clamp(race.race.baseBodySize, 0.5f, 3f);
            if (factionRaces.Contains(race))
            {
                chance *= FactionAnimalFactor;
            }

            if (biomeRaces.Contains(race))
            {
                chance *= HomeBiomeFactor;
            }

            if (IsDomestic(race))
            {
                chance *= DomesticFactor;
            }

            yield return new PreceptThingChance { def = race, chance = chance };
        }
    }

    // Farm and fighting animals, as Giddy-Up counts domestic mounts for raids
    public static bool IsDomestic(
        ThingDef race
    ) => race.tradeTags != null && (race.tradeTags.Contains("AnimalFarm") || race.tradeTags.Contains("AnimalFighter"));

    // The player's starting biome while they make their ideoligion, or the current map's. NPC factions are made before
    // they have settlements, so they get none, and their faction list or the other weights decide.
    private static BiomeDef HomeBiome(
        FactionDef generatingFor
    )
    {
        if (generatingFor is { isPlayer: false })
        {
            return null;
        }

        if (Find.CurrentMap is { } map)
        {
            return map.Biome;
        }

        var tile = Find.GameInitData?.startingTile ?? PlanetTile.Invalid;
        return tile.Valid && Find.World?.grid != null ? Find.WorldGrid[tile].PrimaryBiome : null;
    }
}

public static class PreferredMounts
{
    public static IEnumerable<ThingDef> For(
        Ideo ideo
    )
    {
        if (ideo == null)
        {
            yield break;
        }

        foreach (var precept in ideo.PreceptsListForReading)
        {
            if (precept is Precept_PreferredMount { ThingDef: { } race })
            {
                yield return race;
            }
        }
    }

    public static bool HasAny(
        Ideo ideo
    ) => For(ideo).Any();

    public static bool IsPreferred(
        Ideo ideo,
        ThingDef race
    ) => For(ideo).Contains(race);

    // How many preferred mounts a freshly generated ideoligion gets: two for Horde Riders, one for any other culture that
    // respects riding, none otherwise
    public static int CountToGenerate(
        Ideo ideo
    )
    {
        if (ideo.HasMeme(SaddleboundDefOf.Ordo_HordeRiders))
        {
            return 2;
        }

        return ideo.HasPrecept(SaddleboundDefOf.Ordo_Riding_Essential) || ideo.HasPrecept(SaddleboundDefOf.Ordo_Riding_Respected) ? 1 : 0;
    }

    // Vanilla adds venerated animals in their own step after the other precepts; this does the same for preferred
    // mounts. The precept has no selection weight and doesn't count towards the limit, so vanilla never picks it.
    public static void Generate(
        Ideo ideo,
        FactionDef forFaction
    )
    {
        var def = SaddleboundDefOf.Ordo_PreferredMount;
        var wanted = Mathf.Min(CountToGenerate(ideo), def.maxCount) - ideo.GetPreceptCountOfDef(def);
        for (var i = 0; i < wanted; i++)
        {
            if (!def.Worker.ThingDefsForIdeo(ideo, forFaction).Any())
            {
                return;
            }

            ideo.AddPrecept(PreceptMaker.MakePrecept(def), init: true, generatingFor: forFaction);
        }
    }
}
