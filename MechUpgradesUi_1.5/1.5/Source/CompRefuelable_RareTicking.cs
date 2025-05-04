using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace MechUi;

public class CompProperties_RefuelableRareTicking : CompProperties_Refuelable
{

    public CompProperties_RefuelableRareTicking() => compClass = typeof(CompRefuelable);

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        return base.ConfigErrors(parentDef)
            .Where(configError => !configError.Contains("tickertype"));
    }
}
