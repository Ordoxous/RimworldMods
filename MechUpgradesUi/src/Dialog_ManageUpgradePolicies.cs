using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using MU;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>
/// Vanilla's policy dialog with the upgrade filter in the middle and, on the right, a preview of what the selected policy
/// gives a mech of the chosen type.
/// </summary>
public class Dialog_ManageUpgradePolicies(
    [CanBeNull] UpgradePolicy currentPolicy,
    [CanBeNull] CompUpgradableMechanoid upgradableMechanoid)
    : Dialog_ManagePolicies<UpgradePolicy>(currentPolicy ?? Database.DefaultPolicy())
{
    private const float TopRowHeight = 32f;

    private const float PreviewWidth = 330f;

    private const float Gap = 10f;

    private const float RowHeight = 28f;

    private const float SkippedRowHeight = 44f;

    private static readonly Dictionary<ThingDef, ThingFilter> MechFilterByDef = new();

    private static WorldComponent_UpgradeDatabase Database => Find.World.GetComponent<WorldComponent_UpgradeDatabase>();

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

    /// <summary>
    /// MU's special filters on upgrades (its tier filters). A policy is checked by def, which ignores special filters, so
    /// they'd be switches that do nothing.
    /// </summary>
    public static List<SpecialThingFilterDef> HiddenSpecialFilters =>
        field ??= DefDatabase<SpecialThingFilterDef>.AllDefs
            .Where(f => f.parentCategory != null
                        && ThingCategoryDef.Named("MU_Upgrades").ThisAndChildCategoryDefs.Contains(f.parentCategory))
            .ToList();

    private static readonly Lazy<List<ThingDef>> upgradableMechRaces = new(() =>
        DefDatabase<ThingDef>.AllDefs
            .Where(t => t.race is { IsMechanoid: true } && t.comps.Any(c => c is CompProperties_UpgradableMechanoid))
            .OrderBy(t => t.label)
            .ToList()
    );

    private static ThingFilter MechFilterForDef(ThingDef mechDef)
    {
        if (MechFilterByDef.TryGetValue(mechDef, out var filter))
            return filter;

        var upgradeDefs = MechUpgradeUtility
            .UpgradesDatabase
            .Where(u => u.CanAdd(mechDef))
            .Select(u => u.linkedThingDef)
            .Where(t => t != null);

        filter = new ThingFilter();

        foreach (var def in upgradeDefs)
        {
            filter.SetAllow(def, true);
        }

        MechFilterByDef[mechDef] = filter;
        return filter;
    }

    private readonly ThingFilterUI.UIState _thingFilterState = new();

    [CanBeNull] private readonly Pawn _openedFor = upgradableMechanoid?.Mech;

    [CanBeNull] private ThingDef _mechDef = upgradableMechanoid?.Mech.def;

    private Vector2 _previewScroll;

    private float _previewHeight;

    public override string TitleKey => "MU_Upgrades_Ui_Policy_Title";

    public override string TipKey => "MU_Upgrades_Ui_Policy_Tip";

    public override Vector2 InitialSize => new(1000f, 700f);

    public override void PreOpen()
    {
        base.PreOpen();
        _thingFilterState.quickSearch.Reset();
    }

    public override UpgradePolicy CreateNewPolicy() => Database.TryCreate();

    public override UpgradePolicy GetDefaultPolicy() => Database.DefaultPolicy();

    /// <inheritdoc />
    public override void SetDefaultPolicy(
        UpgradePolicy policy
    ) => Database.SetDefaultPolicy(policy);

    public override AcceptanceReport TryDeletePolicy(UpgradePolicy policy) => Database.TryDelete(policy);

    public override List<UpgradePolicy> GetPolicies() => Database.GetPolicies();

    private static IEnumerable<Widgets.DropdownMenuElement<ThingDef>> MechRaceSelectorMenuGenerator(Dialog_ManageUpgradePolicies dialog)
    {
        yield return new Widgets.DropdownMenuElement<ThingDef>
        {
            payload = null,
            option = new FloatMenuOption("MU_Upgrades_Ui_Policy_Mech_All".Translate().CapitalizeFirst(), () => dialog._mechDef = null)
        };

        foreach (var x in upgradableMechRaces.Value)
            yield return new Widgets.DropdownMenuElement<ThingDef>
            {
                payload = x,
                option = new FloatMenuOption(x.LabelCap, () => dialog._mechDef = x, x)
            };
    }

    public override void DoContentsRect(Rect rect)
    {
        var filterColumn = rect with { width = rect.width - PreviewWidth - Gap };
        var previewColumn = rect with { xMin = filterColumn.xMax + Gap };

        var topRow = filterColumn with { height = TopRowHeight };
        DoTopRow(topRow);

        ThingFilterUI.DoThingFilterConfigWindow(
            filterColumn with { yMin = topRow.yMax + 4f },
            _thingFilterState,
            SelectedPolicy.Filter,
            _mechDef != null ? MechFilterForDef(_mechDef) : GlobalFilter,
            16,
            forceHiddenFilters: HiddenSpecialFilters,
            forceHideHitPointsConfig: true,
            forceHideQualityConfig: true
        );

        DoPreview(previewColumn);
    }

    private void DoTopRow(Rect rect)
    {
        const float loadWidth = 170f;
        var selectorRect = rect with { width = rect.width - loadWidth - Gap };
        var loadRect = rect with { xMin = selectorRect.xMax + Gap };

        Widgets.Dropdown(
            rect: selectorRect,
            target: this,
            getPayload: dialog => dialog._mechDef,
            menuGenerator: MechRaceSelectorMenuGenerator,
            buttonLabel: "MU_Upgrades_Ui_Policy_ShowFor".Translate(_mechDef?.LabelCap ?? "MU_Upgrades_Ui_Policy_Mech_All".Translate())
        );
        TooltipHandler.TipRegionByKey(selectorRect, "MU_Upgrades_Ui_Policy_Mech_Selector");

        if (Widgets.ButtonText(loadRect, "MU_Upgrades_Ui_Policy_LoadSet".Translate()))
        {
            var policy = SelectedPolicy;
            Find.WindowStack.Add(new Dialog_UpgradeSetList_Load(set => Find.WindowStack.Add(
                Dialog_MessageBox.CreateConfirmation(
                    "MU_Upgrades_Ui_Policy_LoadSet_Confirm".Translate(policy.label, set.name, set.upgrades.Count),
                    () => policy.LoadFrom(set)
                )
            )));
        }

        TooltipHandler.TipRegionByKey(loadRect, "MU_Upgrades_Ui_Policy_LoadSet_Tip");
    }

    /// <summary>The mech whose upgrade points the preview uses: the one the dialog was opened for, or any of that type.</summary>
    [CanBeNull]
    private Pawn PreviewMech()
    {
        if (_mechDef == null)
            return null;

        if (_openedFor != null && _openedFor.def == _mechDef && !_openedFor.Dead)
            return _openedFor;

        return Find.Maps
            .SelectMany(m => m.mapPawns.SpawnedColonyMechs)
            .FirstOrDefault(p => p.def == _mechDef);
    }

    /// <summary>What the policy gives a mech of the chosen type that has no upgrades yet, with everything in stock.</summary>
    public static List<MechUpgrade> Preview(
        UpgradePolicy policy,
        ThingDef mechDef,
        int? maxPoints,
        List<SkippedUpgrade> skipped
    )
    {
        var candidates = policy.AllowedUpgradeDefs
            .Where(d => d.CanAdd(mechDef).Accepted)
            .Select(UpgradePlanner.Sample);
        return UpgradePlanner.PickAdditions(mechDef, maxPoints, [], candidates, skipped);
    }

    private void DoPreview(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(8f);

        using (new TextBlock(GameFont.Medium))
        {
            Widgets.Label(inner with { height = 32f }, "MU_Upgrades_Ui_Preview_Title".Translate());
        }

        inner.yMin += 34f;

        if (_mechDef == null)
        {
            using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, Color.gray))
            {
                Widgets.Label(inner, "MU_Upgrades_Ui_Preview_ChooseMech".Translate());
            }

            return;
        }

        var mech = PreviewMech();
        int? maxPoints = mech != null ? MechUpgradeUtility.MaxUpgradePoints(mech) : null;
        var skipped = new List<SkippedUpgrade>();
        var gets = Preview(SelectedPolicy, _mechDef, maxPoints, skipped);
        var used = MechUpgradeUtility.CurrentPoints(gets);

        string summary = maxPoints.HasValue
            ? "MU_Upgrades_Ui_Preview_Points".Translate(_mechDef.LabelCap, used, maxPoints.Value)
            : "MU_Upgrades_Ui_Preview_PointsUnknown".Translate(_mechDef.LabelCap, used);
        var summaryHeight = Text.CalcHeight(summary, inner.width);
        Widgets.Label(inner with { height = summaryHeight }, summary);
        inner.yMin += summaryHeight + 6f;

        const string footerKey = "MU_Upgrades_Ui_Preview_Footer";
        float footerHeight;
        using (new TextBlock(GameFont.Tiny))
        {
            footerHeight = Text.CalcHeight(footerKey.Translate(), inner.width);
        }

        var listRect = inner with { height = inner.height - footerHeight - 6f };
        var viewRect = new Rect(0f, 0f, listRect.width - 16f, Mathf.Max(_previewHeight, listRect.height));
        Widgets.BeginScrollView(listRect, ref _previewScroll, viewRect);
        var y = 0f;

        y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_Gets".Translate());
        if (gets.Count == 0)
            y = DoNothing(viewRect, y);
        foreach (var upgrade in gets)
            y = DoUpgradeRow(viewRect, y, upgrade, null);

        if (skipped.Count > 0)
        {
            y += 8f;
            y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_Skipped".Translate());
            foreach (var skip in skipped)
                y = DoUpgradeRow(viewRect, y, skip.Upgrade, skip.Reason);
        }

        if (Event.current.type == EventType.Layout)
            _previewHeight = y;
        Widgets.EndScrollView();

        using (new TextBlock(GameFont.Tiny, TextAnchor.LowerLeft, Color.gray))
        {
            Widgets.Label(inner with { yMin = inner.yMax - footerHeight }, footerKey.Translate());
        }
    }

    private static float DoHeading(Rect viewRect, float y, string label)
    {
        Widgets.Label(new Rect(0f, y, viewRect.width, 24f), label);
        Widgets.DrawLineHorizontal(0f, y + 23f, viewRect.width, Widgets.SeparatorLineColor);
        return y + 26f;
    }

    private static float DoNothing(Rect viewRect, float y)
    {
        using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, Color.gray))
        {
            Widgets.Label(new Rect(4f, y, viewRect.width, RowHeight), "MU_Upgrades_Ui_Preview_Nothing".Translate());
        }

        return y + RowHeight;
    }

    // A skipped upgrade is greyed, with the reason under its name
    private static float DoUpgradeRow(Rect viewRect, float y, MechUpgrade upgrade, [CanBeNull] string skipReason)
    {
        var height = skipReason == null ? RowHeight : SkippedRowHeight;
        var row = new Rect(0f, y, viewRect.width, height);
        if (Mouse.IsOver(row))
            Widgets.DrawHighlight(row);

        var color = skipReason == null ? Color.white : Color.gray;
        var def = upgrade.def;

        var iconRect = new Rect(row.x + 2f, row.y + 2f, 24f, 24f);
        // The item's icon: MU's own uiIcon field is typed against a Unity assembly the build can't resolve
        var icon = def.linkedThingDef?.uiIcon;
        if (icon != null)
        {
            GUI.color = color;
            Widgets.DrawTextureFitted(iconRect, icon, 1f);
            GUI.color = Color.white;
        }

        var infoRect = new Rect(row.xMax - 24f, row.y + 2f, 24f, 24f);
        Widgets.InfoCardButton(infoRect, def);

        var pointsRect = new Rect(infoRect.x - 34f, row.y, 30f, RowHeight);
        using (new TextBlock(GameFont.Small, TextAnchor.MiddleRight, color))
        {
            Widgets.Label(pointsRect, def.upgradePoints.ToString());
        }

        var labelRect = new Rect(iconRect.xMax + 6f, row.y, pointsRect.x - iconRect.xMax - 8f, RowHeight);
        using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, color))
        {
            Widgets.LabelEllipses(labelRect, def.LabelCap);
        }

        if (skipReason != null)
        {
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, Color.gray))
            {
                Widgets.LabelEllipses(new Rect(labelRect.x, row.y + RowHeight - 6f, row.width - labelRect.x, 20f), skipReason);
            }
        }

        if (Mouse.IsOver(row) && !Mouse.IsOver(infoRect))
        {
            var tip = MechUpgradeUtility.DescWithStats(upgrade);
            if (skipReason != null)
                tip = $"{skipReason.Colorize(ColoredText.WarningColor)}\n\n{tip}";
            TooltipHandler.TipRegion(row, tip);
        }

        return y + height + 2f;
    }
}
