using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Ordo.Saddlebound;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Riding is driven through Giddy-Up's own Mount and Dismount, so the patches are exercised for real. Time spent off a mount
// is faked by backdating the tracker, not by stepping days. Each scenario sets the colonists' ideoligion's precepts for the
// issue it tests and puts the old ones back afterwards.
public static class SaddleboundScenarios
{
    // Suites: "setup" for loading and the Giddy-Up API, "riding" for the Riding issue, "mounts" for mount death,
    // "glory" for mounted kills, "raids" for Horde Riders raids
    public static readonly List<Scenario> All = new()
    {
        new("setup_no_load_errors", "setup", NoLoadErrors),
        new("setup_giddyup_api", "setup", GiddyUpApi),
        new("riding_tracks_mount_and_dismount", "riding", TracksMountAndDismount),
        new("riding_save_forgets_gone_pawns", "riding save", SaveForgetsGonePawns),
        new("riding_respected_thought", "riding", RespectedThought),
        new("riding_essential_stages", "riding", EssentialStages),
        new("riding_essential_long_ride", "riding", EssentialLongRide),
        new("riding_essential_suppressed_no_mounts", "riding", EssentialSuppressedNoMounts),
        new("riding_essential_mount_on_other_level", "riding levels", EssentialMountOnOtherLevel, requiresMod: "telardo.MultiFloors"),
        new("riding_disliked_memory", "riding", DislikedMemory),
        new("mount_death_memory", "mounts", MountDeathMemory),
        new("mount_death_unridden_ignored", "mounts", MountDeathUnridden),
        new("mounted_kill_glory", "glory", MountedKillGlory),
        new("horde_raid_mount_chance", "raids", HordeRaidMountChance),
        new("preferred_mount_choices", "preferred setup", PreferredMountChoices),
        new("preferred_mount_memory", "preferred", PreferredMountMemory),
        new("preferred_mount_lesser_needs_essential", "preferred", LesserNeedsEssential),
        new("preferred_mount_none_no_memory", "preferred", NoPreferredNoMemory),
        new("preferred_mount_generation", "preferred", PreferredMountGeneration),
    };

    private const string LesserKind = "Dromedary";

    private const string MountKind = "Horse";

    private static PreceptDef Precept(
        string defName
    ) => DefDatabase<PreceptDef>.GetNamed(defName);

    private static ThoughtDef Thought(
        string defName
    ) => DefDatabase<ThoughtDef>.GetNamed(defName);

    private static int Now => Find.TickManager.TicksGame;

    // Gives the ideoligion this precept in place of any others for its issue; disposing puts the old ones back
    private sealed class PreceptSwap : IDisposable
    {
        private readonly Ideo _ideo;
        private readonly Precept _added;
        private readonly List<Precept> _removed;

        public PreceptSwap(
            Ideo ideo,
            PreceptDef def,
            ThingDef thingDef = null
        )
        {
            _ideo = ideo;
            _removed = ideo.PreceptsListForReading.Where(precept => precept.def.issue == def.issue).ToList();
            foreach (var precept in _removed)
            {
                ideo.RemovePrecept(precept);
            }

            _added = PreceptMaker.MakePrecept(def);
            ideo.AddPrecept(_added, init: true);
            if (thingDef != null && _added is Precept_ThingDef withThing)
            {
                withThing.ThingDef = thingDef;
            }
        }

        // Removes every precept of the issue, adding nothing
        public static IDisposable Clear(
            Ideo ideo,
            IssueDef issue
        )
        {
            var removed = ideo.PreceptsListForReading.Where(precept => precept.def.issue == issue).ToList();
            foreach (var precept in removed)
            {
                ideo.RemovePrecept(precept);
            }

            return new Restore(() =>
            {
                foreach (var precept in removed)
                {
                    ideo.AddPrecept(precept);
                }
            });
        }

        public void Dispose()
        {
            _ideo.RemovePrecept(_added);
            foreach (var precept in _removed)
            {
                _ideo.AddPrecept(precept);
            }
        }
    }

    private sealed class Restore(
        Action restore
    ) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static PreceptSwap Use(
        Pawn colonist,
        string preceptDefName
    ) => new(colonist.Ideo, Precept(preceptDefName));

    private static PreceptSwap Prefer(
        Pawn colonist,
        string raceDefName
    ) => new(colonist.Ideo, Precept("Ordo_PreferredMount"), DefDatabase<ThingDef>.GetNamed(raceDefName));

