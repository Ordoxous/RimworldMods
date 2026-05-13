using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MU;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

public class Dialog_ManageUpgradePolicies(
    [CanBeNull] UpgradePolicy currentPolicy,
    [CanBeNull] CompUpgradableMechanoid upgradableMechanoid)
    : Dialog_ManagePolicies<UpgradePolicy>(currentPolicy)
{
    private static readonly ConcurrentDictionary<ThingDef, ThingFilter> MechFilterByDef = new();

    private static ThingFilter GlobalFilter
    {
        get
        {
            if (field == null)
            {
                field = new ThingFilter();
                field.SetAllow(ThingCategoryDef.Named("MU_Upgrades"), true);
            }

            return field;
        }
    }

    private static readonly Lazy<List<ThingDef>> upgradableMechRaces = new(() =>
        DefDatabase<ThingDef>.AllDefs
            .Where(t => t.race is { IsMechanoid: true } && t.comps.Any(c => c is CompProperties_UpgradableMechanoid))
            .OrderBy(t => t.label)
            .ToList()
    );

    private static ThingFilter MechFilterForDef(ThingDef mechDef)
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

    [CanBeNull] private ThingDef _mechDef = upgradableMechanoid?.Mech.def;

    public override string TitleKey => "MU_Upgrades_Ui_Policy_Title";

    public override string TipKey => "MU_Upgrades_Ui_Policy_Tip";

    public override Vector2 InitialSize => new(700f, 700f);

    public override void PreOpen()
    {
        base.PreOpen();
        _thingFilterState.quickSearch.Reset();
    }

    public override UpgradePolicy CreateNewPolicy() =>
        Find.World.GetComponent<WorldComponent_UpgradeDatabase>().TryCreate();

    public override UpgradePolicy GetDefaultPolicy() =>
        Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy();

    /// <inheritdoc />
    public override void SetDefaultPolicy(
        UpgradePolicy policy
    )
    {
        throw new NotImplementedException();
    }

    public override AcceptanceReport TryDeletePolicy(UpgradePolicy policy) =>
        Find.World.GetComponent<WorldComponent_UpgradeDatabase>().TryDelete(policy);

    public override List<UpgradePolicy> GetPolicies() =>
        Find.World.GetComponent<WorldComponent_UpgradeDatabase>().GetPolicies();

    private static IEnumerable<Widgets.DropdownMenuElement<ThingDef>> MechRaceSelectorMenuGenerator(Dialog_ManageUpgradePolicies dialog)
    {
        foreach (var x in upgradableMechRaces.Value)
            yield return new Widgets.DropdownMenuElement<ThingDef>
            {
                payload = x,
                option = new FloatMenuOption(x.LabelCap, () => dialog._mechDef = x, x)
            };

        yield return new Widgets.DropdownMenuElement<ThingDef>
        {
            payload = null,
            option = new FloatMenuOption("All", () => dialog._mechDef = null)
        };
    }

    public override void DoWindowContents(Rect inRect)
    {
        base.DoWindowContents(inRect);
        
        var mechSelectorRect = inRect with { 
            xMin =  inRect.xMin + ((inRect.xMax - inRect.xMin) / 2),
            height = 32f
        };
        
        Widgets.Dropdown(
            rect: mechSelectorRect, 
            target: this,
            getPayload: dialog => dialog._mechDef,
            menuGenerator: MechRaceSelectorMenuGenerator,
            buttonLabel: $"Mech Type: {_mechDef?.LabelCap ?? "All"}"
        );

        TooltipHandler.TipRegionByKey(mechSelectorRect, "MU_Upgrades_Ui_Policy_Mech_Selector");
    }

    public override void DoContentsRect(Rect rect)
    {
        ThingFilterUI.DoThingFilterConfigWindow(
            rect,
            _thingFilterState,
            SelectedPolicy.Filter,
            _mechDef != null ? MechFilterForDef(_mechDef) : GlobalFilter,
            16
        );
    }
}