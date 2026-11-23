using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.BridgeTools.Scenarios;

// The resource list and bill tweaks for MultiFloors levels
public static partial class TweaksScenarios
{
    // The resource list at the top left: the wall's default material is stocked upstairs and steel on the ground. From the
    // ground, each count the readout reads adds the level's, and with the setting off it's the ground's alone again.
    private static async Task LevelsReadoutCountsOtherLevels(
        ScenarioContext context
    )
    {
        using var stock = new LevelStock(context);
        if (stock.Level.Map == null)
        {
            return;
        }

        var counter = context.Map.resourceCounter;
        var levelCounter = stock.Level.Map.resourceCounter;
        var ground = counter.GetCount(stock.Upstairs);
        var upstairs = levelCounter.GetCount(stock.Upstairs);
        var category = stock.Upstairs.FirstThingCategory;
        var summed = Ordo.Tweaks.MultiFloorsReadout.GetCount(counter, stock.Upstairs);
        context.Check($"the level's counter has the {stock.Upstairs.label}: {upstairs}", upstairs >= 75);
        context.Check($"the count adds both levels: {summed} = {ground} + {upstairs}", summed == ground + upstairs);
        var listed = Ordo.Tweaks.MultiFloorsReadout.AllCountedAmounts(counter).TryGetValue(stock.Upstairs);
        context.Check($"the simple list adds both levels: {listed}", listed == ground + upstairs);
        var own = counter.AllCountedAmounts.TryGetValue(stock.Upstairs);
        context.Check($"the ground's own counter is unchanged: {own}", own == ground);
        var inCategory = category == null ? -1 : Ordo.Tweaks.MultiFloorsReadout.GetCountIn(counter, category);
        context.Check($"the {category?.label} category adds both levels: {inCategory}",
            category != null && inCategory == counter.GetCountIn(category) + levelCounter.GetCountIn(category));

        var vanilla = new MethodInfo[]
        {
            AccessTools.PropertyGetter(typeof(ResourceCounter), nameof(ResourceCounter.AllCountedAmounts)),
            AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCount)),
            AccessTools.Method(typeof(ResourceCounter), nameof(ResourceCounter.GetCountIn), [typeof(ThingCategoryDef)])
        };
        foreach (var (method, name) in new (MethodBase, string)[]
                 {
                     (AccessTools.Method(typeof(ResourceReadout), "DoReadoutSimple"), "ResourceReadout.DoReadoutSimple"),
                     (AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.DrawResourceSimple)), "ResourceReadout.DrawResourceSimple"),
                     (AccessTools.Method(typeof(Listing_ResourceReadout), nameof(Listing_ResourceReadout.DoCategory)), "Listing_ResourceReadout.DoCategory"),
                     (AccessTools.Method(typeof(Listing_ResourceReadout), "DoThingDef"), "Listing_ResourceReadout.DoThingDef")
                 })
        {
            var called = PatchProcessor.GetCurrentInstructions(method).Select(instruction => instruction.operand as MethodInfo).Where(m => m != null).ToList();
            var ours = called.Count(m => m.DeclaringType?.FullName == "Ordo.Tweaks.MultiFloorsReadout");
            var direct = called.Count(m => vanilla.Contains(m));
            context.Check($"{name} counts through Tweaks: {ours} calls, {direct} straight to one level's counter", ours > 0 && direct == 0);
        }

        await WithSetting("ReadoutAcrossLevels", "false", () =>
        {
            var off = Ordo.Tweaks.MultiFloorsReadout.GetCount(counter, stock.Upstairs);
            context.Check($"with the setting off, the count is the ground's: {off}", off == ground);
            context.Check("with the setting off, the list is the ground counter's own", Ordo.Tweaks.MultiFloorsReadout.AllCountedAmounts(counter) == counter.AllCountedAmounts);
            return Task.CompletedTask;
        });
    }

    // A "Do until you have 8" simple meal bill on a stove on the ground, with 2 meals stockpiled on the ground and 7
    // upstairs. Across levels it counts 9 and isn't done now; counting only the ground's stockpile, or with the setting
    // off, it counts 2.
    private static async Task LevelsBillsCountOtherLevels(
        ScenarioContext context
    )
    {
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var meal = ThingDefOf.MealSimple;
        var stove = (Building_WorkTable)context.Build(ThingDef.Named("FueledStove"), -6, 0);
        var bill = (Bill_Production)DefDatabase<RecipeDef>.GetNamed("CookMealSimple").MakeNewBill();
        bill.repeatMode = BillRepeatModeDefOf.TargetCount;
        bill.targetCount = 8;
        stove.BillStack.AddBill(bill);

        var groundZone = LevelStock.Stockpile(context.Map, context.Cell(-10, -10));
        try
        {
            context.Place(meal, -10, -10, stackCount: 2);
            LevelStock.Stockpile(level.Map, context.Cell(10, 10));
            var upstairs = ThingMaker.MakeThing(meal);
            upstairs.stackCount = 7;
            GenSpawn.Spawn(upstairs, context.Cell(10, 10), level.Map);
            context.Map.resourceCounter.UpdateResourceCounts();
            level.Map.resourceCounter.UpdateResourceCounts();

            var counter = bill.recipe.WorkerCounter;
            var groundCount = context.Map.resourceCounter.GetCount(meal);
            var levelCount = level.Map.resourceCounter.GetCount(meal);
            context.Check($"the ground has 2 meals and upstairs 7: {groundCount}, {levelCount}", groundCount == 2 && levelCount == 7);
            context.Check($"the bill counts both levels: {counter.CountProducts(bill)}", counter.CountProducts(bill) == 9);
            context.Check("with 9 of 8, the bill isn't worked now", !bill.ShouldDoNow());

            // A bill limited by hit points counts thing by thing, not through the resource counter. Vanilla counts each
            // stack there as one, so it's one stack on each level. The unofficial 1.7 Better Workbench Management (the only
            // one with DetachedBillData) adds the rest of each stack, so it's every meal
            var wholeStacks = AccessTools.TypeByName("ImprovedWorkbenches.DetachedBillData") != null;
            bill.hpRange = new FloatRange(0.5f, 1f);
            var byThing = counter.CountProducts(bill);
            context.Check($"counting thing by thing, both levels' stacks count: {byThing}", byThing == (wholeStacks ? 9 : 2));
            bill.hpRange = FloatRange.ZeroToOne;

            bill.SetIncludeGroup(groundZone.slotGroup);
            context.Check($"counting only the ground's stockpile, it's 2: {counter.CountProducts(bill)}", counter.CountProducts(bill) == 2);
            bill.SetIncludeGroup(null);

            var billMap = AccessTools.PropertyGetter(typeof(Bill), nameof(Bill.Map));
            foreach (var (method, name, postfixed) in new (MethodBase, string, bool)[]
                     {
                         (AccessTools.DeclaredMethod(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts)), "RecipeWorkerCounter.CountProducts", true),
                         (AccessTools.DeclaredMethod(typeof(RecipeWorkerCounter_ButcherAnimals), nameof(RecipeWorkerCounter.CountProducts)), "RecipeWorkerCounter_ButcherAnimals.CountProducts", true),
                         (AccessTools.DeclaredMethod(typeof(RecipeWorkerCounter_MakeStoneBlocks), nameof(RecipeWorkerCounter.CountProducts)), "RecipeWorkerCounter_MakeStoneBlocks.CountProducts", true),
                         (AccessTools.Method(typeof(RecipeWorkerCounter), "GetCarriedCount"), "RecipeWorkerCounter.GetCarriedCount", false)
                     })
            {
                var patches = Harmony.GetPatchInfo(method);
                var transpiled = patches?.Transpilers.Any(patch => patch.owner == ModId) == true;
                var counted = !postfixed || patches?.Postfixes.Any(patch => patch.owner == ModId) == true;
                var direct = PatchProcessor.GetCurrentInstructions(method).Count(instruction => instruction.operand as MethodInfo == billMap);
                context.Check($"{name} reads the bill's map through Tweaks ({direct} direct reads left), counting other levels: {counted}",
                    transpiled && direct == 0 && counted);
            }

            await WithSetting("BillsAcrossLevels", "false", () =>
            {
                context.Check($"with the setting off, the bill counts the ground's 2: {counter.CountProducts(bill)}", counter.CountProducts(bill) == 2);
                context.Check("with 2 of 8, the bill is worked now", bill.ShouldDoNow());
                bill.hpRange = new FloatRange(0.5f, 1f);
                context.Check($"with the setting off, thing by thing counts the ground's stack: {counter.CountProducts(bill)}", counter.CountProducts(bill) == (wholeStacks ? 2 : 1));
                bill.hpRange = FloatRange.ZeroToOne;
                return Task.CompletedTask;
            });
        }
        finally
        {
            groundZone.Delete();
            context.Map.resourceCounter.UpdateResourceCounts();
        }
    }

    // Work Tab and MultiFloors' prioritized scanner. A colonist does only construction, at Work Tab priority 1, with one of
    // its tasks at 1, one switched off and one at 5. MultiFloors' cached lists, rebuilt with a level above, keep the first
    // as high priority, leave out the one switched off, and put the one at 5 with the low priority work (the threshold is 2
    // by default). With the setting off, MultiFloors' own lists still have the task switched off.
    private static async Task LevelsWorkTabPriorities(
        ScenarioContext context
    )
    {
        var colonist = context.Colonist(-6, 0);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var scanner = AccessTools.TypeByName("MultiFloors.HarmonyPatches.HarmonyPatch_ScanJobsOnOtherLevelPrioritized");
        var cache = AccessTools.Method(scanner, "CacheWorkGiversInOrder");
        var fields = AccessTools.TypeByName("MultiFloors.PrepatcherFields");
        var highList = AccessTools.Method(fields, "HighPriorityWorkGiversInOrderNormal");
        var lowList = AccessTools.Method(fields, "LowPriorityWorkGiversInOrderNormal");
        var extensions = AccessTools.TypeByName("WorkTab.Pawn_Extensions");
        var setType = AccessTools.Method(extensions, "SetPriority", [typeof(Pawn), typeof(WorkTypeDef), typeof(int), typeof(List<int>)]);
        var setGiver = AccessTools.Method(extensions, "SetPriority", [typeof(Pawn), typeof(WorkGiverDef), typeof(int), typeof(List<int>)]);
        context.Check("found MultiFloors' scanner and Work Tab's priorities",
            cache != null && highList != null && lowList != null && setType != null && setGiver != null);
        if (cache == null || highList == null || lowList == null || setType == null || setGiver == null)
        {
            return;
        }

        var givers = WorkTypeDefOf.Construction.workGiversByPriority.Where(giver => !giver.emergency).Take(3).ToList();
        context.Check($"construction has three tasks: {string.Join(", ", givers.Select(giver => giver.defName))}", givers.Count == 3);
        var usedPriorities = Find.PlaySettings.useWorkPriorities;
        Find.PlaySettings.useWorkPriorities = true;
        try
        {
            var hours = Enumerable.Range(0, 24).ToList();
            var settings = colonist.workSettings;
            settings.DisableAll();
            setType.Invoke(null, [colonist, WorkTypeDefOf.Construction, 1, hours]);
            setGiver.Invoke(null, [colonist, givers[0], 1, hours]);
            setGiver.Invoke(null, [colonist, givers[1], 0, hours]);
            setGiver.Invoke(null, [colonist, givers[2], 5, hours]);

            (List<WorkGiver> high, List<WorkGiver> low) Lists()
            {
                settings.Notify_UseWorkPrioritiesChanged();
                cache.Invoke(null, [settings]);
                return ((List<WorkGiver>)highList.Invoke(null, [settings]), (List<WorkGiver>)lowList.Invoke(null, [settings]));
            }

            string Names(
                List<WorkGiver> list
            ) => string.Join(", ", list.Select(giver => giver.def.defName));

            var (high, low) = Lists();
            context.Check($"Work Tab's list has the task at 1 and the one at 5, not the one switched off: {Names(settings.WorkGiversInOrderNormal)}",
                settings.WorkGiversInOrderNormal.Any(giver => giver.def == givers[0])
                && settings.WorkGiversInOrderNormal.Any(giver => giver.def == givers[2])
                && settings.WorkGiversInOrderNormal.All(giver => giver.def != givers[1]));
            context.Check($"high priority: the task at 1 only. High: {Names(high)}; low: {Names(low)}",
                high.Any(giver => giver.def == givers[0]) && high.All(giver => giver.def != givers[2]));
            context.Check("low priority: the task at 5", low.Any(giver => giver.def == givers[2]));
            context.Check("the task switched off is in neither", high.Concat(low).All(giver => giver.def != givers[1]));

            await WithSetting("WorkTabPrioritiesAcrossLevels", "false", () =>
            {
                var (offHigh, offLow) = Lists();
                context.Check($"with the setting off, MultiFloors' lists have the task switched off: {Names(offHigh)}; {Names(offLow)}",
                    offHigh.Concat(offLow).Any(giver => giver.def == givers[1]));
                return Task.CompletedTask;
            });
        }
        finally
        {
            Find.PlaySettings.useWorkPriorities = usedPriorities;
            colonist.workSettings.Notify_UseWorkPrioritiesChanged();
        }
    }

    // A charged neural supercharger upstairs, set to auto-use for everyone, and an idle colonist on the ground who has
    // never been charged. The colonist takes the stairs and gets charged; with the setting off, it stays on the ground.
    private static async Task LevelsSupercharger(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var supercharger = level.Build(ThingDef.Named("NeuralSupercharger"), context.Cell(10, 8));
        var comp = supercharger.TryGetComp<CompNeuralSupercharger>();
        comp.autoUseMode = CompNeuralSupercharger.AutoUseMode.AutoUseForEveryone;
        AccessTools.Field(typeof(CompRechargeable), "ticksUntilCharged").SetValue(comp, 0);
        context.Check("the supercharger upstairs is charged", comp.Charged);

        foreach (var need in colonist.needs.AllNeeds)
        {
            need.CurLevelPercentage = 1f;
        }

        colonist.health.lastReceivedNeuralSuperchargeTick = -1;
        await WithSetting("SuperchargersAcrossLevels", on ? "true" : "false", async () =>
        {
            colonist.drafter.Drafted = false;
            if (on)
            {
                await context.Until($"the colonist goes upstairs and is charged: {Stage.Describe(colonist)} on {colonist.MapHeld}",
                    () => colonist.health.lastReceivedNeuralSuperchargeTick != -1, 4000);
                context.Check($"it was charged upstairs: {colonist.MapHeld}", colonist.MapHeld == level.Map);
            }
            else
            {
                await context.Step(1500);
                // Not checked to stay on the ground: idle, it may go upstairs for MultiFloors' own work scan
                context.Check($"it wasn't charged: {Stage.Describe(colonist)} on {colonist.MapHeld}",
                    colonist.health.lastReceivedNeuralSuperchargeTick == -1);
            }
        });
    }

    // MultiFloorsFixes 1: a hauler delivering to a blueprint upstairs, with a job queued behind its delivery. On arrival its
    // follow-up delivery is an ordinary job and the queued one is kept. Off, MultiFloors makes it a forced order and clears
    // the queue.
    private static async Task LevelsFixFollowUpJob(
        ScenarioContext context,
        bool on
    )
    {
        var deliver = DefDatabase<WorkGiverDef>.GetNamed("MF_DeliverResourcesAcrossLevel");
        var hauler = context.Worker(-6, 0, deliver.workType);
        context.Place(ThingDefOf.WoodLog, -8, 0, stackCount: 75);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, context.Cell(10, 10), level.Map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
        await WithSetting("MultiFloorsFixes", on ? "true" : "false", async () =>
        {
            await context.Until($"the hauler sets off with the wood: {Stage.Describe(hauler)}", () => hauler.CurJobDef?.defName == "MF_DeliverResourcesAcrossLevel", 2000);
            var queued = JobMaker.MakeJob(JobDefOf.Wait, 600);
            hauler.jobs.jobQueue.EnqueueLast(queued);
            await context.Until($"the hauler arrives upstairs: {Stage.Describe(hauler)} on {hauler.MapHeld}", () => hauler.MapHeld == level.Map, 2000, 1);
            var followUp = hauler.CurJob;
            var kept = hauler.jobs.jobQueue.Contains(queued) || hauler.CurJob == queued;
            var forced = followUp?.playerForced ?? false;
            var state = $"follow-up {followUp?.def.defName} forced {forced}, queued job kept {kept}";
            if (on)
            {
                context.Check($"the follow-up is an ordinary job and the queue is kept: {state}", followUp?.def.defName == "MF_DeliverToContainer" && !forced && kept);
            }
            else
            {
                context.Check($"with the fix off, MultiFloors forces it and clears the queue: {state}", forced && !kept);
            }
        });
    }

    // MultiFloorsFixes 2: an exception thrown from the cross-level work scan, by a temporary patch. The scanners' flags are
    // put back and the pawn is home. Off, the flag stays set, which turns the scan off for every pawn.
    private static async Task LevelsFixScanFlags(
        ScenarioContext context,
        bool on
    )
    {
        var worker = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var prioritized = AccessTools.TypeByName("MultiFloors.HarmonyPatches.HarmonyPatch_ScanJobsOnOtherLevelPrioritized");
        var plain = AccessTools.TypeByName("MultiFloors.HarmonyPatches.HarmonyPatch_ScanJobsOnOtherLevel");
        var scanningLevels = AccessTools.Field(prioritized, "ScanningOtherLevels");
        var scanningHigh = AccessTools.Field(prioritized, "ScanningHighPriorityWorkGivers");
        var scanningLevel = AccessTools.Field(plain, "ScanningOtherLevel");
        var harmony = new Harmony("Ordo.BridgeTools.Scenarios.ScanThrows");
        harmony.Patch(AccessTools.Method(plain, "ScanWorksOnOtherLevelVerticallyOutWard"), prefix: new HarmonyMethod(typeof(TweaksScenarios), nameof(ThrowFromScan)));
        try
        {
            await WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
            {
                Exception thrown = null;
                try
                {
                    new JobGiver_Work().TryIssueJobPackage(worker, default);
                }
                catch (Exception e)
                {
                    thrown = e;
                }

                var stuck = (bool)scanningLevels.GetValue(null) || (bool)scanningLevel.GetValue(null) || !(bool)scanningHigh.GetValue(null);
                context.Check($"the temporary patch threw from the scan: {thrown?.GetType().Name}", thrown != null);
                if (on)
                {
                    context.Check($"the scan's flags are back: levels {scanningLevels.GetValue(null)}, level {scanningLevel.GetValue(null)}, high {scanningHigh.GetValue(null)}", !stuck);
                    context.Check($"the pawn is back on its own map: {worker.Map}", worker.Map == context.Map);
                }
                else
                {
                    context.Check("with the fix off, a flag stays set", stuck);
                }

                return Task.CompletedTask;
            });
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            scanningLevels.SetValue(null, false);
            scanningLevel.SetValue(null, false);
            scanningHigh.SetValue(null, true);
        }
    }

    public static bool ThrowFromScan() => throw new InvalidOperationException("thrown on purpose by a scenario");

    // MultiFloorsFixes 3: wood designated to be hauled upstairs, and a hauler drafted once it carries it. The wood it drops
    // is designated again. Off, the designation is gone.
    private static async Task LevelsFixInterruptedHaul(
        ScenarioContext context,
        bool on
    )
    {
        var hauler = context.Worker(-6, 0, WorkTypeDefOf.Hauling);
        var wood = context.Place(ThingDefOf.WoodLog, -8, 0, stackCount: 20);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        Ordo.Tweaks.MultiFloorsFixes.DesignateHaulTo(wood, level.Map);
        await WithSetting("MultiFloorsFixes", on ? "true" : "false", async () =>
        {
            await context.Until($"the hauler picks up the wood: {Stage.Describe(hauler)}", () => hauler.carryTracker.CarriedThing?.def == ThingDefOf.WoodLog, 2000, 1);
            var carried = hauler.carryTracker.CarriedThing;
            hauler.drafter.Drafted = true;
            await context.Step(5);
            var dropped = carried.Spawned ? carried : context.Map.listerThings.ThingsOfDef(ThingDefOf.WoodLog).FirstOrDefault();
            var destination = dropped == null ? null : Ordo.Tweaks.MultiFloorsFixes.HaulDestination(dropped);
            var state = $"dropped {dropped} at {dropped?.Position} on {dropped?.Map}, designated to {destination}";
            context.Check($"the wood was dropped on the ground: {state}", dropped?.Map == context.Map);
            context.Check(on ? $"it's designated upstairs again: {state}" : $"with the fix off, its designation is gone: {state}",
                on ? destination == level.Map : destination == null);
            hauler.drafter.Drafted = false;
        });
    }

    // MultiFloorsFixes 4: a colonist carrying wood, moved onto the level in place as MultiFloors' work scan does, and
    // MultiFloors' drop called. The wood lands where the colonist really is. Off, it lands upstairs.
    private static async Task LevelsFixDropDuringScan(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        var wood = context.Place(ThingDefOf.WoodLog, -6, 0, stackCount: 10);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var teleportTo = AccessTools.Method("MultiFloors.LevelUtility:UnsafeTeleportTo");
        var teleportBack = AccessTools.Method("MultiFloors.LevelUtility:UnsafeTeleportBack");
        var drop = AccessTools.Method("MultiFloors.Jobs.CrossLevelHaulJobUtility:TryDropCarriedThing");
        wood.DeSpawn();
        colonist.carryTracker.TryStartCarry(wood);
        context.Check("the colonist carries the wood", colonist.carryTracker.CarriedThing == wood);
        await WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            teleportTo.Invoke(null, [colonist, level.Map, context.Cell(6, 6)]);
            try
            {
                drop.Invoke(null, [colonist]);
            }
            finally
            {
                teleportBack.Invoke(null, [colonist]);
            }

            context.Check(on ? $"the wood landed on the ground: {wood.Map}" : $"with the fix off, it landed upstairs: {wood.Map}",
                on ? wood.Map == context.Map : wood.Map == level.Map);
            return Task.CompletedTask;
        });
    }

    // MultiFloorsFixes 5: an elevator the colonist has no path through, so MultiFloors finds no cell to enter by. The job
    // ends. Off, it carries on as if the pawn had arrived.
    private static async Task LevelsFixBlockedExit(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var elevatorType = AccessTools.TypeByName("MultiFloors.Elevator");
        var elevatorDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.thingClass != null && elevatorType.IsAssignableFrom(def.thingClass));
        context.Check("MultiFloors has an elevator", elevatorDef != null);
        if (elevatorDef == null)
        {
            return;
        }

        var elevator = ThingMaker.MakeThing(elevatorDef, elevatorDef.MadeFromStuff ? GenStuff.DefaultStuffFor(elevatorDef) : null);
        var validate = AccessTools.Method("MultiFloors.Jobs.CrossLevelMoveJobUtility:ValidateEnterCell");
        await WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            var job = JobMaker.MakeJob(JobDefOf.Wait, 2000);
            colonist.jobs.StartJob(job, JobCondition.InterruptForced);
            // Jobs are pooled: an ended job can come straight back as the next one, so compare ids
            var jobId = job.loadID;
            var cell = (IntVec3)validate.Invoke(null, [elevator, null, colonist]);
            context.Check($"no cell to enter by: {cell}", !cell.IsValid);
            context.Check(on ? $"the job ended: {colonist.CurJobDef}" : $"with the fix off, the job carries on: {colonist.CurJobDef}",
                on ? colonist.CurJob?.loadID != jobId : colonist.CurJob?.loadID == jobId);
            return Task.CompletedTask;
        });
        elevator.Destroy();
    }

    // MultiFloorsFixes 6 and 7: a one-cell stockpile upstairs and wood on the ground with nowhere to go there.
    // 6: with the cell blacklisted, as a full cell at the same coordinates on another level makes it, the stockpile is
    // still found. Off, it isn't.
    // 7: once found, the cell's planned load is counted. Off, the first one on a level isn't.
    private static async Task LevelsFixMarkerCells(
        ScenarioContext context,
        bool on,
        bool blacklisted
    )
    {
        var wood = context.Place(ThingDefOf.WoodLog, -8, 0, stackCount: 10);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var cell = context.Cell(10, 10);
        var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, level.Map.zoneManager);
        level.Map.zoneManager.RegisterZone(zone);
        zone.AddCell(cell);

        var marker = AccessTools.TypeByName("MultiFloors.Jobs.CrossLevelHaulingMarker");
        var reserved = (IDictionary)AccessTools.Field(marker, "CellReservedByMap").GetValue(null);
        var maxCount = (IDictionary)AccessTools.Field(marker, "CellMaxCountByMap").GetValue(null);
        var cells = (HashSet<IntVec3>)AccessTools.Field(marker, "BlacklistedCells").GetValue(null);
        var args = new object[] { context.Map, null };
        AccessTools.Method("MultiFloors.LevelUtility:TryGetLevelControllerOnCurrentTile").Invoke(null, args);
        var controller = args[1];
        reserved.Clear();
        maxCount.Clear();
        cells.Clear();
        if (blacklisted)
        {
            cells.Add(cell);
        }

        try
        {
            await WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
            {
                var find = new object[] { wood, controller, IntVec3.Invalid, null, StoragePriority.Unstored };
                var found = (bool)AccessTools.Method(marker, "TryFindHaulDestinationFor").Invoke(null, find);
                var result = $"found {found}: {find[2]} on {find[3]}";
                if (blacklisted)
                {
                    context.Check(on ? $"the stockpile upstairs is found: {result}" : $"with the fix off, it's hidden: {result}",
                        on ? found && (IntVec3)find[2] == cell && find[3] == level.Map : !found);
                    return Task.CompletedTask;
                }

                context.Check($"the stockpile upstairs is found: {result}", found && find[3] == level.Map);
                var count = 0;
                if (reserved.Contains(level.Map.uniqueID))
                {
                    foreach (var entry in (IEnumerable)reserved[level.Map.uniqueID])
                    {
                        count += (int)AccessTools.Field(entry.GetType(), "Count").GetValue(entry);
                    }
                }

                context.Check(on ? $"its planned load is counted: {count}" : $"with the fix off, it isn't: {count}", on ? count == 1 : count == 0);
                return Task.CompletedTask;
            });
        }
        finally
        {
            reserved.Clear();
            maxCount.Clear();
            cells.Clear();
        }
    }

    // MultiFloorsFixes 8: the designated haul job asked for wood with no designation. It gives no job. Off, it throws.
    private static Task LevelsFixHaulWithoutDesignation(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        var wood = context.Place(ThingDefOf.WoodLog, -8, 0, stackCount: 10);
        var giverType = AccessTools.TypeByName("MultiFloors.Jobs.WorkGiver_HaulDesignatedToDestMap");
        var giver = (WorkGiver_Scanner)DefDatabase<WorkGiverDef>.AllDefs.First(def => def.giverClass == giverType).Worker;
        return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            Exception thrown = null;
            Job job = null;
            try
            {
                job = giver.JobOnThing(colonist, wood);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            context.Check(on ? $"no job and no exception: {job}, {thrown?.GetType().Name}" : $"with the fix off, it throws: {thrown?.GetType().Name}",
                on ? job == null && thrown == null : thrown is NullReferenceException);
            return Task.CompletedTask;
        });
    }

    // MultiFloorsFixes 9: a temporary floor lock saved and loaded through the save system, as MultiFloorManager's data is
    // (a holder calling the same method, written to a temporary file). It's back after loading. Off, it's lost, which is
    // what makes the pawn's saved Lock floor permanent. Also checks the patch is on MultiFloorManager.ExposeData.
    private static Task LevelsFixTemporaryLockSaved(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        var managerType = AccessTools.TypeByName("MultiFloors.MultiFloorManager");
        var patched = Harmony.GetPatchInfo(AccessTools.Method(managerType, "ExposeData"))?.Postfixes.Any(patch => patch.PatchMethod.DeclaringType == typeof(Ordo.Tweaks.MultiFloorsFixes)) ?? false;
        context.Check("the fix is patched onto MultiFloorManager.ExposeData", patched);
        var path = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "ordo_lock_test.xml");
        return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            var saved = new Ordo.Tweaks.MultiFloorsFixes.TemporaryLocksForTests();
            saved.Locks.Add((colonist, 1234));
            Scribe.saver.InitSaving(path, "OrdoLockTest");
            Scribe_Deep.Look(ref saved, "holder");
            Scribe.saver.FinalizeSaving();

            Ordo.Tweaks.MultiFloorsFixes.TemporaryLocksForTests loaded = null;
            try
            {
                Scribe.loader.InitLoading(path);
                Scribe.loader.crossRefs.loadedObjectDirectory.RegisterLoaded(colonist);
                Scribe_Deep.Look(ref loaded, "holder");
                Scribe.loader.FinalizeLoading();
            }
            finally
            {
                System.IO.File.Delete(path);
            }

            var state = string.Join(", ", loaded?.Locks.Select(entry => $"{entry.pawn?.LabelShort} at {entry.tick}") ?? []);
            context.Check(on ? $"the temporary lock is back after loading: {state}" : $"with the fix off, it's lost: {state}",
                on ? loaded?.Locks.Count == 1 && loaded.Locks[0].pawn == colonist && loaded.Locks[0].tick == 1234 : loaded?.Locks.Count == 0);
            return Task.CompletedTask;
        });
    }

    // MultiFloorsFixes 10: a colonist given a temporary floor lock, as "Prioritize work on this level" does, then unlocked
    // and locked again with the Lock floor gizmo. The temporary lock is dropped, so its timer won't undo the player's.
    // Off, it's still pending.
    private static async Task LevelsFixManualLockKept(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var managerType = AccessTools.TypeByName("MultiFloors.MultiFloorManager");
        var manager = Current.Game.components.First(component => managerType.IsInstanceOfType(component));
        var lockFlag = AccessTools.Method("MultiFloors.PrepatcherFields:StayOnCurrentMap");
        var gizmo = (Command_Toggle)AccessTools.Method("MultiFloors.GizmoFactory:StayOnCurrentMap").Invoke(null, [colonist, 1f]);
        try
        {
            await WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
            {
                AccessTools.Method(managerType, "LockPawn").Invoke(manager, [colonist]);
                context.Check("the temporary lock is pending", Ordo.Tweaks.MultiFloorsFixes.HasTemporaryLock(colonist));
                gizmo.toggleAction();
                gizmo.toggleAction();
                context.Check($"locked by hand: {gizmo.isActive()}", gizmo.isActive());
                var pending = Ordo.Tweaks.MultiFloorsFixes.HasTemporaryLock(colonist);
                context.Check(on ? $"the temporary lock was dropped: {pending}" : $"with the fix off, it's still pending: {pending}", on ? !pending : pending);
                return Task.CompletedTask;
            });
        }
        finally
        {
            if (gizmo.isActive())
            {
                gizmo.toggleAction();
            }

            var locks = (IList)AccessTools.Field(managerType, "lockedPawns").GetValue(manager);
            for (var i = locks.Count - 1; i >= 0; i--)
            {
                if (((ValueTuple<Pawn, int>)locks[i]).Item1 == colonist)
                {
                    locks.RemoveAt(i);
                }
            }
        }
    }

    // MultiFloorsFixes 11: MultiFloors' elevator wait toil, for an unpowered elevator built on the site (spawned, so it has
    // its power comp), ticked 600 times for a colonist with a job. The job ends. Off, it never does.
    private static Task LevelsFixUnpoweredElevatorWait(
        ScenarioContext context,
        bool on
    )
    {
        var colonist = context.Colonist(-6, 0);
        var elevatorType = AccessTools.TypeByName("MultiFloors.Elevator");
        var elevatorDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.thingClass != null && elevatorType.IsAssignableFrom(def.thingClass)
            && def.GetCompProperties<CompProperties_Power>() is { } power && power.PowerConsumption > 0f);
        context.Check("MultiFloors has a powered elevator", elevatorDef != null);
        if (elevatorDef == null)
        {
            return Task.CompletedTask;
        }

        var elevator = context.Build(elevatorDef, 14, 4);
        context.Check($"the elevator has no power: required {AccessTools.Property(elevatorType, "PowerRequired").GetValue(elevator)}, on {elevator.TryGetComp<CompPowerTrader>()?.PowerOn}",
            elevator.TryGetComp<CompPowerTrader>() is { PowerOn: false });
        var stairType = AccessTools.TypeByName("MultiFloors.Stair");
        var getter = Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(stairType),
            new ElevatorBox(elevator), AccessTools.Method(typeof(ElevatorBox), nameof(ElevatorBox.Get)).MakeGenericMethod(stairType));
        var toil = (Toil)AccessTools.Method("MultiFloors.Jobs.Toils_Elevator:WaitForElevator").Invoke(null, [colonist, getter, (Action)(() => { }), 600]);
        return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            var job = JobMaker.MakeJob(JobDefOf.Wait, 5000);
            colonist.jobs.StartJob(job, JobCondition.InterruptForced);
            // Jobs are pooled: an ended job can come straight back as the next one, so compare ids
            var jobId = job.loadID;
            for (var i = 0; i < 600 && colonist.CurJob?.loadID == jobId; i++)
            {
                toil.tickAction();
            }

            context.Check(on ? $"the wait ended: {colonist.CurJobDef}" : $"with the fix off, it waits on: {colonist.CurJobDef}",
                on ? colonist.CurJob?.loadID != jobId : colonist.CurJob?.loadID == jobId);
            return Task.CompletedTask;
        });
    }

    private sealed class ElevatorBox(Thing elevator)
    {
        public T Get<T>() where T : class => elevator as T;
    }

    // MultiFloorsFixes 13: the up-stair's transfer policy set to move 1 revolver, and two revolvers of different quality on
    // the ground, given to its auto-transfer. One is queued. Off, both are: each quality's transferable gets the whole limit.
    private static Task LevelsFixTransferLimit(
        ScenarioContext context,
        bool on
    )
    {
        var level = new UpperLevel(context);
        if (level.Map == null)
        {
            level.Dispose();
            return Task.CompletedTask;
        }

        var revolverDef = ThingDef.Named("Gun_Revolver");
        var stair = (MapPortal)context.Map.thingGrid.ThingsListAt(context.Cell(6, 6)).First(thing => thing is MapPortal);
        var worker = AccessTools.Field(stair.GetType(), "AutoTransferWorker").GetValue(stair);
        var policy = AccessTools.Field(stair.GetType(), "TransferPolicy").GetValue(stair);
        var revolvers = new List<Thing>();
        foreach (var quality in new[] { QualityCategory.Normal, QualityCategory.Good })
        {
            var revolver = ThingMaker.MakeThing(revolverDef);
            revolver.TryGetComp<CompQuality>().SetQuality(quality, ArtGenerationContext.Colony);
            revolvers.Add(GenSpawn.Spawn(revolver, context.Cell(-8 + revolvers.Count, 2), context.Map));
        }

        try
        {
            return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
            {
                AccessTools.Method(policy.GetType(), "AddOrUpdatePolicy").Invoke(policy, [revolverDef, 1]);
                stair.leftToLoad = null;
                AccessTools.Method(worker.GetType(), "AutoTransferItems").Invoke(worker, [new List<Verse.AI.Group.Lord>(), revolvers, new HashSet<int>()]);
                var queued = stair.leftToLoad?.Where(transferable => transferable.ThingDef == revolverDef).Sum(transferable => transferable.CountToTransfer) ?? 0;
                context.Check(on ? $"one revolver is queued: {queued}" : $"with the fix off, both are: {queued}", on ? queued == 1 : queued == 2);
                return Task.CompletedTask;
            });
        }
        finally
        {
            stair.leftToLoad = null;
            AccessTools.Method(policy.GetType(), "RemoveFromPolicy").Invoke(policy, [revolverDef]);
            level.Dispose();
        }
    }

    // MultiFloorsFixes 15: a fueled stove on the ground with a simple meal bill, linked to the level above for its
    // ingredients, and forbidden potatoes there. The monitor's pass doesn't designate them. Off, it does, and counts them
    // as on their way though nobody will haul them.
    private static Task LevelsFixForbiddenIngredients(
        ScenarioContext context,
        bool on
    )
    {
        var level = new UpperLevel(context);
        if (level.Map == null)
        {
            level.Dispose();
            return Task.CompletedTask;
        }

        var stove = context.Build(ThingDef.Named("FueledStove"), -8, 4);
        ((IBillGiver)stove).BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("CookMealSimple").MakeNewBill());
        var potatoes = ThingMaker.MakeThing(ThingDef.Named("RawPotatoes"));
        potatoes.stackCount = 30;
        potatoes = GenSpawn.Spawn(potatoes, context.Cell(10, 10), level.Map);
        potatoes.SetForbidden(true, warnOnFail: false);
        try
        {
            return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
            {
                Ordo.Tweaks.MultiFloorsFixes.LinkBillGiverForTests(stove, 1);
                Ordo.Tweaks.MultiFloorsFixes.DesignateMissingIngredientsForTests();
                var destination = Ordo.Tweaks.MultiFloorsFixes.HaulDestination(potatoes);
                context.Check(on ? $"the forbidden potatoes aren't designated: {destination}" : $"with the fix off, they're designated to the stove's level: {destination}",
                    on ? destination == null : destination == context.Map);
                return Task.CompletedTask;
            });
        }
        finally
        {
            Ordo.Tweaks.MultiFloorsFixes.LinkBillGiverForTests(stove, 0);
            level.Dispose();
        }
    }

    // MultiFloorsFixes, performance: a level work setting with hauling switched off filters every work giver. The result
    // leaves out hauling's givers and nothing else, and a second call gives back the same list instead of a new one.
    // Off, each call makes a new list. With everything switched back on, the list passed in comes back.
    private static Task LevelsFixWorkFilterReused(
        ScenarioContext context,
        bool on
    )
    {
        var settingType = AccessTools.TypeByName("MultiFloors.LevelWorkSettings.LevelWorkSetting");
        var setting = Activator.CreateInstance(settingType);
        var filter = AccessTools.Method(settingType, "FilterWorkGivers");
        var givers = DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(def => def.workType != null).Select(def => def.Worker).ToList();
        var expected = givers.Where(giver => giver.def.workType != WorkTypeDefOf.Hauling || !giver.def.workType.visible).ToList();
        return WithSetting("MultiFloorsFixes", on ? "true" : "false", () =>
        {
            AccessTools.Method(settingType, "Disable").Invoke(setting, [WorkTypeDefOf.Hauling]);
            var first = (List<WorkGiver>)filter.Invoke(setting, [givers]);
            var firstContent = first.ToList();
            var second = (List<WorkGiver>)filter.Invoke(setting, [givers]);
            context.Check($"hauling's givers are left out and nothing else: {firstContent.Count} of {givers.Count}, expected {expected.Count}",
                firstContent.SequenceEqual(expected) && second.SequenceEqual(expected));
            context.Check(on ? "the second call reuses the list" : "with the fix off, each call makes a new list", on ? ReferenceEquals(first, second) : !ReferenceEquals(first, second));
            AccessTools.Method(settingType, "Enable").Invoke(setting, [WorkTypeDefOf.Hauling]);
            context.Check("with everything on, the list passed in comes back", ReferenceEquals(filter.Invoke(setting, [givers]), givers));
            return Task.CompletedTask;
        });
    }

    // A wood wall blueprint upstairs that MultiFloors counts as claimed, with nobody coming, as its cross-level delivery
    // leaves it, and a hauler on the ground beside wood. Released, the hauler takes the wood up; a claim its own delivery job
    // holds is kept. With the setting off, nobody ever delivers.
    private static async Task LevelsStuckClaim(
        ScenarioContext context,
        bool on
    )
    {
        var deliver = DefDatabase<WorkGiverDef>.GetNamed("MF_DeliverResourcesAcrossLevel");
        var hauler = context.Worker(-6, 0, deliver.workType);
        context.Place(ThingDefOf.WoodLog, -8, 0, stackCount: 75);
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var cell = context.Cell(10, 10);
        var blueprint = GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, cell, level.Map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
        Ordo.Tweaks.MultiFloorsStuckClaims.Claim(blueprint);

        int Delivered() => level.Map.thingGrid.ThingsListAtFast(cell).OfType<Frame>().Sum(frame => frame.resourceContainer.TotalStackCount);

        await WithSetting("ClaimsAcrossLevels", "false", async () =>
        {
            // Long enough for the hauler to have gone, had the claim not stopped it. Off, so the hourly check can't release
            // it in the meantime.
            await context.Step(600);
            context.Check($"the claim stopped the delivery: {Stage.Describe(hauler)} on {hauler.MapHeld}, {Delivered()} delivered", Delivered() == 0);
            if (!on)
            {
                // The component's own hourly check, which does nothing with the setting off
                await context.Step(Ordo.Tweaks.MultiFloorsStuckClaims.IntervalTicks + 100);
                context.Check($"with the setting off, it's still claimed: {Delivered()} delivered", Ordo.Tweaks.MultiFloorsStuckClaims.IsClaimed(blueprint) && Delivered() == 0);
            }
        });
        if (!on)
        {
            return;
        }

        await WithSetting("ClaimsAcrossLevels", "true", async () =>
        {
            var released = Ordo.Tweaks.MultiFloorsStuckClaims.Release();
            context.Check($"the blueprint's claim was released: {string.Join(", ", released)}", released.Contains(blueprint) && !Ordo.Tweaks.MultiFloorsStuckClaims.IsClaimed(blueprint));

            await context.Until($"the hauler takes the cross-level delivery: {Stage.Describe(hauler)}", () => hauler.CurJobDef?.defName == "MF_DeliverResourcesAcrossLevel", 2000);
            var duringJob = Ordo.Tweaks.MultiFloorsStuckClaims.Release();
            context.Check($"the claim its delivery holds is kept: released {string.Join(", ", duringJob)}",
                !duringJob.Contains(blueprint) && Ordo.Tweaks.MultiFloorsStuckClaims.IsClaimed(blueprint));

            await context.Until($"the wood reaches the frame upstairs: {Stage.Describe(hauler)} on {hauler.MapHeld}, {Delivered()} delivered", () => Delivered() > 0, 3000);
        });
    }
}
