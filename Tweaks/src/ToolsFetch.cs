using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using PeteTimesSix.SimpleSidearms;
using PeteTimesSix.SimpleSidearms.Utilities;
using RimWorld;
using SimpleSidearms.rimworld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

// Colonists fetch a tool from storage for the work they mainly do, and put it back in storage when it's no longer theirs.
// Simple Sidearms carries it: its EquipSecondary job picks it up and remembers it, and its weight and slot limits apply.
//
// The assignment is recomputed every 2500 ticks (MapComponent_ToolAssignments); JobGiver_FetchTool, just before normal work
// in the colonist think tree, acts on it. Without Simple Sidearms the setting is false, so nothing reaches its types.
public static class ToolsFetch
{
    public const int RefreshInterval = 2500;

    // How long work stays "main" after it leaves the pawn's main band, before its tool goes back
    public const int MainWorkGraceTicks = GenDate.TicksPerDay;

    // A pawn swaps the tool they have only for one that beats it by this much
    public const float BetterBy = 1.1f;

    // Every tool Simple Sidearms remembers adding comes through here: Tweaks' own fetch, an order from the player, a trade, or
    // Auto Arm swapping a sidearm for a better copy. Simple Sidearms' re-fetch of a lost sidearm doesn't (it doesn't memorise).
    public static void Postfix_InformOfAddedSidearm(ThingComp __instance, Thing weapon)
    {
        if (weapon is not ThingWithComps tool || __instance.parent is not Pawn pawn)
        {
            return;
        }

        if (GameComponent_FetchedTools.TakePending(pawn, tool))
        {
            GameComponent_FetchedTools.Remember(tool);
        }
        else if (GameComponent_FetchedTools.IsFetched(tool))
        {
            // Fetched for someone else, or once by this pawn and since lost, and now picked up another way: the player's
            GameComponent_FetchedTools.Forget(tool);
        }
        else if (GameComponent_FetchedTools.ReplacesFetched(pawn, tool, out var replaced))
        {
            // Auto Arm upgraded a fetched tool to a better copy: the copy is fetched in its place
            GameComponent_FetchedTools.Forget(replaced);
            GameComponent_FetchedTools.Remember(tool);
        }

        if (GameComponent_FetchedTools.IsFetched(tool))
        {
            GameComponent_FetchedTools.NoteCarried(pawn, tool);
            AutoArmForced.Add(pawn, tool);
        }
    }

    // Who fetches: free adult colonists Simple Sidearms carries for, not drafted, downed or in a mental state, on a home map
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool CanFetch(Pawn pawn) =>
        pawn.Spawned
        && pawn.IsFreeColonist
        && pawn.DevelopmentalStage.Adult()
        && !pawn.Drafted
        && !pawn.Downed
        && !pawn.InMentalState
        && pawn.Map.IsPlayerHome
        && pawn.IsValidSidearmsCarrierRightNow();

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static List<ThingWithComps> CarriedTools(Pawn pawn) =>
        pawn.GetCarriedWeapons(includeEquipped: true, includeTools: true).Where(weapon => ToolsCategory.IsTool(weapon.def)).ToList();

    // Simple Sidearms' weight, slot and other limits, and a tool the pawn could use beside their shield or off-hand weapon
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static bool CanPickUp(Pawn pawn, ThingWithComps tool) =>
        StatCalculator.CanPickupSidearmInstance(tool, pawn, out _) && ToolsForWork.Switching.UsableBeside(pawn, tool);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Job FetchJob(ThingWithComps tool) => JobMaker.MakeJob(SidearmsDefOf.EquipSecondary, tool);

