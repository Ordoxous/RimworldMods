using System.Linq;
using System.Threading;
using Ordo.Saddlebound;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

public sealed class SaddleboundTools
{
    [Tool("ordo/stage_saddlebound", Description = "Give the player's primary ideoligion Riding: Essential and the horse and dromedary as preferred mounts, for looking at the Preferred mounts section of the ideoligion tab and editor. Reports the ideoligion's preferred mounts. Don't save afterwards.")]
    public static object StageSaddlebound(
        IRimBridgeContext bridge,
        CancellationToken cancellationToken
    )
    {
        var ideo = Faction.OfPlayer?.ideos?.PrimaryIdeo;
        if (ideo == null)
        {
            return new { success = false, message = "The player has no ideoligion" };
        }

        foreach (var precept in ideo.PreceptsListForReading
                     .Where(p => p.def.issue == SaddleboundDefOf.Ordo_Riding_Essential.issue || p is Precept_PreferredMount)
                     .ToList())
        {
            ideo.RemovePrecept(precept);
        }

        ideo.AddPrecept(PreceptMaker.MakePrecept(SaddleboundDefOf.Ordo_Riding_Essential), init: true);
        foreach (var race in new[] { "Horse", "Dromedary" })
        {
            var precept = (Precept_PreferredMount)PreceptMaker.MakePrecept(SaddleboundDefOf.Ordo_PreferredMount);
            ideo.AddPrecept(precept, init: true);
            precept.ThingDef = DefDatabase<ThingDef>.GetNamed(race);
        }

        return new
        {
            success = true,
            ideo = ideo.name,
            preferredMounts = PreferredMounts.For(ideo).Select(race => race.defName).ToList(),
        };
    }
}
