using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools;

// Why a colony's pawns do or don't take a work giver's jobs, across every map (MultiFloors' levels included)
public sealed class WorkTools
{
    [Tool("ordo/work_giver_check", Description = "For every player pawn and colony mech on every map, report whether a work giver is in its work lists (vanilla, MultiFloors' high and low lists, Work Tab's per-task priority), whether it would skip it, and the job its NonScanJob gives now. Also lists unfinished frames on every map. May drop what a pawn carries, as the work giver would.")]
    public static object WorkGiverCheck(
        [ToolParameter(Description = "The WorkGiverDef to check")] string workGiver = "MF_DeliverResourcesAcrossLevel"
    )
    {
        var def = DefDatabase<WorkGiverDef>.GetNamedSilentFail(workGiver);
        if (def == null)
            return new { success = false, message = $"No work giver named {workGiver}" };

        var worker = def.Worker;
        var careAboutOtherLevels = AccessTools.Method("MultiFloors.LevelUtility:CareAboutOtherLevels");
        var stayOnCurrentMap = AccessTools.Method("MultiFloors.PrepatcherFields:StayOnCurrentMap");
        var highList = AccessTools.Method("MultiFloors.PrepatcherFields:HighPriorityWorkGiversInOrderNormal");
        var lowList = AccessTools.Method("MultiFloors.PrepatcherFields:LowPriorityWorkGiversInOrderNormal");
        var dirty = AccessTools.Method("MultiFloors.PrepatcherFields:WorkGiversOrderDirty");
        var workTabPriority = AccessTools.Method("WorkTab.Pawn_Extensions:GetPriority", [typeof(Pawn), typeof(WorkGiverDef), typeof(int)]);

        object Call(System.Reflection.MethodInfo method, params object[] args)
        {
            if (method == null)
                return "n/a";
            try
            {
                return method.Invoke(null, args);
            }
            catch (Exception e)
            {
                return $"{e.InnerException?.GetType().Name ?? e.GetType().Name}: {e.InnerException?.Message ?? e.Message}";
            }
        }

        object InList(object list) => list is List<WorkGiver> givers ? givers.Contains(worker) : list ?? "null";

        var pawns = Find.Maps
            .SelectMany(map => map.mapPawns.AllPawnsSpawned)
            .Where(pawn => pawn.Faction == Faction.OfPlayer && pawn.workSettings != null && (pawn.RaceProps.Humanlike || pawn.RaceProps.IsMechanoid))
            .Select(pawn =>
            {
                var settings = pawn.workSettings;
                string job;
                try
                {
                    var found = worker.ShouldSkip(pawn) ? null : worker.NonScanJob(pawn);
                    job = found == null ? "none" : $"{found.def.defName} A={found.targetA} B={found.targetB} count={found.count}";
                }
                catch (Exception e)
                {
                    job = $"{e.GetType().Name}: {e.Message}";
                }

                return new
                {
                    pawn = pawn.LabelShort,
                    map = pawn.Map.ToString(),
                    job = pawn.CurJobDef?.defName,
                    everWork = settings.EverWork,
                    workTypePriority = def.workType == null ? -1 : settings.GetPriority(def.workType),
                    workTypeDisabled = def.workType != null && pawn.WorkTypeIsDisabled(def.workType),
                    missingCapacity = worker.MissingRequiredCapacity(pawn)?.defName,
                    workTabPriority = Call(workTabPriority, pawn, def, GenLocalDate.HourOfDay(pawn)),
                    inVanillaList = settings.WorkGiversInOrderNormal.Contains(worker),
                    inHighList = InList(Call(highList, settings)),
                    inLowList = InList(Call(lowList, settings)),
                    listsDirty = Call(dirty, settings),
                    careAboutOtherLevels = Call(careAboutOtherLevels, pawn),
                    lockFloor = Call(stayOnCurrentMap, pawn),
                    shouldSkip = worker.ShouldSkip(pawn),
                    nonScanJob = job,
                    frameChecks = FrameChecks(pawn)
                };
            })
            .ToList();

        var frames = Find.Maps
            .SelectMany(map => map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).OfType<Frame>())
            .Where(frame => frame.Faction == Faction.OfPlayer)
            .Select(frame => new
            {
                frame = frame.LabelShort,
                map = frame.Map.ToString(),
                position = frame.Position.ToString(),
                forbidden = frame.IsForbidden(Faction.OfPlayer),
                needs = frame.TotalMaterialCost()
                    .Select(cost => $"{cost.thingDef.defName} {frame.ThingCountNeeded(cost.thingDef)}")
                    .ToList()
            })
            .ToList();

        return new { success = true, workGiver = def.defName, workType = def.workType?.defName, pawns, frames };
    }

    // Each check MultiFloors' cross-level delivery makes, for every frame on another map than the pawn's
    private static List<string> FrameChecks(
        Pawn pawn
    )
    {
        var handling = AccessTools.Method("MultiFloors.PrepatcherFields:HandlingByOthers");
        var forbiddenAcross = AccessTools.Method("MultiFloors.Jobs.CrossLevelMoveJobUtility:IsForbiddenAcrossLevel");
        var reachAcross = AccessTools.Method("MultiFloors.StairPathFinderUtility:CanReachAcrossLevel");
        var lines = new List<string>();
        foreach (var frame in Find.Maps.SelectMany(map => map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)).OfType<Frame>())
        {
            if (frame.Map == pawn.Map || frame.Faction != pawn.Faction)
                continue;

            string Try(Func<object> get)
            {
                try
                {
                    return get()?.ToString() ?? "null";
                }
                catch (Exception e)
                {
                    return $"{e.InnerException?.GetType().Name ?? e.GetType().Name}: {e.InnerException?.Message ?? e.Message}";
                }
            }

            Area area = null;
            pawn.playerSettings?.allowedAreas?.TryGetValue(frame.Map, out area);
            var line = $"{frame.LabelShort}@{frame.Map}{frame.Position}: handlingByOthers={Try(() => handling?.Invoke(null, [frame]))}"
                + $" forbiddenAcross={Try(() => forbiddenAcross?.Invoke(null, [frame, pawn]))}"
                + $" area={area?.Label ?? "none"}({area?.TrueCount ?? 0} cells, hasFrame={area == null || area.TrueCount == 0 || area[frame.Position]})"
                + $" reachAcross={Try(() => reachAcross?.Invoke(null, [pawn, frame, null, false]))}";
            foreach (var cost in frame.TotalMaterialCost())
            {
                var needed = frame.ThingCountNeeded(cost.thingDef);
                if (needed <= 0)
                    continue;

                var anywhere = Try(() => pawn.Map.itemAvailability.ThingsAvailableAnywhere(cost.thingDef, needed, pawn));
                var closest = Try(() => GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.Map, ThingRequest.ForDef(cost.thingDef), Verse.AI.PathEndMode.ClosestTouch, TraverseParms.For(pawn), 1500f,
                    res => !res.IsForbidden(pawn) && Verse.AI.ReservationUtility.CanReserve(pawn, res)
                )?.Position);
                var onMap = pawn.Map.listerThings.ThingsOfDef(cost.thingDef).Sum(thing => thing.stackCount);
                var unforbidden = pawn.Map.listerThings.ThingsOfDef(cost.thingDef).Where(thing => !thing.IsForbidden(pawn)).Sum(thing => thing.stackCount);
                line += $" | {cost.thingDef.defName} need {needed}: onMap={onMap} unforbidden={unforbidden} availableAnywhere={anywhere} closest={closest}";
            }

            lines.Add(line);
        }

        return lines;
    }
}
