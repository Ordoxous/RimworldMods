using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld.Planet;
using Verse;

namespace MechUi;

public class WorldComponent_UpgradeDatabase : WorldComponent
{
    private List<UpgradePolicy> _policies = [];

    /// <inheritdoc />
    public WorldComponent_UpgradeDatabase(World world) : base(world) {
    }

    public List<UpgradePolicy> GetPolicies() => _policies;

    public UpgradePolicy TryCreate()
    {
        var id = _policies.Select(o => o.id).MaxByWithFallback(o => o) + 1;
        var policy = new UpgradePolicy(id, $"Upgrade Policy {id}");
        _policies.Add(policy);
        return policy;
    }

    public UpgradePolicy DefaultPolicy()
    {
        var policies = GetPolicies();
        return policies.FirstOrDefault() ?? TryCreate();
    }

    public AcceptanceReport TryDelete(UpgradePolicy policy)
    {
        throw new NotImplementedException();
        // foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravelingTransportPods_Alive)
        // {
        //     if (pawn.outfits != null && pawn.outfits.CurrentApparelPolicy == apparelPolicy)
        //         return new AcceptanceReport((string) "OutfitInUse".Translate((NamedArgument) (Thing) pawn));
        // }
        // foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
        // {
        //     if (pawn.outfits != null && pawn.outfits.CurrentApparelPolicy == apparelPolicy)
        //         pawn.outfits.CurrentApparelPolicy = (ApparelPolicy) null;
        // }
        // this.outfits.Remove(apparelPolicy);
        // return AcceptanceReport.WasAccepted;
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref _policies, "policies", LookMode.Deep);
    }
}