    // Dropped and carried to storage. Intentional, so Simple Sidearms forgets it and doesn't fetch it again. Only when some
    // storage takes it: otherwise the pawn keeps it rather than leave it on the floor.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static Job HandBack(Pawn pawn, ThingWithComps tool)
    {
        if (!StoreUtility.TryFindBestBetterStoreCellFor(tool, pawn, pawn.Map, StoragePriority.Unstored, pawn.Faction, out var cell))
        {
            return null;
        }

        AutoArmForced.Remove(pawn, tool);
        GameComponent_FetchedTools.Forget(tool);
        WeaponAssingment.DropSidearm(pawn, tool, intentionalDrop: true, unmemorise: true);
        if (!tool.Spawned || !pawn.CanReserve(tool))
        {
            // Left where it fell, unforbidden, for haulers
            return null;
        }

        return HaulAIUtility.HaulToCellStorageJob(pawn, tool, cell, fitInStoreCell: false);
    }
}

// The tools this feature fetched, by thing ID: tools the player gave are never handed back. Also the fetches under way and
// the fetched tools each pawn was last seen with, for telling how a tool came to be added.
public class GameComponent_FetchedTools : GameComponent
{
    private HashSet<int> _fetched = new();

    // Pawn ID → the tool they were sent for
    private Dictionary<int, int> _pending = new();

    // Pawn ID → the fetched tools they carried when last seen; not saved, refilled at each assignment
    private readonly Dictionary<int, List<Thing>> _carried = new();

    public GameComponent_FetchedTools(Game game)
    {
    }

    private static GameComponent_FetchedTools Instance => Current.Game?.GetComponent<GameComponent_FetchedTools>();

    public static bool IsFetched(Thing tool) => Instance?._fetched.Contains(tool.thingIDNumber) == true;

    public static void Remember(Thing tool) => Instance?._fetched.Add(tool.thingIDNumber);

    public static void Forget(Thing tool) => Instance?._fetched.Remove(tool.thingIDNumber);

    public static void SetPending(Pawn pawn, Thing tool)
    {
        if (Instance != null)
        {
            Instance._pending[pawn.thingIDNumber] = tool.thingIDNumber;
        }
    }

    // Whether this is the tool the pawn was sent for; the fetch is then done
    public static bool TakePending(Pawn pawn, Thing tool)
    {
        var instance = Instance;
        if (instance == null || !instance._pending.TryGetValue(pawn.thingIDNumber, out var id) || id != tool.thingIDNumber)
        {
            return false;
        }

        instance._pending.Remove(pawn.thingIDNumber);
        return true;
    }

    public static void SetCarried(Pawn pawn, List<ThingWithComps> fetched)
    {
        if (Instance != null)
        {
            Instance._carried[pawn.thingIDNumber] = fetched.Cast<Thing>().ToList();
        }
    }

    public static void NoteCarried(Pawn pawn, Thing tool)
    {
        if (Instance == null)
        {
            return;
        }

        if (!Instance._carried.TryGetValue(pawn.thingIDNumber, out var list))
        {
            Instance._carried[pawn.thingIDNumber] = list = new List<Thing>();
        }

        if (!list.Contains(tool))
        {
            list.Add(tool);
        }
    }

    // A fetched tool of the same kind that the pawn carried and no longer does: what Auto Arm's sidearm upgrade swapped out
    public static bool ReplacesFetched(Pawn pawn, Thing tool, out Thing replaced)
    {
        replaced = null;
        if (Instance == null || !Instance._carried.TryGetValue(pawn.thingIDNumber, out var list))
        {
            return false;
        }

        replaced = list.FirstOrDefault(old => old != tool && old.def == tool.def && IsFetched(old) && old.ParentHolder != pawn.inventory && old.ParentHolder != pawn.equipment);
        if (replaced == null)
        {
            return false;
        }

        list.Remove(replaced);
        return true;
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref _fetched, "fetchedTools", LookMode.Value);
        Scribe_Collections.Look(ref _pending, "pendingFetches", LookMode.Value, LookMode.Value);
        _fetched ??= new HashSet<int>();
        _pending ??= new Dictionary<int, int>();
    }
}

