using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Ordo.MechUpgradesUi;

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

    public void SetDefaultPolicy(UpgradePolicy policy)
    {
        // The default policy is the first in the list
        if (_policies.Remove(policy))
            _policies.Insert(0, policy);
    }

    public AcceptanceReport TryDelete(UpgradePolicy policy)
    {
        // Only living mechs block deletion. Dead ones are cleared below, as vanilla does for apparel policies
        foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
        {
            if (pawn.IsColonyMech && pawn.TryGetComp<CompUpgradableMechWithPolicy>(out var comp))
            {
                if (comp.CurrentUpgradePolicy == policy)
                    return new AcceptanceReport("OutfitInUse".Translate((NamedArgument) (Thing) pawn));
            }
        }

        foreach (var pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead)
        {
            if (pawn.IsColonyMech && pawn.TryGetComp<CompUpgradableMechWithPolicy>(out var comp))
            {
                if (comp.CurrentUpgradePolicy == policy)
                    comp.CurrentUpgradePolicy = null;
            }
        }

        _policies.Remove(policy);
        return AcceptanceReport.WasAccepted;
    }

    public override void ExposeData()
    {
        Scribe_Collections.Look(ref _policies, "policies", LookMode.Deep);
    }
}