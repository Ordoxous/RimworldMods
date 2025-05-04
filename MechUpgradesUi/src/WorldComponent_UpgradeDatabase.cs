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

    public AcceptanceReport TryDelete(UpgradePolicy policy)
    {
        foreach (var pawn in PawnsFinder.All_AliveOrDead)
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
                    pawn.outfits.CurrentApparelPolicy = null;
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