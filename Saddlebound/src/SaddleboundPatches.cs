using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.Saddlebound;

public static class SaddleboundPatches
{
    public static void Apply(
        Harmony harmony
    )
    {
        harmony.Patch(
            GiddyUpAdapter.MountMethod,
            postfix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Postfix_Mount))
        );
        harmony.Patch(
            GiddyUpAdapter.DismountMethod,
            postfix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Postfix_Dismount))
        );
        harmony.Patch(
            AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)),
            prefix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Prefix_Kill)),
            postfix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Postfix_Kill))
        );
        harmony.Patch(
            AccessTools.Method(typeof(IdeoFoundation), nameof(IdeoFoundation.RandomizePrecepts)),
            postfix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Postfix_RandomizePrecepts))
        );
        harmony.Patch(
            AccessTools.Method(typeof(IdeoUIUtility), nameof(IdeoUIUtility.DoPrecepts)),
            postfix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Postfix_DoPrecepts))
        );
        harmony.Patch(
            GiddyUpAdapter.GenerateMountsMethod,
            prefix: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Prefix_GenerateMounts)),
            finalizer: new HarmonyMethod(typeof(SaddleboundPatches), nameof(Finalizer_GenerateMounts))
        );
    }

    public static void Postfix_Mount(
        Pawn rider
    )
    {
        var mount = GiddyUpAdapter.MountOf(rider);
        if (mount == null)
        {
            return;
        }

        RidingTracker.Instance?.Notify_Mounted(rider, mount);
        // Only the player's pawns: mounted raiders spawn by the dozen and no precept here reacts to them
        var ideo = rider.Ideo;
        if (ideo == null || !rider.CountsAsNonNPCForPrecepts())
        {
            return;
        }

        Find.HistoryEventsManager.RecordEvent(
            new HistoryEvent(SaddleboundDefOf.Ordo_Mounted, rider.Named(HistoryEventArgsNames.Doer))
        );
        if (!PreferredMounts.HasAny(ideo))
        {
            return;
        }

        if (PreferredMounts.IsPreferred(ideo, mount.def))
        {
            Find.HistoryEventsManager.RecordEvent(
                new HistoryEvent(SaddleboundDefOf.Ordo_RodePreferredMount, rider.Named(HistoryEventArgsNames.Doer))
            );
        }
        else if (ideo.HasPrecept(SaddleboundDefOf.Ordo_Riding_Essential))
        {
            Find.HistoryEventsManager.RecordEvent(
                new HistoryEvent(SaddleboundDefOf.Ordo_RodeLesserMount, rider.Named(HistoryEventArgsNames.Doer))
            );
        }
    }

    public static void Postfix_RandomizePrecepts(
        IdeoFoundation __instance,
        IdeoGenerationParms parms
    ) => PreferredMounts.Generate(__instance.ideo, parms.forFaction);

    // The editor lists precepts by exact class, so preferred mounts need their own section, drawn with vanilla's own
    // section code
    public static void Postfix_DoPrecepts(
        ref float curY,
        float width,
        Ideo ideo,
        IdeoEditMode editMode
    ) => IdeoUIUtility.DoPreceptsInt(
        "Ordo_Saddlebound_PreferredMounts".Translate(),
        "Ordo_Saddlebound_Mount".Translate(),
        mainPrecepts: false,
        ideo,
        editMode,
        ref curY,
        width,
        def => def.preceptClass == typeof(Precept_PreferredMount)
    );

    public static void Postfix_Dismount(
        Pawn rider
    ) => RidingTracker.Instance?.Notify_Dismounted(rider);

    public struct KillState
    {
        public bool mountDeath;
        public Pawn usualRider;
        public Pawn mountedKiller;
    }

    // Decided before the kill: after it, the victim is dead and its rider has been thrown off
    public static void Prefix_Kill(
        Pawn __instance,
        DamageInfo? dinfo,
        out KillState __state
    )
    {
        __state = default;
        if (__instance.Dead)
        {
            return;
        }

        var tracker = RidingTracker.Instance;
        if (tracker == null)
        {
            return;
        }

        if (__instance.RaceProps.Animal && __instance.Faction is { IsPlayer: true } && tracker.HasBeenRidden(__instance))
        {
            __state.mountDeath = true;
            __state.usualRider = tracker.RecordFor(__instance)?.UsualRider();
        }

        if (dinfo?.Instigator is Pawn killer
            && killer != __instance
            && killer.RaceProps.Humanlike
            && GiddyUpAdapter.IsMounted(killer)
            && __instance.HostileTo(killer))
        {
            __state.mountedKiller = killer;
        }
    }

    public static void Postfix_Kill(
        Pawn __instance,
        KillState __state
    )
    {
        if (!__instance.Dead)
        {
            return;
        }

        RidingTracker.Instance?.Notify_Died(__instance);
        if (__state.mountDeath)
        {
            Find.HistoryEventsManager.RecordEvent(
                new HistoryEvent(SaddleboundDefOf.Ordo_MountDied, __instance.Named(HistoryEventArgsNames.Victim))
            );
            if (__state.usualRider is { Dead: false } rider)
            {
                Find.HistoryEventsManager.RecordEvent(
                    new HistoryEvent(
                        SaddleboundDefOf.Ordo_OwnMountDied,
                        rider.Named(HistoryEventArgsNames.Doer),
                        __instance.Named(HistoryEventArgsNames.Victim)
                    )
                );
            }
        }

        if (__state.mountedKiller is { Dead: false } killer)
        {
            Find.HistoryEventsManager.RecordEvent(
                new HistoryEvent(
                    SaddleboundDefOf.Ordo_KilledFromSaddle,
                    killer.Named(HistoryEventArgsNames.Doer),
                    __instance.Named(HistoryEventArgsNames.Victim)
                )
            );
        }
    }

    // Horde Riders factions raid mostly mounted: Giddy-Up reads its enemy mount chance inside GenerateMounts, so it's
    // raised for that one call and put back afterwards, even if Giddy-Up throws. A faction's or pawn kind's own
    // override still wins.
    public struct MountChanceState
    {
        public bool changed;
        public int chance;
        public int chancePreInd;
    }

    public static void Prefix_GenerateMounts(
        IncidentParms parms,
        out MountChanceState __state
    )
    {
        __state = default;
        if (!SaddleboundMod.Settings.hordeRaids || !IsHorde(parms?.faction))
        {
            return;
        }

        __state = new MountChanceState
        {
            changed = true,
            chance = GiddyUpAdapter.EnemyMountChance,
            chancePreInd = GiddyUpAdapter.EnemyMountChancePreInd,
        };
        GiddyUpAdapter.EnemyMountChance = SaddleboundSettings.HordeMountChance;
        GiddyUpAdapter.EnemyMountChancePreInd = SaddleboundSettings.HordeMountChance;
    }

    public static void Finalizer_GenerateMounts(
        MountChanceState __state
    )
    {
        if (!__state.changed)
        {
            return;
        }

        GiddyUpAdapter.EnemyMountChance = __state.chance;
        GiddyUpAdapter.EnemyMountChancePreInd = __state.chancePreInd;
    }

    public static bool IsHorde(
        Faction faction
    ) =>
        faction is { IsPlayer: false }
        && faction.ideos?.PrimaryIdeo is { } ideo
        && ideo.HasMeme(SaddleboundDefOf.Ordo_HordeRiders);
}
