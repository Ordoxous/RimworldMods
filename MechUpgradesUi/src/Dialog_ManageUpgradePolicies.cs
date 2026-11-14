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

    [CanBeNull] private readonly CompUpgradableMechWithPolicy _openedForComp = upgradableMechanoid as CompUpgradableMechWithPolicy;

    [CanBeNull] private ThingDef _mechDef = upgradableMechanoid?.Mech.def ?? DefaultMechDef(currentPolicy ?? Database.DefaultPolicy());

    /// <summary>The mech type the upgrade list and preview are for. Null for all mechs.</summary>
    [CanBeNull] public ThingDef MechDef => _mechDef;

    /// <summary>
    /// The mech type to start on when the dialog wasn't opened from a mech: the commonest type following the policy, otherwise
    /// the colony's commonest type. Null with no mechs.
    /// </summary>
    [CanBeNull]
    public static ThingDef DefaultMechDef(UpgradePolicy policy)
    {
        var mechs = CompUpgradableMechWithPolicy.PlayerMechs().ToList();
        var following = mechs.Where(c => c.EffectiveUpgradePolicy == policy).ToList();
        return CommonestFirst(following.Count > 0 ? following : mechs).FirstOrDefault()?.Key;
    }

    /// <summary>Which mechs follow a policy, by type, as one line: "Used by 3: Lifter ×2, Constructoid."</summary>
    public static string UsedBy(UpgradePolicy policy)
    {
        var mechs = CompUpgradableMechWithPolicy.PlayerMechs().ToList();
        var assigned = mechs.Where(c => c.CurrentUpgradePolicy == policy).ToList();
        string text = assigned.Count == 0
            ? "MU_Upgrades_Ui_Policy_UsedByNone".Translate()
            : "MU_Upgrades_Ui_Policy_UsedBy".Translate(
                assigned.Count,
                CommonestFirst(assigned).Select(g => g.Count() > 1 ? $"{g.Key.LabelCap} ×{g.Count()}" : g.Key.LabelCap.ToString()).ToCommaList()
            );

        if (MechUpgradesUiMod.Settings.UseDefaultPolicyForUnassigned && policy == Database.DefaultPolicy())
        {
            var unassigned = mechs.Count(c => c.CurrentUpgradePolicy == null);
            if (unassigned > 0)
                text += " " + "MU_Upgrades_Ui_Policy_UsedByDefault".Translate(unassigned);
        }

        return text;
    }

    private static IEnumerable<IGrouping<ThingDef, CompUpgradableMechWithPolicy>> CommonestFirst(IEnumerable<CompUpgradableMechWithPolicy> mechs) =>
        mechs.GroupBy(c => c.Mech.def).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.label);

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

        // The colony's own mech types first, commonest first, with how many there are
        var counts = CompUpgradableMechWithPolicy.PlayerMechs()
            .GroupBy(c => c.Mech.def)
            .ToDictionary(g => g.Key, g => g.Count());
        int Count(ThingDef def) => counts.TryGetValue(def, out var n) ? n : 0;

        foreach (var x in upgradableMechRaces.Value.OrderByDescending(Count))
        {
            var label = Count(x) > 0 ? $"{x.LabelCap} ({Count(x)})" : x.LabelCap.ToString();
            yield return new Widgets.DropdownMenuElement<ThingDef>
            {
                payload = x,
                option = new FloatMenuOption(label, () => dialog._mechDef = x, x)
            };
        }
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
        var plan = UpgradePlanner.Plan(mechDef, maxPoints, [], policy, candidates);
        skipped?.AddRange(plan.Skipped);
        return plan.Add;
    }

    // The ranked list's reorder group, made on repaint and used by every event until the next one
    private int _rankGroup = -1;

    private void DoPreview(Rect rect)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(8f);

        using (new TextBlock(GameFont.Medium))
        {
            Widgets.Label(inner with { height = 32f }, "MU_Upgrades_Ui_Preview_Title".Translate());
        }

        inner.yMin += 34f;

        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, Color.gray))
        {
            var usedBy = UsedBy(SelectedPolicy);
            var usedByHeight = Text.CalcHeight(usedBy, inner.width);
            Widgets.Label(inner with { height = usedByHeight }, usedBy);
            inner.yMin += usedByHeight + 6f;
        }

        // Changes how this policy picks, so the statuses shown elsewhere are stale
        var replaceRect = inner with { height = 24f };
        var replace = SelectedPolicy.ReplaceLowerRanked;
        Widgets.CheckboxLabeled(replaceRect, "MU_Upgrades_Ui_Policy_Replace".Translate(), ref replace);
        TooltipHandler.TipRegionByKey(replaceRect, "MU_Upgrades_Ui_Policy_Replace_Tip");
        if (replace != SelectedPolicy.ReplaceLowerRanked)
        {
            SelectedPolicy.ReplaceLowerRanked = replace;
            UpgradeStatus.Invalidate();
        }

        inner.yMin += 30f;

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

        if (_mechDef == null)
        {
            y = DoRankingForAll(viewRect, y);
        }
        else
        {
            // The mech the dialog was opened for, as it stands, while its type is the one shown
            if (_openedForComp != null && _openedFor is { Dead: false } && _openedFor.def == _mechDef)
                y = DoThisMech(viewRect, y, _openedForComp) + 12f;

            y = DoNewMech(viewRect, y);
        }

        if (Event.current.type == EventType.Layout)
            _previewHeight = y;
        Widgets.EndScrollView();

        using (new TextBlock(GameFont.Tiny, TextAnchor.LowerLeft, Color.gray))
        {
            Widgets.Label(inner with { yMin = inner.yMax - footerHeight }, footerKey.Translate());
        }
    }

    // What the selected policy would do to this mech, from what it has now and what's in stock
    private float DoThisMech(Rect viewRect, float y, CompUpgradableMechWithPolicy comp)
    {
        // Worked out every frame, so ticking an upgrade shows at once. The dialog pauses the game
        var status = UpgradeStatus.Compute(comp, SelectedPolicy);
        y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_ThisMech".Translate(comp.Mech.LabelShort));

        if (status.Blocker != null)
            y = DoWrapped(viewRect, y, status.Blocker.Colorize(UpgradeStatus.RemovingColor));

        if (status.Lines.Count == 0)
            y = DoNothing(viewRect, y);
        foreach (var line in status.Lines)
            y = DoUpgradeRow(viewRect, y, UpgradePlanner.Sample(line.Def), UpgradeStatus.StateLabel(line), line.State == UpgradeState.Skipped);

        return DoWrapped(viewRect, y + 2f, "MU_Upgrades_Ui_Status_PointsAfter".Translate(status.PointsAfter, status.MaxPoints));
    }

    // What the selected policy gives a mech of the chosen type that has no upgrades yet, with everything in stock
    private float DoNewMech(Rect viewRect, float y)
    {
        var mech = PreviewMech();
        int? maxPoints = mech != null ? MechUpgradeUtility.MaxUpgradePoints(mech) : null;
        var skipped = new List<SkippedUpgrade>();
        var gets = Preview(SelectedPolicy, _mechDef, maxPoints, skipped);
        var used = MechUpgradeUtility.CurrentPoints(gets);

        y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_NewMech".Translate(_mechDef.label));
        string summary = maxPoints.HasValue
            ? "MU_Upgrades_Ui_Preview_Points".Translate(_mechDef.LabelCap, used, maxPoints.Value)
            : "MU_Upgrades_Ui_Preview_PointsUnknown".Translate(_mechDef.LabelCap, used);
        y = DoWrapped(viewRect, y, summary) + 4f;

        // Every allowed upgrade it can have, in rank order: the ones it gets plainly, the rest greyed with why
        var ranked = SelectedPolicy.InRankOrder(SelectedPolicy.AllowedUpgradeDefs.Where(d => d.CanAdd(_mechDef).Accepted)).ToList();
        y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_Ranked".Translate(), sub: true);
        return DoRankedList(viewRect, y, ranked, def =>
        {
            if (gets.Any(u => u.def == def))
                return (null, false);
            return (skipped.FirstOrDefault(s => s.Upgrade.def == def).Reason ?? "MU_Upgrades_Ui_State_Skipped".Translate(), true);
        });
    }

    // With no mech type chosen: the whole ranking, without what any one type would get
    private float DoRankingForAll(Rect viewRect, float y)
    {
        var ranked = SelectedPolicy.InRankOrder(SelectedPolicy.AllowedUpgradeDefs).ToList();
        y = DoHeading(viewRect, y, "MU_Upgrades_Ui_Preview_RankedAll".Translate());
        return DoRankedList(viewRect, y, ranked, _ => (null, false));
    }

    // Upgrades in rank order, dragged to rerank. Each row's subtitle and greying come from mark
    private float DoRankedList(Rect viewRect, float y, List<MechUpgradeDef> ranked, Func<MechUpgradeDef, (string subtitle, bool greyed)> mark)
    {
        if (ranked.Count == 0)
            return DoNothing(viewRect, y);

        if (Event.current.type == EventType.Repaint)
        {
            var policy = SelectedPolicy;
            _rankGroup = ReorderableWidget.NewGroup(
                (from, to) =>
                {
                    policy.Move(ranked, from, to);
                    UpgradeStatus.Invalidate();
                },
                ReorderableDirection.Vertical,
                new Rect(0f, 0f, viewRect.width, viewRect.height)
            );
        }

        foreach (var def in ranked)
        {
            var (subtitle, greyed) = mark(def);
            var row = new Rect(0f, y, viewRect.width, subtitle == null ? RowHeight : SkippedRowHeight);
            ReorderableWidget.Reorderable(_rankGroup, row);
            y = DoUpgradeRow(viewRect, y, UpgradePlanner.Sample(def), subtitle, greyed, draggable: true);
        }

        return y;
    }

    // A section heading, or with sub a grey one inside a section
    private static float DoHeading(Rect viewRect, float y, string label, bool sub = false)
    {
        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, sub ? Color.gray : Color.white))
        {
            Widgets.Label(new Rect(0f, y, viewRect.width, 24f), label);
        }

        Widgets.DrawLineHorizontal(0f, y + 23f, viewRect.width, Widgets.SeparatorLineColor);
        return y + 26f;
    }

    private static float DoWrapped(Rect viewRect, float y, string text)
    {
        using (new TextBlock(GameFont.Small, TextAnchor.UpperLeft, Color.white))
        {
            var height = Text.CalcHeight(text, viewRect.width);
            Widgets.Label(new Rect(0f, y, viewRect.width, height), text);
            return y + height + 2f;
        }
    }

    private static float DoNothing(Rect viewRect, float y)
    {
        using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft, Color.gray))
        {
            Widgets.Label(new Rect(4f, y, viewRect.width, RowHeight), "MU_Upgrades_Ui_Preview_Nothing".Translate());
        }

        return y + RowHeight;
    }

    // An upgrade, with an optional line under its name: why it's skipped, or its state. A greyed row won't be fitted. A
    // draggable one has a drag handle on the left
    private static float DoUpgradeRow(Rect viewRect, float y, MechUpgrade upgrade, [CanBeNull] string subtitle, bool greyed, bool draggable = false)
    {
        var height = subtitle == null ? RowHeight : SkippedRowHeight;
        var row = new Rect(0f, y, viewRect.width, height);
        if (Mouse.IsOver(row))
            Widgets.DrawHighlight(row);

        var color = greyed ? Color.gray : Color.white;
        var def = upgrade.def;

        var left = row.x + 2f;
        if (draggable)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.5f);
            GUI.DrawTexture(new Rect(left, row.y + 6f, 16f, 16f), TexButton.DragHash);
            GUI.color = Color.white;
            left += 20f;
        }

        var iconRect = new Rect(left, row.y + 2f, 24f, 24f);
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

        if (subtitle != null)
        {
            // A state label carries its own colour, which a grey tint would muddy
            using (new TextBlock(GameFont.Tiny, TextAnchor.UpperLeft, greyed ? Color.gray : Color.white))
            {
                Widgets.LabelEllipses(new Rect(labelRect.x, row.y + RowHeight - 6f, row.width - labelRect.x, 20f), subtitle);
            }
        }

        if (Mouse.IsOver(row) && !Mouse.IsOver(infoRect))
        {
            var tip = MechUpgradeUtility.DescWithStats(upgrade);
            if (subtitle != null)
                tip = $"{(greyed ? subtitle.Colorize(ColoredText.WarningColor) : subtitle)}\n\n{tip}";
            TooltipHandler.TipRegion(row, tip);
        }

        return y + height + 2f;
    }
}
