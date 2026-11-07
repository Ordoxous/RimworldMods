using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.BioFuelFertilizer;

public class BioFuelFertilizerSettings : ModSettings
{
    private const float DefaultFertilityMultiplier = 1.5f;
    private const float DefaultFuelCapacityPerCell = 2.5f;
    private const float DefaultFuelConsumptionRatePerCell = 0.1f; // 1 per 10 days

    public float FertilityMultiplier = DefaultFertilityMultiplier;
    public float FuelCapacityPerCell = DefaultFuelCapacityPerCell;
    public float FuelConsumptionRatePerCell = DefaultFuelConsumptionRatePerCell;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref FertilityMultiplier, nameof(FertilityMultiplier));
        Scribe_Values.Look(ref FuelCapacityPerCell, nameof(FuelCapacityPerCell));
        Scribe_Values.Look(ref FuelConsumptionRatePerCell, nameof(FuelConsumptionRatePerCell));
        base.ExposeData();
    }
}

public class BioFuelFertilizerMod : Mod
{
    public static BioFuelFertilizerSettings Settings { get; private set; }

    public BioFuelFertilizerMod(
        ModContentPack content
    ) : base(content)
    {
        Settings = GetSettings<BioFuelFertilizerSettings>();

        var harmony = new Harmony(typeof(BioFuelFertilizerMod).Namespace);

        harmony.Patch(
            AccessTools.Method(typeof(FertilityGrid), nameof(FertilityGrid.CalculateFertilityAt)),
            postfix: new HarmonyMethod(Postfix_FertilityGrid_CalculateFertilityAt)
        );

        harmony.Patch(
            AccessTools.Method(typeof(Building_PlantGrower), nameof(Building_PlantGrower.TickRare)),
            postfix: new HarmonyMethod(Postfix_Building_PlantGrower_TickRare)
        );

        if (DubsBadHygieneExtension.IsAvailable())
        {
            DubsBadHygieneExtension.PatchDubsBadHygiene(harmony);
        }
    }

    public override void DoSettingsWindowContents(
        Rect inRect
    )
    {
        var listingStandard = new Listing_Standard();

        listingStandard.Begin(inRect);

        listingStandard.Label($"Fertility Multiplier: {Settings.FertilityMultiplier}");
        Settings.FertilityMultiplier = Widgets.HorizontalSlider(listingStandard.GetRect(22f), Settings.FertilityMultiplier, 1f, 5f, roundTo: 0.1f);
        listingStandard.Gap(listingStandard.verticalSpacing);

        listingStandard.Label($"Fuel Capacity Per Cell: {Settings.FuelCapacityPerCell}");
        Settings.FuelCapacityPerCell = Widgets.HorizontalSlider(listingStandard.GetRect(22f), Settings.FuelCapacityPerCell, 1f, 50f, roundTo: 0.1f);
        listingStandard.Gap(listingStandard.verticalSpacing);

        listingStandard.Label($"Fuel Consumption Rate (Daily, Per Cell): {Settings.FuelConsumptionRatePerCell}");
        Settings.FuelConsumptionRatePerCell = Widgets.HorizontalSlider(listingStandard.GetRect(22f), Settings.FuelConsumptionRatePerCell, 0.01f, 1f, roundTo: 0.01f);
        listingStandard.Gap(listingStandard.verticalSpacing);

        listingStandard.End();

        base.DoSettingsWindowContents(inRect);
    }

    public override string SettingsCategory()
    {
        return "BioFuelFertilizer";
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        
        DefDatabase<ThingDef>.AllDefsListForReading
            .ForEach(d =>
                {
                    var compProperties = d.GetCompProperties<CompProperties_RefuelableRareTicking>();
                    if (compProperties == null)
                        return;
                    
                    compProperties.ApplySettings(d);
                }
            );
    }

    private static void Postfix_FertilityGrid_CalculateFertilityAt(
        IntVec3 loc,
        ref float __result,
        FertilityGrid __instance
    )
    {
        if (loc.GetEdifice(__instance.map) is Building_PlantGrower building
            && building.TryGetComp<CompRefuelable>(out var refuelable)
            && refuelable.HasFuel)
        {
            __result *= Settings.FertilityMultiplier;
        }
    }

    private static void Postfix_Building_PlantGrower_TickRare(
        Building_PlantGrower __instance
    )
    {
        if (__instance.compPower is { PowerOn: false })
            // Consume fuel only when power is on for a powerable
            return;
        
        if (__instance.TryGetComp<CompRefuelable>(out var refuelable))
        {
            var fuelToConsume = ((CompProperties_Refuelable) refuelable.props).fuelConsumptionRate
                                / 60000f // Ticks per second
                                * 250f; // Ticks per rare tick;

            refuelable.ConsumeFuel(fuelToConsume);
        }
    }
}