public class MapComponent_ToolAssignments : MapComponent
{
    // Tools in storage each pawn should fetch, and the fetched tools they carry and should put back
    private readonly Dictionary<Pawn, List<ThingWithComps>> _toFetch = new();
    private readonly Dictionary<Pawn, List<ThingWithComps>> _toHandBack = new();

    // When each work type was last in the pawn's main band
    private readonly Dictionary<Pawn, Dictionary<WorkTypeDef, int>> _lastMain = new();

    public MapComponent_ToolAssignments(Map map) : base(map)
    {
    }

    public IReadOnlyList<ThingWithComps> ToFetch(Pawn pawn) => _toFetch.TryGetValue(pawn, out var tools) ? tools : (IReadOnlyList<ThingWithComps>)new List<ThingWithComps>();

    public IReadOnlyList<ThingWithComps> ToHandBack(Pawn pawn) => _toHandBack.TryGetValue(pawn, out var tools) ? tools : (IReadOnlyList<ThingWithComps>)new List<ThingWithComps>();

    public bool HasWork(Pawn pawn) => (_toFetch.TryGetValue(pawn, out var fetch) && fetch.Count > 0) || (_toHandBack.TryGetValue(pawn, out var back) && back.Count > 0);

    // Each map refreshes on its own tick within the interval, so several maps don't all refresh on the same one
    public override void MapComponentTick()
    {
        if ((Find.TickManager.TicksGame + map.uniqueID * 613) % ToolsFetch.RefreshInterval == 0)
        {
            Refresh();
        }
    }

    public void Refresh()
    {
        _toFetch.Clear();
        _toHandBack.Clear();
        if (!TweaksSettings.ToolsFetch)
        {
            return;
        }

        Assign();
    }

    // Work with no skill (cleaning, hauling...) is ranked after skilled work for the pawn's tool slots
    private static bool HasSkill(WorkTypeDef workType) => workType.relevantSkills.Count > 0;

    // The main work types: with manual priorities, those at 1 to ToolsMainWorkPriority; without, the enabled ones where the
    // pawn has a passion in a relevant skill, and the enabled ones with no skill. A work type stays main for a day after it
    // leaves that band.
    private List<WorkTypeDef> MainWork(Pawn pawn, List<ThingWithComps> fetchedHeld, int now)
    {
        var main = new List<WorkTypeDef>();
        if (pawn.workSettings == null || !pawn.workSettings.EverWork)
        {
            return main;
        }

        if (!_lastMain.TryGetValue(pawn, out var last))
        {
            // First seen (a new pawn, or after loading): the work its fetched tools are for counts as recent
            _lastMain[pawn] = last = new Dictionary<WorkTypeDef, int>();
            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                var stats = ToolStats.ForWorkType(workType);
                if (fetchedHeld.Any(tool => ToolStats.Score(tool.def, stats) > 0f))
                {
                    last[workType] = now;
                }
            }
        }

        var maxPriority = TweaksSettings.ToolsMainWorkPriority;
        foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
        {
            if (pawn.WorkTypeIsDisabled(workType))
            {
                continue;
            }

            var priority = pawn.workSettings.GetPriority(workType);
            var inBand = priority > 0 && (Find.PlaySettings.useWorkPriorities
                ? priority <= maxPriority
                : !HasSkill(workType) || workType.relevantSkills.Any(skill => pawn.skills?.GetSkill(skill).passion > Passion.None));
            if (inBand)
            {
                last[workType] = now;
            }

            if (priority > 0 && last.TryGetValue(workType, out var tick) && now - tick < ToolsFetch.MainWorkGraceTicks)
            {
                main.Add(workType);
            }
        }

