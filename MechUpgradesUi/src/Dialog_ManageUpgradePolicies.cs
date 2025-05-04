using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MU;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

public class Dialog_ManageUpgradePolicies(UpgradePolicy policy, CompUpgradableMechanoid upgradableMechanoid) : Dialog_ManagePolicies<UpgradePolicy>(policy)
{
    // TODO: Add a mech type filter
    
    private static readonly ConcurrentDictionary<ThingDef, ThingFilter> MechFilterByDef = new();

    private static ThingFilter _globalFilter;

    private static ThingFilter GlobalFilter
    {
        get
        {
            if (_globalFilter == null)
            {
                _globalFilter = new ThingFilter();
                _globalFilter.SetAllow(ThingCategoryDef.Named("MU_Upgrades"), true);
            }
            return _globalFilter;
        }
    }
    
    public static ThingFilter MechFilterForDef(ThingDef mechDef)
    {
        return MechFilterByDef.GetOrAdd(mechDef, key =>
        {
            var upgradeDefs = MechUpgradeUtility
                .UpgradesDatabase
                .Where(u => u.CanAdd(key))
                .Select(u => u.linkedThingDef);

            var filter = new ThingFilter();

            foreach (var def in upgradeDefs)
            {
                filter.SetAllow(def, true);
            }

            return filter;
        });
    }

    private readonly ThingFilterUI.UIState _thingFilterState = new();

    protected override string TitleKey => "ApparelPolicyTitle";

    protected override string TipKey => "ApparelPolicyTip";

    public override Vector2 InitialSize => new(700f, 700f);

    public override void PreOpen()
    {
        base.PreOpen();
        _thingFilterState.quickSearch.Reset();
    }

    protected override UpgradePolicy CreateNewPolicy() => Find.World.GetComponent<WorldComponent_UpgradeDatabase>().TryCreate();

    protected override UpgradePolicy GetDefaultPolicy() => Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy();

    /// <inheritdoc />
    protected override void SetDefaultPolicy(
        UpgradePolicy policy
    )
    {
        throw new System.NotImplementedException();
    }

    protected override AcceptanceReport TryDeletePolicy(UpgradePolicy policy) => Find.World.GetComponent<WorldComponent_UpgradeDatabase>().TryDelete(policy);

    protected override List<UpgradePolicy> GetPolicies() => Find.World.GetComponent<WorldComponent_UpgradeDatabase>().GetPolicies();

    protected override void DoContentsRect(Rect rect) => ThingFilterUI.DoThingFilterConfigWindow(
        rect,
        _thingFilterState,
        SelectedPolicy.Filter,
        upgradableMechanoid != null ? MechFilterForDef(upgradableMechanoid.Mech.def) : GlobalFilter,
        16
    );
}