using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Bill counts: the cache for drawing them, and Better Workbench Management's extra products across MultiFloors levels
public static partial class TweaksScenarios
{
    // A "Do until you have 8" simple meal bill with 2 meals stockpiled. Counted again at once, outside a tick, after 3 more
    // arrive, the cache still answers 2; inside a tick, after a change to the bill's options, or with the setting off,
    // the count is fresh.
    private static async Task BillsCountCache(
        ScenarioContext context
    )
    {
        var meal = ThingDefOf.MealSimple;
        var stove = (Building_WorkTable)context.Build(ThingDef.Named("FueledStove"), -6, 0);
        var bill = (Bill_Production)DefDatabase<RecipeDef>.GetNamed("CookMealSimple").MakeNewBill();
        bill.repeatMode = BillRepeatModeDefOf.TargetCount;
        bill.targetCount = 8;
        stove.BillStack.AddBill(bill);
        LevelStock.Stockpile(context.Map, context.Cell(-10, -10));
        context.Place(meal, -10, -10, stackCount: 2);
        context.Map.resourceCounter.UpdateResourceCounts();
        Ordo.Tweaks.BillCountCache.Clear();

        var counter = bill.recipe.WorkerCounter;
        var first = counter.CountProducts(bill);
        context.Check($"the bill counts the 2 meals: {first}", first == 2);

        context.Place(meal, -10, -9, stackCount: 3);
        context.Map.resourceCounter.UpdateResourceCounts();
        var cached = counter.CountProducts(bill);
        context.Check($"asked again at once, outside a tick, the cache answers 2: {cached}", cached == 2);

        Ordo.Tweaks.BillCountCache.Prefix_DoSingleTick();
        try
        {
            var inTick = counter.CountProducts(bill);
            context.Check($"inside a tick the count is fresh: {inTick}", inTick == 5);
        }
        finally
        {
            Ordo.Tweaks.BillCountCache.Finalizer_DoSingleTick();
        }

        context.Check($"outside the tick, the cache still answers its 2: {counter.CountProducts(bill)}", counter.CountProducts(bill) == 2);
        // Tainted, as it keeps meals on the resource counter: hit points, quality or equipped count stack by stack
        bill.includeTainted = !bill.includeTainted;
        var changed = counter.CountProducts(bill);
        context.Check($"after the bill's options change, the count is fresh: {changed}", changed == 5);
        bill.includeTainted = !bill.includeTainted;

        context.Place(meal, -9, -10, stackCount: 1);
        context.Map.resourceCounter.UpdateResourceCounts();
        await WithSetting("CacheBillCounts", "false", () =>
        {
            var off = counter.CountProducts(bill);
            context.Check($"with the setting off, the count is fresh: {off}", off == 6);
            return Task.CompletedTask;
        });

        await Task.Delay(300);
        var expired = counter.CountProducts(bill);
        context.Check($"after a quarter of a second, the count is fresh: {expired}", expired == 6);

        var patches = Harmony.GetPatchInfo(AccessTools.DeclaredMethod(typeof(RecipeWorkerCounter), nameof(RecipeWorkerCounter.CountProducts)));
        context.Check("CountProducts has the cache's prefix, last postfix and finalizer",
            patches != null && patches.Prefixes.Any(p => p.owner == ModId) && patches.Finalizers.Any(p => p.owner == ModId)
            && patches.Postfixes.Where(p => p.owner == ModId).Any(p => p.PatchMethod.DeclaringType?.Name == "BillCountCache"));
        Ordo.Tweaks.BillCountCache.Clear();
    }

    // A simple meal bill that also counts fine meals through Better Workbench Management's "count additional items",
    // with 2 simple and 3 fine meals on the ground and 7 simple and 4 fine upstairs. It counts 16, each level once; before
    // BWM's count read the level, the level's count added the ground's 3 fine meals instead of its own 4. With
    // BillsAcrossLevels off it's the ground's 5.
    private static async Task LevelsBillsBetterWorkbenches(
        ScenarioContext context
    )
    {
        using var level = new UpperLevel(context);
        if (level.Map == null)
        {
            return;
        }

        var simple = ThingDefOf.MealSimple;
        var fine = ThingDefOf.MealFine;
        var stove = (Building_WorkTable)context.Build(ThingDef.Named("FueledStove"), -6, 0);
        var bill = (Bill_Production)DefDatabase<RecipeDef>.GetNamed("CookMealSimple").MakeNewBill();
        bill.repeatMode = BillRepeatModeDefOf.TargetCount;
        bill.targetCount = 30;
        stove.BillStack.AddBill(bill);

        // BWM's types stay out of BridgeTools' signatures: reached by reflection
        var main = AccessTools.TypeByName("ImprovedWorkbenches.Main");
        var instance = AccessTools.Property(main, "Instance").GetValue(null);
        var storage = AccessTools.Method(main, "GetExtendedBillDataStorage").Invoke(instance, null);
        var data = AccessTools.Method(storage.GetType(), "GetOrCreateExtendedDataFor").Invoke(storage, [bill]);
        var extra = new ThingFilter();
        extra.SetAllow(fine, true);
        AccessTools.Field(data.GetType(), "ProductAdditionalFilter").SetValue(data, extra);

        try
        {
            LevelStock.Stockpile(context.Map, context.Cell(-10, -10));
            context.Place(simple, -10, -10, stackCount: 2);
            context.Place(fine, -10, -9, stackCount: 3);
            LevelStock.Stockpile(level.Map, context.Cell(10, 10));
            foreach (var (def, count, z) in new[] { (simple, 7, 10), (fine, 4, 9) })
            {
                var thing = ThingMaker.MakeThing(def);
                thing.stackCount = count;
                GenSpawn.Spawn(thing, context.Cell(10, z), level.Map);
            }

            context.Map.resourceCounter.UpdateResourceCounts();
            level.Map.resourceCounter.UpdateResourceCounts();
            Ordo.Tweaks.BillCountCache.Clear();

            var counter = bill.recipe.WorkerCounter;
            var counted = counter.CountProducts(bill);
            context.Check($"both meals on both levels count once each: {counted} (2 + 3 + 7 + 4)", counted == 16);
            var again = counter.CountProducts(bill);
            context.Check($"answered from the cache, BWM's postfix doesn't add again: {again}", again == 16);

            var helper = AccessTools.DeclaredMethod(AccessTools.TypeByName("ImprovedWorkbenches.RecipeWorkerCounter_CountProducts_Detour"), "CountProducts");
            var billMap = AccessTools.PropertyGetter(typeof(Bill), nameof(Bill.Map));
            var direct = PatchProcessor.GetCurrentInstructions(helper).Count(instruction => instruction.operand as MethodInfo == billMap);
            context.Check($"BWM's count reads the bill's map through Tweaks ({direct} direct reads left)", direct == 0);

            await WithSetting("BillsAcrossLevels", "false", () =>
            {
                var off = counter.CountProducts(bill);
                context.Check($"with BillsAcrossLevels off, the ground's 2 + 3: {off}", off == 5);
                return Task.CompletedTask;
            });
        }
        finally
        {
            AccessTools.Method(storage.GetType(), "DeleteExtendedDataFor")?.Invoke(storage, [bill]);
            Ordo.Tweaks.BillCountCache.Clear();
        }
    }
}