        return main;
    }

    private static float Skill(Pawn pawn, WorkTypeDef workType) =>
        pawn.skills == null || !HasSkill(workType) ? 0f : workType.relevantSkills.Average(skill => (float)pawn.skills.GetSkill(skill).Level);

    private void Assign()
    {
        var now = Find.TickManager.TicksGame;
        var cap = TweaksSettings.ToolsPerPawn;
        _lastMain.RemoveAll(entry => entry.Key.Destroyed);
        var pawns = map.mapPawns.FreeColonistsSpawned.Where(ToolsFetch.CanFetch).ToList();

        // The tools a colonist carries are theirs first; the rest of the supply is what's in storage
        var held = pawns.ToDictionary(pawn => pawn, ToolsFetch.CarriedTools);
        var supply = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
            .OfType<ThingWithComps>()
            .Where(tool => ToolsCategory.IsTool(tool.def) && tool.Spawned && !tool.IsForbidden(Faction.OfPlayer) && tool.IsInValidStorage())
            // One reserved is on its way to someone: a pawn walking to fetch it, who must still find it marked fetched
            .Where(tool => !map.reservationManager.IsReservedByAnyoneOf(tool, Faction.OfPlayer))
            .ToList();

        // A stored, unreserved tool is nobody's, whatever happened to it before: a pickup that failed, or one handed back and stored
        foreach (var tool in supply)
        {
            GameComponent_FetchedTools.Forget(tool);
        }

        var fetchedHeld = pawns.ToDictionary(pawn => pawn, pawn => held[pawn].Where(GameComponent_FetchedTools.IsFetched).ToList());
        foreach (var pawn in pawns)
        {
            GameComponent_FetchedTools.SetCarried(pawn, fetchedHeld[pawn]);
        }

        var main = pawns.ToDictionary(pawn => pawn, pawn => MainWork(pawn, fetchedHeld[pawn], now));
        var wanted = pawns.ToDictionary(pawn => pawn, _ => new List<ThingWithComps>());
        var taken = new HashSet<ThingWithComps>();

        // Skilled work first, then each work type's colonists in order of skill, so the best miner gets the best jackhammer and
        // a broom only takes a slot no skilled work wants
        var pairs = pawns
            .SelectMany(pawn => main[pawn].Select(workType => (pawn, workType)))
            .OrderBy(pair => HasSkill(pair.workType) ? 0 : 1)
            .ThenByDescending(pair => Skill(pair.pawn, pair.workType))
            .ThenBy(pair => pair.pawn.thingIDNumber)
            .ToList();
        foreach (var (pawn, workType) in pairs)
        {
            var stats = ToolStats.ForWorkType(workType);
            if (stats.Count == 0)
            {
                continue;
            }

            var mine = held[pawn].Concat(wanted[pawn]).ToList();
            var current = mine.OrderByDescending(tool => ToolStats.Score(tool.def, stats)).FirstOrDefault();
            var have = current == null ? 0f : ToolStats.Score(current.def, stats);

            // At the cap (tools the player gave don't count), only a replacement: a better tool for work whose current tool
            // was fetched and serves no other main work, so it goes back
            var fetchedCount = fetchedHeld[pawn].Count(tool => main[pawn].Any(other => ToolStats.Score(tool.def, ToolStats.ForWorkType(other)) > 0f))
                               + wanted[pawn].Count;
            if (fetchedCount >= cap)
            {
                var replaceable = have > 0f
                                  && fetchedHeld[pawn].Contains(current)
                                  && !main[pawn].Any(other => other != workType && IsBestFor(current, mine, ToolStats.ForWorkType(other)));
                if (!replaceable)
                {
                    continue;
                }
            }

            var best = supply
                .Where(tool => !taken.Contains(tool) && ToolStats.Score(tool.def, stats) > 0f)
                .OrderByDescending(tool => ToolStats.Score(tool.def, stats))
                .ThenBy(tool => tool.GetStatValue(StatDefOf.Mass))
                .ThenBy(tool => tool.Position.DistanceToSquared(pawn.Position))
                // The pawn may go there (allowed area, forbidden, reachable) and carry it
                .FirstOrDefault(tool => !tool.IsForbidden(pawn)
                                        && AutoArmForced.OutfitAllows(pawn, tool)
                                        && ToolsFetch.CanPickUp(pawn, tool)
                                        && pawn.CanReach(tool, PathEndMode.ClosestTouch, Danger.Some));
            if (best != null && (have <= 0f || ToolStats.Score(best.def, stats) > have * ToolsFetch.BetterBy))
            {
                wanted[pawn].Add(best);
                taken.Add(best);
            }
        }

        foreach (var pawn in pawns)
        {
            if (wanted[pawn].Count > 0)
            {
                _toFetch[pawn] = wanted[pawn];
            }

            // A fetched tool stays while it's the pawn's best for one of their main work types
            var mine = held[pawn].Concat(wanted[pawn]).ToList();
            var back = fetchedHeld[pawn]
                .Where(tool => !main[pawn].Any(workType => IsBestFor(tool, mine, ToolStats.ForWorkType(workType))))
                .ToList();
            if (back.Count > 0)
            {
                _toHandBack[pawn] = back;
            }
        }
    }

    private static bool IsBestFor(ThingWithComps tool, List<ThingWithComps> mine, HashSet<StatDef> stats)
    {
        var score = ToolStats.Score(tool.def, stats);
        return score > 0f && mine.All(other => ToolStats.Score(other.def, stats) <= score);
    }

    // Called by the job giver once a job is given or a tool is dealt with
    internal void Done(Pawn pawn, ThingWithComps tool)
    {
        if (_toFetch.TryGetValue(pawn, out var fetch))
        {
            fetch.Remove(tool);
        }

        if (_toHandBack.TryGetValue(pawn, out var back))
        {
            back.Remove(tool);
        }
    }
}

