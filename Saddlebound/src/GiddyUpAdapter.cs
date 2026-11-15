using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GiddyUp;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ordo.Saddlebound;

// Every call into Giddy-Up 2 - Continued. It's a community fork that changes often, so an API change should break only
// this file. Members are listed in Members for the setup_giddyup_api scenario.
public static class GiddyUpAdapter
{
    // MountUtility.Mount(this Pawn rider, Pawn animal) is private; every mount goes through it, apart from the WTH
    // mechanoid raid patch. Hooked instead of the ExtendedPawnData.Mount setter, which is one line and may be inlined.
    public static MethodInfo MountMethod => AccessTools.Method(typeof(MountUtility), "Mount");

    // Public; InvoluntaryDismount and GoDismount both end here
    public static MethodInfo DismountMethod => AccessTools.Method(typeof(MountUtility), nameof(MountUtility.Dismount));

    public static MethodInfo GenerateMountsMethod =>
        AccessTools.Method(typeof(MountUtility), nameof(MountUtility.GenerateMounts));

    // Giddy-Up's FactionRestrictions def extension is internal, so it's read by reflection
    private static readonly System.Type FactionRestrictionsType = AccessTools.TypeByName("GiddyUp.FactionRestrictions");
    private static readonly FieldInfo AllowedNonWildField =
        FactionRestrictionsType == null ? null : AccessTools.Field(FactionRestrictionsType, "allowedNonWildAnimals");
    private static readonly FieldInfo AllowedWildField =
        FactionRestrictionsType == null ? null : AccessTools.Field(FactionRestrictionsType, "allowedWildAnimals");

    // What setup_giddyup_api checks: name and whether it resolved
    public static IEnumerable<(string name, bool found)> Members()
    {
        yield return ("MountUtility.Mount", MountMethod != null);
        yield return ("MountUtility.Dismount", DismountMethod != null);
        yield return ("MountUtility.GenerateMounts", GenerateMountsMethod != null);
        yield return ("ModSettings_GiddyUp.enemyMountChance",
            AccessTools.Field(typeof(ModSettings_GiddyUp), nameof(ModSettings_GiddyUp.enemyMountChance)) != null);
        yield return ("ModSettings_GiddyUp.enemyMountChancePreInd",
            AccessTools.Field(typeof(ModSettings_GiddyUp), nameof(ModSettings_GiddyUp.enemyMountChancePreInd)) != null);
        yield return ("ModSettings_GiddyUp.battleMountsEnabled",
            AccessTools.Field(typeof(ModSettings_GiddyUp), nameof(ModSettings_GiddyUp.battleMountsEnabled)) != null);
        yield return ("ModSettings_GiddyUp.caravansEnabled",
            AccessTools.Field(typeof(ModSettings_GiddyUp), nameof(ModSettings_GiddyUp.caravansEnabled)) != null);
        yield return ("ExtendedPawnData.Mount",
            AccessTools.Property(typeof(ExtendedPawnData), nameof(ExtendedPawnData.Mount)) != null);
        yield return ("ModSettings_GiddyUp.MountableCache",
            AccessTools.Field(typeof(ModSettings_GiddyUp), nameof(ModSettings_GiddyUp.MountableCache)) != null);
        yield return ("FactionRestrictions.allowedNonWildAnimals", AllowedNonWildField != null);
        yield return ("FactionRestrictions.allowedWildAnimals", AllowedWildField != null);
        yield return ("ExtendedPawnData.ReservedMount",
            AccessTools.Property(typeof(ExtendedPawnData), nameof(ExtendedPawnData.ReservedMount)) != null);
    }

    public static bool IsMounted(
        Pawn pawn
    ) => pawn.IsMounted();

    public static Pawn MountOf(
        Pawn rider
    ) => rider.GetExtendedPawnData()?.Mount;

    // A caravan pawn isn't mounted, but Giddy-Up pairs riders with mounts when the caravan forms (ReservedMount, which
    // is saved; selectedForCaravan isn't, and is never cleared). Counts as riding while that mount travels with them.
    public static bool RidesInCaravan(
        Pawn pawn
    )
    {
        if (!ModSettings_GiddyUp.caravansEnabled)
        {
            return false;
        }

        var caravan = pawn.GetCaravan();
        if (caravan == null)
        {
            return false;
        }

        var mount = pawn.GetExtendedPawnData()?.ReservedMount;
        return mount is { Dead: false } && mount.GetCaravan() == caravan;
    }

    public static bool CanEverRide(
        Pawn pawn
    ) => pawn.RaceProps.Humanlike && pawn.IsCapableOfRiding(out _);

    // Could the rider use this animal, setting aside what it's doing right now: Giddy-Up's mod options, faction,
    // training, and the animal's "who may ride" setting
    public static bool CouldRide(
        Pawn rider,
        Pawn animal
    ) =>
        animal.IsMountable(out _, rider, checkState: false, checkFaction: true, checkTraining: true)
        && rider.IsAllowed(animal);

    // Races Giddy-Up lets anyone ride: big enough, or marked mountable, and not switched off in its settings. Keyed by
    // the race's ThingDef.shortHash.
    public static bool IsMountableRace(
        ThingDef race
    ) => race?.race is { Animal: true } && ModSettings_GiddyUp.MountableCache.Contains(race.shortHash);

    // The animals a faction def limits its mounts to, through Giddy-Up's FactionRestrictions extension; empty if none
    public static IEnumerable<ThingDef> FactionMountRaces(
        FactionDef faction
    )
    {
        var rules = faction?.modExtensions?.FirstOrDefault(extension => extension.GetType() == FactionRestrictionsType);
        if (rules == null)
        {
            yield break;
        }

        var kinds = ((AllowedNonWildField?.GetValue(rules) as List<PawnKindDef>) ?? new List<PawnKindDef>())
            .Concat((AllowedWildField?.GetValue(rules) as List<PawnKindDef>) ?? new List<PawnKindDef>());
        foreach (var kind in kinds)
        {
            if (kind?.race != null)
            {
                yield return kind.race;
            }
        }
    }

    // Mounts at once, as when Giddy-Up's mount job finishes. For tests.
    public static void MountNow(
        Pawn rider,
        Pawn animal
    ) => rider.GoMount(animal, MountUtility.GiveJobMethod.Instant);

    // For tests
    public static void DismountNow(
        Pawn rider
    )
    {
        var data = rider.GetExtendedPawnData();
        rider.Dismount(data.Mount, data);
    }

    public static bool BattleMountsEnabled => ModSettings_GiddyUp.battleMountsEnabled;

    public static int EnemyMountChance
    {
        get => ModSettings_GiddyUp.enemyMountChance;
        set => ModSettings_GiddyUp.enemyMountChance = value;
    }

    public static int EnemyMountChancePreInd
    {
        get => ModSettings_GiddyUp.enemyMountChancePreInd;
        set => ModSettings_GiddyUp.enemyMountChancePreInd = value;
    }
}