    private static IssueDef PreferredMountsIssue => DefDatabase<IssueDef>.GetNamed("Ordo_PreferredMounts");

    private static ThoughtState StateOf(
        Pawn pawn,
        string thoughtDefName
    )
    {
        RidingTracker.Instance.ClearRideableCache();
        return Thought(thoughtDefName).Worker.CurrentState(pawn);
    }

    private static string Describe(
        ThoughtState state
    ) => state.Active ? $"active at stage {state.StageIndex}" : "inactive";

    private static bool HasMemory(
        Pawn pawn,
        string thoughtDefName
    ) => pawn.needs.mood.thoughts.memories.GetFirstMemoryOfDef(Thought(thoughtDefName)) != null;

    private static Pawn Horse(
        ScenarioContext context,
        int x,
        int z
    ) => context.Spawn(MountKind, x, z, PawnMode.Trained);

    private static Task NoLoadErrors(
        ScenarioContext context
    )
    {
        var errors = Log.Messages
            .Where(message => message.type == LogMessageType.Error)
            .Select(message => message.text)
            .Where(text => text.Contains("Saddlebound") || text.Contains("Ordo_"))
            .ToList();
        context.Check($"no load errors mention the mod: [{string.Join(" | ", errors)}]", errors.Count == 0);

        context.Check("the Horde Riders meme loaded", SaddleboundDefOf.Ordo_HordeRiders != null);
        context.Check("the player has an ideoligion to test with", Faction.OfPlayer.ideos?.PrimaryIdeo != null);
        return Task.CompletedTask;
    }

    private static Task GiddyUpApi(
        ScenarioContext context
    )
    {
        var missing = GiddyUpAdapter.Members().Where(member => !member.found).Select(member => member.name).ToList();
        context.Check($"every Giddy-Up member the adapter uses resolves; missing: [{string.Join(", ", missing)}]", missing.Count == 0);

        void HasPatch(
            string what,
            System.Reflection.MethodBase method
        )
        {
            var info = method == null ? null : Harmony.GetPatchInfo(method);
            var owners = info == null
                ? new List<string>()
                : info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers).Select(patch => patch.owner).Distinct().ToList();
            context.Check($"{what} is patched by the mod; owners: [{string.Join(", ", owners)}]", owners.Contains("Ordo.Saddlebound"));
        }

