using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using MU;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>Where one upgrade stands for a mech under a policy.</summary>
public enum UpgradeState
{
    /// <summary>Installed and allowed, so it stays.</summary>
    Installed,

    /// <summary>The next trip fits it, from a stocked auto upgrader.</summary>
    Adding,

    /// <summary>The policy would fit it, but no auto upgrader has one in stock.</summary>
    Wanted,

    /// <summary>Allowed, but it won't be fitted: no points left, or it conflicts with something the mech will have.</summary>
    Skipped,

    /// <summary>Installed and not allowed, so the next trip removes it.</summary>
    Removing
}

/// <summary>An upgrade's state, with the reason when it's skipped.</summary>
public readonly struct UpgradeLine(MechUpgradeDef def, UpgradeState state, string reason)
{
    public readonly MechUpgradeDef Def = def;

    public readonly UpgradeState State = state;

    public readonly string Reason = reason;
}

/// <summary>What a policy would change on a mech, for the Mechs tab, the policy menus and the gizmo.</summary>
public sealed class UpgradeStatus
{
    private const float CacheSeconds = 1f;

    private static readonly Dictionary<(Pawn, UpgradePolicy), (float time, UpgradeStatus status)> Cache = new();

    private static readonly Color AddingColor = new(0.45f, 0.9f, 0.45f);

    private static readonly Color WantedColor = new(0.95f, 0.8f, 0.35f);

    private static readonly Color RemovingColor = new(0.95f, 0.45f, 0.4f);

    /// <summary>Installed upgrades the policy doesn't allow. They go on the next trip.</summary>
    public List<MechUpgrade> Removals = [];

    /// <summary>What the next trip would fit, from the best stocked auto upgrader on the mech's map.</summary>
    public List<MechUpgrade> Additions = [];

    /// <summary>Upgrades the policy would pick if they were in stock, and aren't.</summary>
    public List<MechUpgradeDef> Unstocked = [];

    /// <summary>Every upgrade the policy allows for this mech's type, and every installed one, with where it stands.</summary>
    public List<UpgradeLine> Lines = [];

    /// <summary>The points the mech's upgrades will use after the next trip.</summary>
    public int PointsAfter;

    public int MaxPoints;

    public bool HasChanges => Removals.Count > 0 || Additions.Count > 0;

    /// <summary>
    /// The status under a policy (the mech's effective one by default), worked out at most once a second per mech and policy,
    /// since the Mechs tab and tooltips ask every frame.
    /// </summary>
    public static UpgradeStatus For(CompUpgradableMechWithPolicy comp, [CanBeNull] UpgradePolicy policy = null)
    {
        policy ??= comp.EffectiveUpgradePolicy;
        var key = (comp.Mech, policy);
        var now = Time.realtimeSinceStartup;
        if (Cache.TryGetValue(key, out var cached) && now - cached.time < CacheSeconds)
            return cached.status;

        if (Cache.Count > 500)
            Cache.Clear();

        var status = Compute(comp, policy);
        Cache[key] = (now, status);
        return status;
    }

