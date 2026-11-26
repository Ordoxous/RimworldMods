using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Bill counts: the cache for drawing them
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
        Stockpile(context.Map, context.Cell(-10, -10));
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

    // A 3x3 stockpile, so stored meals count
    private static Zone_Stockpile Stockpile(
        Map map,
        IntVec3 center
    )
    {
        var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
        map.zoneManager.RegisterZone(zone);
        foreach (var cell in CellRect.CenteredOn(center, 1))
        {
            zone.AddCell(cell);
        }

        return zone;
    }
}
