using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MultiFloors;
using MultiFloors.HarmonyPatches;
using MultiFloors.Jobs;
using MultiFloors.Maps;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

/// <summary>
/// Fixes for bugs in MultiFloors 1.6.1.3, found by reading its decompiled code (it has no public source). Each is described
/// where it's patched. Not fixed: MultiFloors' own tick for upper levels leaves out the wind manager, so their wind is always
/// 0 and wind turbines there make no power; judged not worth patching (see the README). All are behind <see cref="TweaksSettings.MultiFloorsFixes"/>, read at run time, and every patch
/// falls back to MultiFloors' own behaviour with it off. Everything here touches MultiFloors' types, so it's only patched
/// when MultiFloors is active.
/// </summary>
public static class MultiFloorsFixes
{
    private static readonly MethodInfo TryTakeOrderedJob = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob));
    private static readonly MethodInfo HashSetCellContains = AccessTools.Method(typeof(HashSet<IntVec3>), nameof(HashSet<IntVec3>.Contains));

    // The prioritized scanner's flag for which list it's scanning, which is private
    private static readonly AccessTools.FieldRef<bool> ScanningHighPriorityWorkGivers =
        AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(HarmonyPatch_ScanJobsOnOtherLevelPrioritized), "ScanningHighPriorityWorkGivers"));

    // CrossLevelHaulingMarker.CellReservedByMap: map id to a set of its private CellWithCount
    private static readonly FieldInfo CellReservedByMap = AccessTools.Field(typeof(CrossLevelHaulingMarker), "CellReservedByMap");

    // Depth of JobGiver_Work.TryIssueJobPackage, which MultiFloors' scanners call again from inside it
    private static int _workScanDepth;

    // Whether the job SwitchMap last ended was a player's order, for the pawn it moved
    private static Pawn _switchedPawn;
    private static bool _switchedJobForced;

    public static void Patch(
        Harmony harmony
    )
    {
        // 1. The job a pawn takes on after changing level is started as a player order
        var followUps = typeof(Toils_Stairs).GetNestedTypes(AccessTools.all)
            .SelectMany(AccessTools.GetDeclaredMethods)
            .Where(method => !method.IsAbstract && PatchProcessor.ReadMethodBody(method).Any(instruction => instruction.Value as MethodInfo == TryTakeOrderedJob))
            .ToList();
        if (followUps.Count == 0)
        {
            Log.Warning("[Tweaks] Found no TryTakeOrderedJob call in MultiFloors' Toils_Stairs, so a pawn changing level still loses its queued jobs.");
        }

        foreach (var method in followUps)
        {
            harmony.TryPatch(method, $"MultiFloors' Toils_Stairs.{method.Name}", "a pawn changing level loses its queued jobs",
                transpiler: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Transpiler_FollowUpJob)));
        }

        harmony.TryPatch(AccessTools.Method(typeof(CrossLevelMoveJobUtility), nameof(CrossLevelMoveJobUtility.SwitchMap)),
            "MultiFloors' CrossLevelMoveJobUtility.SwitchMap", "a pawn changing level on a player's order may lose it on the way",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_SwitchMap)));

        // 2. One exception during the cross-level work scan stops it for every pawn
        harmony.TryPatch(AccessTools.Method(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage)), "JobGiver_Work.TryIssueJobPackage",
            "one exception during MultiFloors' work scan stops it for the rest of the game",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_TryIssueJobPackage)) { priority = Priority.First },
            finalizer: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Finalizer_TryIssueJobPackage)));

        // 3. A "haul to other level" designation is lost when the haul is interrupted
        harmony.TryPatch(AccessTools.Method(typeof(JobDriver_HaulToOtherLevel), "MakeNewToils"), "MultiFloors' JobDriver_HaulToOtherLevel.MakeNewToils",
            "an interrupted haul to another level forgets the designation",
            postfix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Postfix_HaulToOtherLevel_MakeNewToils)));

        // 4. What a pawn carries is dropped on another level during the cross-level work scan
        harmony.TryPatch(AccessTools.Method(typeof(CrossLevelHaulJobUtility), nameof(CrossLevelHaulJobUtility.TryDropCarriedThing)),
            "MultiFloors' CrossLevelHaulJobUtility.TryDropCarriedThing", "a carried thing can be dropped on another level",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_TryDropCarriedThing)));

        // 5. A stair whose exit can't be entered ends the job as succeeded
        harmony.TryPatch(AccessTools.Method(typeof(CrossLevelMoveJobUtility), nameof(CrossLevelMoveJobUtility.ValidateEnterCell)),
            "MultiFloors' CrossLevelMoveJobUtility.ValidateEnterCell", "a blocked stair exit leaves the pawn holding its load",
            postfix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Postfix_ValidateEnterCell)));

        // 6 and 7. The hauling marker's storage cell bookkeeping
        harmony.TryPatch(AccessTools.Method(typeof(CrossLevelHaulingMarker), nameof(CrossLevelHaulingMarker.TryFindHaulDestinationFor)),
            "MultiFloors' CrossLevelHaulingMarker.TryFindHaulDestinationFor", "a full storage cell on one level hides the same cell on every level",
            transpiler: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Transpiler_TryFindHaulDestinationFor)));
        var reserveCell = AccessTools.GetDeclaredMethods(typeof(CrossLevelHaulingMarker)).FirstOrDefault(method => method.Name.Contains("ReserveCell"));
        harmony.TryPatch(reserveCell, "MultiFloors' CrossLevelHaulingMarker ReserveCell", "the first storage cell planned on each level isn't held for its load",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_ReserveCell)));

        // 9. Temporary floor locks become permanent after a save
        harmony.TryPatch(AccessTools.Method(typeof(MultiFloorManager), nameof(MultiFloorManager.ExposeData)), "MultiFloors' MultiFloorManager.ExposeData",
            "a pawn locked to a floor for a while stays locked after a save and load",
            postfix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Postfix_ManagerExposeData)));

        // 10. A manual "Lock floor" is undone by an earlier temporary lock's timer
        var stayOnCurrentMap = AccessTools.Method(typeof(PrepatcherFields), nameof(PrepatcherFields.StayOnCurrentMap));
        var toggles = new[] { typeof(GizmoFactory), typeof(MultiFloors.PawnLevelSettings.PawnColumnWorker_StayOnCurrentMap) }
            .SelectMany(type => type.GetNestedTypes(AccessTools.all))
            .SelectMany(AccessTools.GetDeclaredMethods)
            .Where(method => !method.IsAbstract && WritesLockFlag(method, stayOnCurrentMap))
            .ToList();
        if (toggles.Count == 0)
        {
            Log.Warning("[Tweaks] Found no Lock floor toggle in MultiFloors, so a manual lock can still be undone by an earlier temporary one.");
        }

        foreach (var method in toggles)
        {
            harmony.TryPatch(method, $"MultiFloors' Lock floor toggle {method.DeclaringType?.Name}.{method.Name}", "a manual Lock floor can be undone by an earlier temporary lock",
                postfix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Postfix_LockToggled)));
        }

        // 11. A pawn waits forever at an unpowered elevator
        harmony.TryPatch(AccessTools.Method(typeof(Toils_Elevator), nameof(Toils_Elevator.WaitForElevator)), "MultiFloors' Toils_Elevator.WaitForElevator",
            "a pawn waits forever at an unpowered elevator",
            postfix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Postfix_WaitForElevator)));

        // 13. A stair's transfer limit is exceeded by stacks of different quality or stuff, and by other stairs
        harmony.TryPatch(AccessTools.Method(typeof(AutoTransferWorker), "TryAddToTheToLoadList"), "MultiFloors' AutoTransferWorker.TryAddToTheToLoadList",
            "a stair moves more than its transfer limit",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_TryAddToTheToLoadList)));

        // 15. Forbidden ingredients on an upper level hold up a bill linked to it
        harmony.TryPatch(AccessTools.Method(typeof(GlobalBillGiverMonitor), nameof(GlobalBillGiverMonitor.DesignateMissingIngredientsForListeningBillGivers)),
            "MultiFloors' GlobalBillGiverMonitor.DesignateMissingIngredientsForListeningBillGivers", "forbidden ingredients upstairs hold up a linked bill",
            transpiler: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Transpiler_DesignateMissingIngredients)));

        // Performance: the level work filter makes a new list on every work scan
        harmony.TryPatch(AccessTools.Method(typeof(MultiFloors.LevelWorkSettings.LevelWorkSetting), nameof(MultiFloors.LevelWorkSettings.LevelWorkSetting.FilterWorkGivers)),
            "MultiFloors' LevelWorkSetting.FilterWorkGivers", "level work settings allocate a list on every work scan",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_FilterWorkGivers)));

        // 8. Making a designated haul job with no designation throws
        harmony.TryPatch(AccessTools.Method(typeof(WorkGiver_HaulDesignatedToDestMap), nameof(WorkGiver_HaulDesignatedToDestMap.JobOnThing)),
            "MultiFloors' WorkGiver_HaulDesignatedToDestMap.JobOnThing", "a designated haul without its designation throws",
            prefix: new HarmonyMethod(typeof(MultiFloorsFixes), nameof(Prefix_HaulDesignatedJobOnThing)));
    }

    // 1. On arrival, each of Toils_Stairs' toils hands the rest of the work to a new job with TryTakeOrderedJob. That marks
    // the job as a player's order and clears the pawn's queued jobs (SwitchMap ended the old job without touching the
    // queue), so a pawn going upstairs to deliver or haul forgets what the player queued for it. Unless the job that took it
    // there was itself a player's order, the new one is started as an ordinary job instead, with the queue kept.
    public static IEnumerable<CodeInstruction> Transpiler_FollowUpJob(
        IEnumerable<CodeInstruction> instructions
    )
    {
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(TryTakeOrderedJob))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MultiFloorsFixes), nameof(TakeFollowUpJob)))
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
                continue;
            }

            yield return instruction;
        }
    }

    public static bool TakeFollowUpJob(
        Pawn_JobTracker jobs,
        Job job,
        JobTag? tag,
        bool requestQueueing
    )
    {
        var pawn = jobs.pawn;
        var ordered = _switchedPawn == pawn && _switchedJobForced;
        if (!TweaksSettings.MultiFloorsFixes || ordered || pawn.Drafted || jobs.curJob != null)
        {
            return jobs.TryTakeOrderedJob(job, tag, requestQueueing);
        }

        jobs.StartJob(job, JobCondition.None, tag: tag, preToilReservationsCanFail: true);
        return jobs.curJob == job;
    }

    public static void Prefix_SwitchMap(
        Pawn pawn
    )
    {
        _switchedPawn = pawn;
        _switchedJobForced = pawn.CurJob?.playerForced ?? false;
    }

    // 2. Both of MultiFloors' scanners set a static flag around their scan of other levels, and clear it after, with no
    // finally. An exception between the two (in its stair lookups, or another mod's patch) leaves the flag set, and every
    // pawn's work scan then skips other levels, and with the prioritized scanner, its high priority work, until the game is
    // reloaded. When the outermost call ends, the flags are put back, and a pawn still moved for the scan is moved back.
    public static void Prefix_TryIssueJobPackage() => _workScanDepth++;

    public static void Finalizer_TryIssueJobPackage(
        Pawn pawn
    )
    {
        if (--_workScanDepth > 0)
        {
            return;
        }

        _workScanDepth = 0;
        if (!TweaksSettings.MultiFloorsFixes)
        {
            return;
        }

        HarmonyPatch_ScanJobsOnOtherLevel.ScanningOtherLevel = false;
        HarmonyPatch_ScanJobsOnOtherLevelPrioritized.ScanningOtherLevels = false;
        ScanningHighPriorityWorkGivers() = true;
        if (pawn?.OldMap() != null)
        {
            LevelUtility.UnsafeTeleportBack(pawn);
        }
    }

    // 3. The haul removes a thing's designation as the pawn picks it up (all of it to carry, or part of the count to its
    // inventory). If the job then fails before the stairs, MultiFloors drops what the pawn holds where it stands, with no
    // designation left, so nobody takes it to the other level. Those things are designated again for the job's level.
    public static IEnumerable<Toil> Postfix_HaulToOtherLevel_MakeNewToils(
        IEnumerable<Toil> __result,
        JobDriver_HaulToOtherLevel __instance
    ) => WithHeldThingsRedesignated(__result, __instance);

    private static IEnumerable<Toil> WithHeldThingsRedesignated(
        IEnumerable<Toil> toils,
        JobDriver driver
    )
    {
        List<Thing> held = null;
        // Added before MultiFloors' own finish action, which drops them
        driver.AddFinishAction(condition =>
        {
            if (condition != JobCondition.Succeeded && TweaksSettings.MultiFloorsFixes)
            {
                held = HeldThings(driver.pawn);
            }
        });
        foreach (var toil in toils)
        {
            yield return toil;
        }

        driver.AddFinishAction(_ =>
        {
            if (held != null)
            {
                Redesignate(held, driver.job.globalTarget.Map);
            }
        });
    }

    private static List<Thing> HeldThings(
        Pawn pawn
    )
    {
        var held = new List<Thing>();
        if (pawn.carryTracker?.CarriedThing is { } carried)
        {
            held.Add(carried);
        }

        if (pawn.ThingsPickupInInventory() is { } pickedUp)
        {
            held.AddRange(pickedUp.Where(thing => thing != null && pawn.inventory.innerContainer.Contains(thing)));
        }

        return held;
    }

    // Public for tests
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Redesignate(
        List<Thing> things,
        Map destMap
    )
    {
        if (destMap == null || destMap.Disposed)
        {
            return;
        }

        foreach (var thing in things)
        {
            // A thing merged into a stack where it was dropped is gone
            if (thing.Spawned && !thing.Destroyed && thing.Map != destMap && thing.Map.GetHaulDesignationOn(thing) == null)
            {
                DesignationUtility.AddHaulToOtherLevelDesignation(thing.Map, thing, IntVec3.Invalid, destMap, thing.stackCount);
            }
        }
    }

    // 4. The cross-level work scan moves the pawn onto the other level in place (UnsafeTeleportTo) and runs the work givers
    // there. MultiFloors' delivery, repair and designated haul givers drop what the pawn carries when they find a job, which
    // then lands on that level, by the stair. It's dropped where the pawn really is instead.
    public static bool Prefix_TryDropCarriedThing(
        Pawn pawn
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || pawn.OldMap() is not { } realMap || pawn.carryTracker?.CarriedThing is not { } carried)
        {
            return true;
        }

        pawn.carryTracker.innerContainer.TryDrop(carried, pawn.OldPosition(), realMap, ThingPlaceMode.Near, out _);
        return false;
    }

    // 5. ValidateEnterCell gives an invalid cell when the stair's exit is blocked, and each arrival toil then does nothing.
    // The toil is instant, so the job runs out of toils and ends as succeeded: the clean-up MultiFloors' drivers only do on
    // failure (dropping the load, releasing a frame's claim) never runs, and the pawn stands at the stair holding its load.
    // It ends as incompletable instead. It's only called from those toils, on the pawn's current job.
    public static void Postfix_ValidateEnterCell(
        IntVec3 __result,
        Pawn pawn
    )
    {
        if (!__result.IsValid && TweaksSettings.MultiFloorsFixes && pawn?.jobs?.curDriver is { ended: false } driver)
        {
            driver.EndJobWith(JobCondition.Incompletable);
        }
    }

    // 6. The marker skips cells in its BlacklistedCells, cells it found already full of planned loads. The set holds cells
    // without their map, and every level shares the same coordinates, so a full cell on one level hides the same cell on
    // every other. IsGoodStoreCell checks the planned loads itself, so the set only saves time: it's no longer consulted.
    public static IEnumerable<CodeInstruction> Transpiler_TryFindHaulDestinationFor(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var replaced = false;
        foreach (var instruction in instructions)
        {
            if (!replaced && instruction.Calls(HashSetCellContains))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MultiFloorsFixes), nameof(CellBlacklisted)))
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
                replaced = true;
                continue;
            }

            yield return instruction;
        }

        if (!replaced)
        {
            Log.Warning("[Tweaks] Found no BlacklistedCells check in MultiFloors' TryFindHaulDestinationFor, so a full storage cell on one level still hides it on the others.");
        }
    }

    public static bool CellBlacklisted(
        HashSet<IntVec3> blacklist,
        IntVec3 cell
    ) => !TweaksSettings.MultiFloorsFixes && blacklist.Contains(cell);

    // 7. ReserveCell, which counts the loads planned for a storage cell, makes the set for a level the first time it's
    // asked, but doesn't add the cell to it, so the first planned load on each level isn't counted and another can be
    // planned for the same cell. The level's set is made first, so the cell is added.
    public static void Prefix_ReserveCell(
        Map map
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || map == null || CellReservedByMap?.GetValue(null) is not IDictionary reserved || reserved.Contains(map.uniqueID))
        {
            return;
        }

        reserved.Add(map.uniqueID, Activator.CreateInstance(CellReservedByMap.FieldType.GetGenericArguments()[1]));
    }

    // 8. JobOnThing reads the thing's designation without checking it's there. The work scanner calls HasJobOnThing first,
    // which checks it, but a float menu or another mod's call may not.
    public static bool Prefix_HaulDesignatedJobOnThing(
        Pawn pawn,
        Thing t,
        ref Job __result
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || pawn.Map?.GetHaulDesignationOn(t) != null)
        {
            return true;
        }

        __result = null;
        return false;
    }

    // MultiFloorManager's temporary floor locks: the pawn and the tick it was locked
    private static readonly AccessTools.FieldRef<MultiFloorManager, List<(Pawn, int)>> LockedPawns =
        AccessTools.FieldRefAccess<MultiFloorManager, List<(Pawn, int)>>("lockedPawns");

    // 9. "Prioritize work on this level" (a stair's or elevator's float menu) turns on the pawn's Lock floor, which is saved
    // with the pawn, and notes the pawn and tick in MultiFloorManager.lockedPawns, which isn't saved, for its tick to turn
    // the lock off after TickToLockIfPrioritized (6000 ticks by default). Saved in between, the pawn loads locked for good.
    // The list is saved alongside the manager's own data, and put back on load.
    public static void Postfix_ManagerExposeData(
        MultiFloorManager __instance
    ) => ExposeTemporaryLocks(LockedPawns(__instance));

    private static List<Pawn> _tmpLockedPawns;
    private static List<int> _tmpLockedTicks;

    // Public for tests, which save and load a list of their own
    public static void ExposeTemporaryLocks(
        List<(Pawn pawn, int tick)> locks
    )
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            var kept = locks.Where(entry => entry.pawn is { Destroyed: false, Discarded: false }).ToList();
            _tmpLockedPawns = kept.Select(entry => entry.pawn).ToList();
            _tmpLockedTicks = kept.Select(entry => entry.tick).ToList();
        }

        Scribe_Collections.Look(ref _tmpLockedPawns, "ordo_temporaryLockPawns", LookMode.Reference);
        Scribe_Collections.Look(ref _tmpLockedTicks, "ordo_temporaryLockTicks", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (TweaksSettings.MultiFloorsFixes && _tmpLockedPawns != null && _tmpLockedTicks != null && _tmpLockedPawns.Count == _tmpLockedTicks.Count)
            {
                locks.Clear();
                for (var i = 0; i < _tmpLockedPawns.Count; i++)
                {
                    if (_tmpLockedPawns[i] != null)
                    {
                        locks.Add((_tmpLockedPawns[i], _tmpLockedTicks[i]));
                    }
                }
            }
        }

        if (Scribe.mode is LoadSaveMode.Saving or LoadSaveMode.PostLoadInit)
        {
            _tmpLockedPawns = null;
            _tmpLockedTicks = null;
        }
    }

    // 10. The manager turns Lock floor off when a temporary lock's time is up, whatever happened since, so a pawn the
    // player locked by hand (the gizmo or the Floors tab) during a temporary lock is unlocked later. Any toggle by hand
    // drops the pawn's temporary locks: the player has now decided.
    private static bool WritesLockFlag(
        MethodBase method,
        MethodInfo stayOnCurrentMap
    )
    {
        var instructions = PatchProcessor.ReadMethodBody(method).ToList();
        return instructions.Any(instruction => instruction.Key == OpCodes.Call && instruction.Value as MethodInfo == stayOnCurrentMap)
               && instructions.Any(instruction => instruction.Key == OpCodes.Stind_I1);
    }

    public static void Postfix_LockToggled(
        object __instance
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || __instance == null)
        {
            return;
        }

        var pawnField = AccessTools.GetDeclaredFields(__instance.GetType()).FirstOrDefault(field => field.FieldType == typeof(Pawn));
        if (pawnField?.GetValue(__instance) is Pawn pawn)
        {
            ForgetTemporaryLocks(pawn);
        }
    }

    // Public for tests
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool HasTemporaryLock(
        Pawn pawn
    ) => Current.Game.GetComponent<MultiFloorManager>() is { } manager && LockedPawns(manager).Any(entry => entry.Item1 == pawn);

    private static void ForgetTemporaryLocks(
        Pawn pawn
    )
    {
        if (Current.Game?.GetComponent<MultiFloorManager>() is { } manager)
        {
            LockedPawns(manager).RemoveAll(entry => entry.Item1 == pawn);
        }
    }

    // 11. The pawn waiting for an elevator only counts down its timeout (600 ticks) while the elevator has power, and the
    // wait never ends by itself, so at an elevator whose power went out it waits until something interrupts it. The wait
    // now also ends after that long without power.
    public static void Postfix_WaitForElevator(
        Toil __result,
        Pawn pawn,
        Func<Stair> nextStairGetter,
        int maxTimeout
    )
    {
        var original = __result.tickAction;
        var unpowered = 0;
        __result.tickAction = () =>
        {
            original?.Invoke();
            if (!TweaksSettings.MultiFloorsFixes || nextStairGetter() is not Elevator { PowerRequired: true } elevator || elevator.CompPowerTrader.PowerOn)
            {
                return;
            }

            if (++unpowered >= maxTimeout)
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            }
        };
    }

    // 13. A stair's transfer policy caps how many of a def it moves ("keep 10 upstairs"). TryAddToTheToLoadList works out
    // the allowance for the def, then checks it against the one transferable the thing matches, and transferables are
    // split by stuff and quality, so each of a def's qualities could be queued up to the whole allowance. Each stair to
    // the same level also had the whole allowance. It's replaced by the same steps checked against everything of the def
    // queued on every stair from this level to the same level.
    private static readonly AccessTools.FieldRef<AutoTransferWorker, Stair> TransferStair = AccessTools.FieldRefAccess<AutoTransferWorker, Stair>("stair");
    private static readonly AccessTools.FieldRef<AutoTransferWorker, HashSet<ThingDef>> TransferBlacklist =
        AccessTools.FieldRefAccess<AutoTransferWorker, HashSet<ThingDef>>("blacklistedThingDefs");
    private static readonly Func<AutoTransferWorker, ThingDef, int> MaxAllowedCount =
        AccessTools.MethodDelegate<Func<AutoTransferWorker, ThingDef, int>>(AccessTools.Method(typeof(AutoTransferWorker), "GetMaxAllowedCount"));

    public static bool Prefix_TryAddToTheToLoadList(
        AutoTransferWorker __instance,
        Thing thing,
        int count,
        HashSet<int> addedThingsOnCurrentLevel
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || thing == null || count <= 0)
        {
            return true;
        }

        var stair = TransferStair(__instance);
        var remaining = MaxAllowedCount(__instance, thing.def) - QueuedForLevel(stair, thing.def);
        count = Math.Min(remaining, count);
        if (count <= 0)
        {
            TransferBlacklist(__instance).Add(thing.def);
            return false;
        }

        stair.leftToLoad ??= new List<TransferableOneWay>();
        var matching = TransferableUtility.TransferableMatching(thing, stair.leftToLoad, TransferAsOneMode.PodsOrCaravanPacking);
        if (matching == null)
        {
            matching = new TransferableOneWay();
            stair.leftToLoad.Add(matching);
        }

        if (!matching.things.Contains(thing))
        {
            matching.things.Add(thing);
            addedThingsOnCurrentLevel.Add(thing.thingIDNumber);
        }

        if (matching.CanAdjustBy(count).Accepted)
        {
            matching.AdjustBy(count);
        }

        return false;
    }

    // Public for tests: everything of a def queued on the stairs from this stair's level to its connected level
    public static int QueuedForLevel(
        Stair stair,
        ThingDef def
    )
    {
        var queued = 0;
        foreach (var other in stair.Map.listerThings.GetThingsOfType<Stair>())
        {
            if (other.ConnectedMap != stair.ConnectedMap || other.leftToLoad == null)
            {
                continue;
            }

            foreach (var transferable in other.leftToLoad)
            {
                if (transferable.ThingDef == def)
                {
                    queued += transferable.CountToTransfer;
                }
            }
        }

        return queued;
    }

    // 15. The monitor designates ingredients on the level a bill giver is linked to, for a bill short of them, and counts
    // what it designates as on its way. It skips forbidden ones only on the ground (map2.Level() != 0 || !IsForbidden),
    // so forbidden ingredients upstairs were designated and counted, but never hauled, since haulers skip forbidden
    // things: the bill waited on them for good and didn't designate allowed ones. The level it reads there is taken as
    // the ground's, so forbidden ingredients are skipped on every level.
    public static IEnumerable<CodeInstruction> Transpiler_DesignateMissingIngredients(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var level = AccessTools.Method(typeof(PrepatcherFields), nameof(PrepatcherFields.Level));
        var replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(level))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MultiFloorsFixes), nameof(LevelForForbiddenCheck)))
                    .MoveLabelsFrom(instruction)
                    .MoveBlocksFrom(instruction);
                replaced++;
                continue;
            }

            yield return instruction;
        }

        if (replaced != 1)
        {
            Log.Warning($"[Tweaks] Expected one level check in MultiFloors' DesignateMissingIngredientsForListeningBillGivers, found {replaced}, so forbidden ingredients upstairs may still hold up a bill.");
        }
    }

    private static int _groundLevel;

    public static ref int LevelForForbiddenCheck(
        Map map
    )
    {
        if (!TweaksSettings.MultiFloorsFixes)
        {
            return ref map.Level();
        }

        _groundLevel = 0;
        return ref _groundLevel;
    }

    // Performance: with a level's work settings switching some work types off, FilterWorkGivers makes a new list on every
    // JobGiver_Work.TryIssueJobPackage, for every pawn, many times a second in a big colony. It fills a list kept for the
    // depth of the call instead (MultiFloors' scanner calls TryIssueJobPackage again from inside, and a list is only read
    // until its call returns).
    private static readonly List<List<WorkGiver>> FilteredByDepth = new();

    public static bool Prefix_FilterWorkGivers(
        MultiFloors.LevelWorkSettings.LevelWorkSetting __instance,
        List<WorkGiver> workGivers,
        ref List<WorkGiver> __result
    )
    {
        if (!TweaksSettings.MultiFloorsFixes || __instance.AllEnabled)
        {
            return true;
        }

        var depth = Math.Max(_workScanDepth, 0);
        while (FilteredByDepth.Count <= depth)
        {
            FilteredByDepth.Add(new List<WorkGiver>());
        }

        var filtered = FilteredByDepth[depth];
        filtered.Clear();
        if (!__instance.AllDisabled)
        {
            foreach (var giver in workGivers)
            {
                if (__instance.Allows(giver.def))
                {
                    filtered.Add(giver);
                }
            }
        }

        __result = filtered;
        return false;
    }

    // For tests: links a bill giver to a level for its ingredients, as its bill tab does, or unlinks it
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void LinkBillGiverForTests(
        Thing billGiver,
        int level
    )
    {
        var monitor = Current.Game.GetComponent<MultiFloorManager>().GlobalBillGiverMonitor;
        billGiver.IsListening() = level != 0;
        billGiver.BillGiverLinkedLevel() = level;
        if (level != 0)
        {
            monitor.AddBillGiver(billGiver);
        }
        else
        {
            monitor.RemoveBillGiver(billGiver);
        }
    }

    // For tests: runs the monitor's designation pass, as its tick does
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DesignateMissingIngredientsForTests() =>
        Current.Game.GetComponent<MultiFloorManager>().GlobalBillGiverMonitor.DesignateMissingIngredientsForListeningBillGivers();

    // For tests: saves and loads temporary locks as MultiFloorManager's data does. Here rather than in the tests, since
    // the save system only finds classes in mod assemblies.
    public sealed class TemporaryLocksForTests : IExposable
    {
        public List<(Pawn pawn, int tick)> Locks = new();

        public void ExposeData() => ExposeTemporaryLocks(Locks);
    }

    // For tests: designates a thing to be hauled to a level, as MultiFloors' designator does
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DesignateHaulTo(
        Thing thing,
        Map destMap
    ) => DesignationUtility.AddHaulToOtherLevelDesignation(thing.Map, thing, IntVec3.Invalid, destMap, thing.stackCount);

    // For tests: the level a thing is designated to be hauled to, if any
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Map HaulDestination(
        Thing thing
    ) => thing.Spawned ? thing.Map.GetHaulDesignationOn(thing)?.DesignationDestMap() : null;
}