        HasPatch("MountUtility.Mount", GiddyUpAdapter.MountMethod);
        HasPatch("MountUtility.Dismount", GiddyUpAdapter.DismountMethod);
        HasPatch("MountUtility.GenerateMounts", GiddyUpAdapter.GenerateMountsMethod);
        HasPatch("Pawn.Kill", AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill)));
        return Task.CompletedTask;
    }

    private static async Task TracksMountAndDismount(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        var tracker = RidingTracker.Instance;

        GiddyUpAdapter.MountNow(rider, horse);
        context.Check("the rider is mounted", GiddyUpAdapter.IsMounted(rider));
        context.Check("the mount is the horse", GiddyUpAdapter.MountOf(rider) == horse);
        context.Check("the tracker saw the mount", tracker.TryGetLastRideTick(rider, out var mountedAt) && mountedAt == Now);
        context.Check("the horse has a record with one ride by the rider", tracker.RecordFor(horse)?.RideCount(rider) == 1);
        context.Check("the rider is the horse's usual rider", tracker.RecordFor(horse)?.UsualRider() == rider);

        await context.Step(30);
        GiddyUpAdapter.DismountNow(rider);
        context.Check("the rider is off", !GiddyUpAdapter.IsMounted(rider));
        context.Check($"dismounting updates the last ride tick", tracker.TryGetLastRideTick(rider, out var dismountedAt) && dismountedAt == Now && dismountedAt > mountedAt);
    }

    // Saving drops the records of pawns the game no longer has, as when a fled raider is discarded, and keeps the rest
    private static Task SaveForgetsGonePawns(
        ScenarioContext context
    )
    {
        var tracker = RidingTracker.Instance;
        var stays = context.Colonist(0, 0);
        var goes = context.Colonist(2, 0);
        var stayingHorse = Horse(context, 1, 0);
        var goingHorse = Horse(context, 3, 0);
        tracker.Notify_Mounted(stays, stayingHorse);
        tracker.Notify_Mounted(goes, stayingHorse);
        tracker.Notify_Mounted(goes, goingHorse);

        // Gone as the game discards a pawn it no longer needs, not dead, which the tracker already handles
        foreach (var pawn in new[] { goes, goingHorse })
        {
            pawn.Destroy();
            if (Find.WorldPawns.Contains(pawn))
            {
                Find.WorldPawns.RemovePawn(pawn);
            }

            pawn.Discard(true);
        }

        context.Check("before saving, the gone pawns are still recorded",
            tracker.TryGetLastRideTick(goes, out _) && tracker.HasBeenRidden(goingHorse) && tracker.RecordFor(stayingHorse)?.RideCount(goes) == 1);
        Scribe.saver.DebugOutputFor(tracker);
        context.Check("saved, the gone rider has no last ride", !tracker.TryGetLastRideTick(goes, out _));
        context.Check("nor the gone horse a record", !tracker.HasBeenRidden(goingHorse));
        context.Check("nor the gone rider rides on the staying horse", tracker.RecordFor(stayingHorse)?.RideCount(goes) == 0);
        context.Check("the staying rider and horse are kept",
            tracker.TryGetLastRideTick(stays, out _) && tracker.RecordFor(stayingHorse)?.RideCount(stays) == 1);
        return Task.CompletedTask;
    }

    private static Task RespectedThought(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_Riding_Respected");

        GiddyUpAdapter.MountNow(rider, horse);
        var riding = StateOf(rider, "Ordo_RodeRecently");
        context.Check($"riding gives rode recently: {Describe(riding)}", riding.Active);

        GiddyUpAdapter.DismountNow(rider);
        RidingTracker.Instance.SetLastRideTick(rider, Now - GenDate.TicksPerDay + 100);
        var within = StateOf(rider, "Ordo_RodeRecently");
        context.Check($"it lasts for a day after: {Describe(within)}", within.Active);

        RidingTracker.Instance.SetLastRideTick(rider, Now - GenDate.TicksPerDay - 100);
        var after = StateOf(rider, "Ordo_RodeRecently");
        context.Check($"it's gone after a day: {Describe(after)}", !after.Active);

        var essential = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"Respected doesn't give the Essential thought: {Describe(essential)}", !essential.Active);
        return Task.CompletedTask;
    }

    private static Task EssentialStages(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_Riding_Essential");
        var tracker = RidingTracker.Instance;

        (int daysAgo, bool active, int stage, string what)[] cases =
        {
            (0, true, 0, "just rode: rode recently"),
            (1, false, 0, "a day and a bit: nothing yet"),
            (3, true, 1, "3 days: haven't ridden"),
            (5, true, 2, "5 days: long without riding"),
        };

        foreach (var (daysAgo, active, stage, what) in cases)
        {
            tracker.SetLastRideTick(rider, Now - daysAgo * GenDate.TicksPerDay - (daysAgo > 0 ? 100 : 0));
            var state = StateOf(rider, "Ordo_RidingEssential");
            context.Check($"{what}; got {Describe(state)}", state.Active == active && (!active || state.StageIndex == stage));
        }

        return Task.CompletedTask;
    }

    // Riding for days without dismounting is riding, not "days since the last mount"
    private static Task EssentialLongRide(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_Riding_Essential");

        GiddyUpAdapter.MountNow(rider, horse);
        RidingTracker.Instance.SetLastRideTick(rider, Now - 5 * GenDate.TicksPerDay);
        var state = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"a pawn mounted 5 days ago and still riding has rode recently: {Describe(state)}", state.Active && state.StageIndex == 0);
        return Task.CompletedTask;
    }

    private static Task EssentialSuppressedNoMounts(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        using var _ = Use(rider, "Ordo_Riding_Essential");
        var tracker = RidingTracker.Instance;

        tracker.SetLastRideTick(rider, Now - 5 * GenDate.TicksPerDay);
        var none = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"no penalty with no animal to ride: {Describe(none)}", !none.Active);

        // A wild horse can't be ridden, and an untrained one fails Giddy-Up's tameness check
        context.Spawn(MountKind, 2, 0, PawnMode.Wild);
        var wild = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"no penalty with only a wild horse: {Describe(wild)}", !wild.Active);

        Horse(context, 1, 0);
        var tame = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"the penalty comes back with a trained horse: {Describe(tame)}", tame.Active && tame.StageIndex == 2);
        return Task.CompletedTask;
    }

    // With MultiFloors, a trained horse on the level above is still one the pawn could ride, so the penalty applies
    private static Task EssentialMountOnOtherLevel(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        using var _ = Use(rider, "Ordo_Riding_Essential");
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return Task.CompletedTask;
        }

        RidingTracker.Instance.SetLastRideTick(rider, Now - 5 * GenDate.TicksPerDay);
        var none = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"no penalty with no animal on either level: {Describe(none)}", !none.Active);

        var horse = Horse(context, 1, 0);
        level.MoveHere(horse, context.Cell(10, 10));
        context.Check($"the horse is upstairs: {horse.Map}", horse.Map == level.Map);
        var upstairs = StateOf(rider, "Ordo_RidingEssential");
        context.Check($"the penalty applies with a trained horse upstairs: {Describe(upstairs)}", upstairs.Active && upstairs.StageIndex == 2);
        return Task.CompletedTask;
    }

    private static Task DislikedMemory(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_Riding_Disliked");

        context.Check("no had to ride before mounting", !HasMemory(rider, "Ordo_HadToRide"));
        GiddyUpAdapter.MountNow(rider, horse);
        context.Check("mounting gives had to ride", HasMemory(rider, "Ordo_HadToRide"));
        var respected = StateOf(rider, "Ordo_RodeRecently");
        context.Check($"Disliked doesn't give rode recently: {Describe(respected)}", !respected.Active);
        return Task.CompletedTask;
    }

    private static async Task MountDeathMemory(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var other = context.Colonist(0, 2);
        var horse = Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_MountDeath_Mourned");

        GiddyUpAdapter.MountNow(rider, horse);
        await context.Step(10);
        GiddyUpAdapter.DismountNow(rider);

        horse.Kill(null);
        context.Check("the horse is dead", horse.Dead);
        context.Check("the rider got our mount died", HasMemory(rider, "Ordo_MountDied"));
        context.Check("the other colonist got our mount died", HasMemory(other, "Ordo_MountDied"));
        context.Check("the rider got my mount died", HasMemory(rider, "Ordo_OwnMountDied"));
        context.Check("the other colonist didn't get my mount died", !HasMemory(other, "Ordo_OwnMountDied"));
        context.Check("the tracker dropped the dead mount's record", !RidingTracker.Instance.HasBeenRidden(horse));
    }

    private static Task MountDeathUnridden(
        ScenarioContext context
    )
    {
        var colonist = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        using var _ = Use(colonist, "Ordo_MountDeath_Mourned");

        horse.Kill(null);
        context.Check("a horse no one rode isn't mourned as a mount", !HasMemory(colonist, "Ordo_MountDied"));
        return Task.CompletedTask;
    }

    private static Task MountedKillGlory(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var walker = context.Colonist(0, 3);
        var horse = Horse(context, 1, 0);
        using var _ = Use(rider, "Ordo_MountedGlory_Glorious");
        GiddyUpAdapter.MountNow(rider, horse);

        var enemyFaction = Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
        Pawn Enemy(
            int x,
            int z
        )
        {
            var pawn = context.Spawn("Villager", x, z, PawnMode.Wild);
            pawn.SetFaction(enemyFaction);
            return pawn;
        }

        var first = Enemy(5, 0);
        first.Kill(new DamageInfo(DamageDefOf.Cut, 100f, instigator: rider));
        context.Check("a kill from the saddle gives killed from the saddle", HasMemory(rider, "Ordo_KilledFromSaddle"));

        var second = Enemy(5, 3);
        second.Kill(new DamageInfo(DamageDefOf.Cut, 100f, instigator: walker));
        context.Check("a kill on foot doesn't", !HasMemory(walker, "Ordo_KilledFromSaddle"));

        var friend = context.Colonist(4, 0);
        friend.drafter.Drafted = false;
        var before = rider.needs.mood.thoughts.memories.Memories.Count(m => m.def == Thought("Ordo_KilledFromSaddle"));
        friend.Kill(new DamageInfo(DamageDefOf.Cut, 100f, instigator: rider));
        var after = rider.needs.mood.thoughts.memories.Memories.Count(m => m.def == Thought("Ordo_KilledFromSaddle"));
        context.Check($"killing a friend from the saddle isn't glorious: {before} -> {after}", after == before);
        return Task.CompletedTask;
    }

    // GenerateMounts is called with a test prefix that runs after the mod's, reads the enemy mount chance Giddy-Up would
    // use, and skips the original, so no raid is generated
    private static Task HordeRaidMountChance(
        ScenarioContext context
    )
    {
        var faction = Find.FactionManager.AllFactionsListForReading
            .FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer) && f.ideos?.PrimaryIdeo != null);
        if (faction == null)
        {
            context.Fail("no hostile humanlike faction with an ideoligion");
            return Task.CompletedTask;
        }

        var ideo = faction.ideos.PrimaryIdeo;
        var meme = SaddleboundDefOf.Ordo_HordeRiders;
        var hadMeme = ideo.memes.Contains(meme);
        var original = (GiddyUpAdapter.EnemyMountChance, GiddyUpAdapter.EnemyMountChancePreInd);
        var settingWas = SaddleboundMod.Settings.hordeRaids;
        var harmony = new Harmony("Ordo.BridgeTools.SaddleboundTest");
        try
        {
            harmony.Patch(
                GiddyUpAdapter.GenerateMountsMethod,
                prefix: new HarmonyMethod(typeof(SaddleboundScenarios), nameof(ReadChancePrefix)) { priority = Priority.Last }
            );

            (int, int) Seen()
            {
                _seenChance = null;
                var pawns = new List<Pawn>();
                GiddyUpAdapter.GenerateMountsMethod.Invoke(null, new object[] { pawns, new IncidentParms { faction = faction, target = context.Map } });
                return _seenChance ?? (-1, -1);
            }

            ideo.memes.Remove(meme);
            var plain = Seen();
            context.Check($"a faction without the meme uses Giddy-Up's chance {original}: saw {plain}", plain == original);

            ideo.memes.Add(meme);
            SaddleboundMod.Settings.hordeRaids = true;
            var horde = Seen();
            var expected = (SaddleboundSettings.HordeMountChance, SaddleboundSettings.HordeMountChance);
            context.Check($"a Horde Riders faction uses {expected}: saw {horde}", horde == expected);
            var afterHorde = (GiddyUpAdapter.EnemyMountChance, GiddyUpAdapter.EnemyMountChancePreInd);
            context.Check($"Giddy-Up's settings are put back {original}: now {afterHorde}", afterHorde == original);

            SaddleboundMod.Settings.hordeRaids = false;
            var off = Seen();
            context.Check($"with the setting off, Horde Riders use Giddy-Up's chance: saw {off}", off == original);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            SaddleboundMod.Settings.hordeRaids = settingWas;
            ideo.memes.Remove(meme);
            if (hadMeme)
            {
                ideo.memes.Add(meme);
            }

            GiddyUpAdapter.EnemyMountChance = original.Item1;
            GiddyUpAdapter.EnemyMountChancePreInd = original.Item2;
        }

        return Task.CompletedTask;
    }

    private static Task PreferredMountChoices(
        ScenarioContext context
    )
    {
        var def = Precept("Ordo_PreferredMount");
        var choices = def.Worker.ThingDefs.Select(choice => choice.def).ToList();
        var horse = DefDatabase<ThingDef>.GetNamed(MountKind);
        context.Check($"the editor offers the horse among {choices.Count} species", choices.Contains(horse));
        context.Check("and the dromedary", choices.Contains(DefDatabase<ThingDef>.GetNamed(LesserKind)));
        context.Check("not the chicken, which Giddy-Up won't let anyone ride", !choices.Contains(DefDatabase<ThingDef>.GetNamed("Chicken")));
        var notMountable = choices.Where(race => !GiddyUpAdapter.IsMountableRace(race)).Select(race => race.defName).ToList();
        context.Check($"every choice is mountable; not: [{string.Join(", ", notMountable)}]", notMountable.Count == 0);

        var colonist = context.Colonist(0, 0);
        using var _ = Prefer(colonist, MountKind);
        context.Check("a species already preferred can't be picked twice", !def.Worker.CanUse(horse, colonist.Ideo, null).Accepted);
        context.Check("the precept shows its species", PreferredMounts.For(colonist.Ideo).SequenceEqual(new[] { horse }));
        context.Check("preferred mounts aren't venerated animals", !colonist.Ideo.IsVeneratedAnimal(horse));
        return Task.CompletedTask;
    }

    private static async Task PreferredMountMemory(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        var dromedary = context.Spawn(LesserKind, 2, 0, PawnMode.Trained);
        using var essential = Use(rider, "Ordo_Riding_Essential");
        using var preferred = Prefer(rider, MountKind);

        GiddyUpAdapter.MountNow(rider, horse);
        context.Check("riding the preferred horse gives rode a preferred mount", HasMemory(rider, "Ordo_RodePreferredMount"));
        context.Check("and not rode a lesser mount", !HasMemory(rider, "Ordo_RodeLesserMount"));
        GiddyUpAdapter.DismountNow(rider);
        await context.Step(10);

        GiddyUpAdapter.MountNow(rider, dromedary);
        context.Check($"riding a dromedary with riding essential gives rode a lesser mount; mounted: {GiddyUpAdapter.MountOf(rider)?.LabelShort}",
            HasMemory(rider, "Ordo_RodeLesserMount"));
    }

    private static Task LesserNeedsEssential(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var dromedary = context.Spawn(LesserKind, 1, 0, PawnMode.Trained);
        using var respected = Use(rider, "Ordo_Riding_Respected");
        using var preferred = Prefer(rider, MountKind);

        GiddyUpAdapter.MountNow(rider, dromedary);
        context.Check("with riding only respected, another species gives no lesser mount memory", !HasMemory(rider, "Ordo_RodeLesserMount"));
        context.Check("nor a preferred one", !HasMemory(rider, "Ordo_RodePreferredMount"));
        return Task.CompletedTask;
    }

    private static Task NoPreferredNoMemory(
        ScenarioContext context
    )
    {
        var rider = context.Colonist(0, 0);
        var horse = Horse(context, 1, 0);
        using var essential = Use(rider, "Ordo_Riding_Essential");
        using var none = PreceptSwap.Clear(rider.Ideo, PreferredMountsIssue);

        GiddyUpAdapter.MountNow(rider, horse);
        context.Check("without preferred mounts, no lesser mount memory", !HasMemory(rider, "Ordo_RodeLesserMount"));
        context.Check("and no preferred mount memory", !HasMemory(rider, "Ordo_RodePreferredMount"));
        return Task.CompletedTask;
    }

    // Generates throwaway ideoligions the way the game does, through RandomizePrecepts
    private static Task PreferredMountGeneration(
        ScenarioContext context
    )
    {
        var def = Precept("Ordo_PreferredMount");
        var meme = SaddleboundDefOf.Ordo_HordeRiders;
        var faction = FactionDefOf.TribeRough;
        Rand.PushState(1234);
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var horde = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(faction, forcedMemes: new List<MemeDef> { meme }));
                var mounts = PreferredMounts.For(horde).ToList();
                context.Check($"Horde Riders ideoligion {i} has 2 preferred mounts: [{string.Join(", ", mounts.Select(m => m.defName))}]",
                    horde.GetPreceptCountOfDef(def) == 2 && mounts.Count == 2 && mounts.Distinct().Count() == 2);
                context.Check($"its species are all mountable", mounts.All(GiddyUpAdapter.IsMountableRace));
                context.Check("it has riding essential", horde.HasPrecept(SaddleboundDefOf.Ordo_Riding_Essential));
            }

            var plain = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(
                faction,
                disallowedMemes: new List<MemeDef> { meme },
                disallowedPrecepts: new List<PreceptDef> { SaddleboundDefOf.Ordo_Riding_Respected, SaddleboundDefOf.Ordo_Riding_Essential }
            ));
            context.Check($"an ideoligion that doesn't value riding has none: {plain.GetPreceptCountOfDef(def)}", plain.GetPreceptCountOfDef(def) == 0);

            var respected = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(faction, disallowedMemes: new List<MemeDef> { meme }));
            var expected = PreferredMounts.CountToGenerate(respected);
            context.Check($"any other ideoligion gets as many as its riding precepts call for ({expected}): {respected.GetPreceptCountOfDef(def)}",
                respected.GetPreceptCountOfDef(def) == expected);
        }
        finally
        {
            Rand.PopState();
        }

        return Task.CompletedTask;
    }

    private static (int, int)? _seenChance;

    public static bool ReadChancePrefix(
        ref bool __result
    )
    {
        _seenChance = (GiddyUpAdapter.EnemyMountChance, GiddyUpAdapter.EnemyMountChancePreInd);
        __result = false;
        return false;
    }
}
