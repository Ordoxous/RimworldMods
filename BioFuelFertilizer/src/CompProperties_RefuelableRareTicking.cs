using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.BioFuelFertilizer;

public class CompProperties_RefuelableRareTicking : CompProperties_Refuelable
{

    public CompProperties_RefuelableRareTicking()
    {
        compClass = typeof(CompRefuelable);
        showAllowAutoRefuelToggle = true;
    }

    public void ApplySettings(ThingDef parent)
    {
        fuelConsumptionRate = BioFuelFertilizerMod.Settings.FuelConsumptionRatePerCell * parent.size.Area;
        fuelCapacity = BioFuelFertilizerMod.Settings.FuelCapacityPerCell * parent.size.Area;
    }

    public override void PostLoadSpecial(ThingDef parent)
    {
        base.PostLoadSpecial(parent);
        ApplySettings(parent);
    }

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        if (DubsBadHygieneExtension.IsAvailable())
        {
            foreach (var dubsBadHygieneConfigError in DubsBadHygieneExtension.DubsBadHygieneConfigErrors())
            {
                yield return dubsBadHygieneConfigError;
            }
        }

        var baseErrors = base.ConfigErrors(parentDef);
        var filteredErrors = baseErrors.Where(configError => !configError.Contains("parent tickertype"));

        foreach (var baseError in filteredErrors)
            yield return baseError;
    }
}