    /// <summary>The status under a policy: the mech's effective one by default. With no policy, nothing changes.</summary>
    public static UpgradeStatus Compute(CompUpgradableMechWithPolicy comp, [CanBeNull] UpgradePolicy policy = null)
    {
        policy ??= comp.EffectiveUpgradePolicy;
        var mech = comp.Mech;
        var status = new UpgradeStatus { MaxPoints = MechUpgradeUtility.MaxUpgradePoints(mech) };
        if (policy == null)
        {
            status.PointsAfter = MechUpgradeUtility.CurrentPoints(comp.upgrades);
            status.Lines = comp.upgrades.Select(u => new UpgradeLine(u.def, UpgradeState.Installed, null)).ToList();
            return status;
        }

        var keep = comp.UpgradesToKeep(policy);
        var wanted = comp.WantedUpgrades(keep, policy);
        var freePoints = status.MaxPoints - MechUpgradeUtility.CurrentPoints(keep);

        status.Removals = comp.upgrades.Where(u => !keep.Contains(u)).ToList();

        var map = mech.MapHeld;
        if (map != null)
        {
            status.Additions = map.listerBuildings
                .AllColonistBuildingsOfType<Building_AutoMechUpgrader>()
                .Select(b => UpgradePlanner.PickAdditions(mech.def, freePoints, keep, b.StockedUpgrades().Where(u => wanted.Contains(u.def))))
                .MaxByWithFallback(added => added.Count) ?? [];
        }

        // What the policy would fit with everything in stock, and why the rest wouldn't fit
        var skipped = new List<SkippedUpgrade>();
        status.Unstocked = UpgradePlanner
            .PickAdditions(mech.def, freePoints, keep, wanted.Select(UpgradePlanner.Sample), skipped)
            .Select(u => u.def)
            .Where(d => status.Additions.All(a => a.def != d))
            .ToList();

        status.PointsAfter = MechUpgradeUtility.CurrentPoints(keep) + MechUpgradeUtility.CurrentPoints(status.Additions);

        // Allowed upgrades the mech's type can't have at all are left out: they're there for other mechs
        foreach (var def in policy.AllowedUpgradeDefs.Where(d => d.CanAdd(mech.def).Accepted).OrderBy(d => d.label))
        {
            if (keep.Any(u => u.def == def))
                status.Lines.Add(new UpgradeLine(def, UpgradeState.Installed, null));
            else if (status.Additions.Any(u => u.def == def))
                status.Lines.Add(new UpgradeLine(def, UpgradeState.Adding, null));
            else if (status.Unstocked.Contains(def))
                status.Lines.Add(new UpgradeLine(def, UpgradeState.Wanted, null));
            else
            {
                // Either it clashes with an upgrade the mech keeps, so it was never wanted, or the plan passed over it
                var reason = wanted.Contains(def)
                    ? skipped.FirstOrDefault(s => s.Upgrade.def == def).Reason
                    : def.CanAdd(mech.def, keep).Reason;
                status.Lines.Add(new UpgradeLine(def, UpgradeState.Skipped, reason));
            }
        }

        status.Lines.AddRange(status.Removals.Select(u => new UpgradeLine(u.def, UpgradeState.Removing, null)));
        return status;
    }

    /// <summary>A policy's effect on a mech, an upgrade a line, for tooltips.</summary>
    public static string Describe(CompUpgradableMechWithPolicy comp, [CanBeNull] UpgradePolicy policy)
    {
        var text = new StringBuilder();
        var status = For(comp, policy);
        policy ??= comp.EffectiveUpgradePolicy;

        if (policy == null)
        {
            text.AppendLine("MU_Upgrades_Ui_Status_Installed".Translate(status.PointsAfter, status.MaxPoints));
            AppendLines(text, status.Lines);
            text.AppendLine();
            text.Append("MU_Upgrades_Ui_Status_Unmanaged".Translate());
            return text.ToString();
        }

        text.AppendLine("MU_Upgrades_Ui_Status_Header".Translate(policy.label, comp.Mech.LabelShort).Resolve().AsTipTitle());
        AppendLines(text, status.Lines);
        text.AppendLine();
        text.Append("MU_Upgrades_Ui_Status_PointsAfter".Translate(status.PointsAfter, status.MaxPoints));
        return text.ToString();
    }

    /// <summary>The Mechs tab's tooltip for a mech's upgrades.</summary>
    public static string Tooltip(CompUpgradableMechWithPolicy comp) =>
        $"{Describe(comp, null)}\n\n{"MU_Upgrades_Ui_Status_Click".Translate()}";

    private static void AppendLines(StringBuilder text, List<UpgradeLine> lines)
    {
        if (lines.Count == 0)
        {
            text.AppendLine("  " + "MU_Upgrades_Ui_Status_None".Translate());
            return;
        }

        foreach (var line in lines)
        {
            var state = StateLabel(line);
            text.AppendLine($"  {line.Def.LabelCap}: {state}");
        }
    }

    private static string StateLabel(UpgradeLine line) =>
        line.State switch
        {
            UpgradeState.Installed => "MU_Upgrades_Ui_State_Installed".Translate().Resolve(),
            UpgradeState.Adding => "MU_Upgrades_Ui_State_Adding".Translate().Resolve().Colorize(AddingColor),
            UpgradeState.Wanted => "MU_Upgrades_Ui_State_Wanted".Translate().Resolve().Colorize(WantedColor),
            UpgradeState.Removing => "MU_Upgrades_Ui_State_Removing".Translate().Resolve().Colorize(RemovingColor),
            _ => ("MU_Upgrades_Ui_State_Skipped".Translate().Resolve()
                  + (line.Reason.NullOrEmpty() ? "" : $" ({line.Reason})")).Colorize(ColoredText.SubtleGrayColor)
        };
}
