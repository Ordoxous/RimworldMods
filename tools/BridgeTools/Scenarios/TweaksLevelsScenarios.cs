using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;

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
            // stack there as one, so it's one stack on each level
            bill.hpRange = new FloatRange(0.5f, 1f);
            var byThing = counter.CountProducts(bill);
            context.Check($"counting thing by thing, both levels' stacks count: {byThing}", byThing == 2);
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
                context.Check($"with the setting off, thing by thing counts the ground's stack: {counter.CountProducts(bill)}", counter.CountProducts(bill) == 1);
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
}