// Just before the colonist's normal work (MainColonistBehaviorCore's priority sorter), so pawns fetch when they're about to
// work, not instead of eating, sleeping or emergency work. Patched in only with Simple Sidearms (Patches/ToolsFetch.xml).
public class JobGiver_FetchTool : ThinkNode_JobGiver
{
    private static readonly JobGiver_Work Work = new();

    public override float GetPriority(Pawn pawn)
    {
        if (!TweaksSettings.ToolsFetch || pawn.Map?.GetComponent<MapComponent_ToolAssignments>()?.HasWork(pawn) != true)
        {
            return 0f;
        }

        try
        {
            return Work.GetPriority(pawn) + 0.01f;
        }
        catch (System.NotImplementedException)
        {
            // A modded time assignment JobGiver_Work doesn't know
            return 0f;
        }
    }

    public override Job TryGiveJob(Pawn pawn)
    {
        if (!TweaksSettings.ToolsFetch || !ToolsFetch.CanFetch(pawn))
        {
            return null;
        }

        var assignments = pawn.Map.GetComponent<MapComponent_ToolAssignments>();
        if (assignments == null)
        {
            return null;
        }

        var carried = ToolsFetch.CarriedTools(pawn);
        foreach (var tool in assignments.ToHandBack(pawn).ToList())
        {
            assignments.Done(pawn, tool);
            if (carried.Contains(tool))
            {
                var haul = ToolsFetch.HandBack(pawn, tool);
                if (haul != null)
                {
                    return haul;
                }
            }
        }

        foreach (var tool in assignments.ToFetch(pawn).ToList())
        {
            assignments.Done(pawn, tool);
            if (tool.Spawned && !tool.IsForbidden(pawn) && pawn.CanReserveAndReach(tool, PathEndMode.ClosestTouch, Danger.Some) && ToolsFetch.CanPickUp(pawn, tool))
            {
                GameComponent_FetchedTools.Remember(tool);
                GameComponent_FetchedTools.SetPending(pawn, tool);
                return ToolsFetch.FetchJob(tool);
            }
        }

        return null;
    }
}
