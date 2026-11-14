using System.Collections.Generic;
using System.Linq;
using System.Text;
using MU;
using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

/// <summary>Alerts about mechs whose upgrade policy can't be carried out. Each can be turned off in the mod settings.</summary>
public abstract class Alert_MechUpgradesBase : Alert
{
    protected static IEnumerable<(Pawn mech, UpgradeStatus status)> ManagedMechs() =>
        CompUpgradableMechWithPolicy.PlayerMechs()
            .Where(c => c.EffectiveUpgradePolicy != null)
            .Select(c => (c.Mech, UpgradeStatus.For(c)));
}

/// <summary>Mechs' policies want upgrades that no usable auto upgrader has in stock: a shopping list.</summary>
public class Alert_UpgradesNotInStock : Alert_MechUpgradesBase
{
    private readonly List<Pawn> _culprits = [];

    private readonly Dictionary<MechUpgradeDef, List<Pawn>> _wantedBy = new();

    public Alert_UpgradesNotInStock()
    {
        defaultLabel = "MU_Upgrades_Ui_Alert_NotInStock".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    /// <summary>Each upgrade that's wanted and not in stock, with the mechs that want it. As of the last report.</summary>
    public IReadOnlyDictionary<MechUpgradeDef, List<Pawn>> WantedBy => _wantedBy;

    public override AlertReport GetReport()
    {
        _culprits.Clear();
        _wantedBy.Clear();
        if (!MechUpgradesUiMod.Settings.ShowNotInStockAlert)
            return false;

        // A mech that can't reach any upgrader is the other alert's
        foreach (var (mech, status) in ManagedMechs().Where(x => x.status.HasShortage && x.status.Blocker == null))
        {
            _culprits.Add(mech);
            foreach (var def in status.Unstocked)
            {
                if (!_wantedBy.TryGetValue(def, out var mechs))
                    _wantedBy[def] = mechs = [];
                mechs.Add(mech);
            }
        }

        return AlertReport.CulpritsAre(_culprits);
    }

    public override TaggedString GetExplanation()
    {
        var text = new StringBuilder("MU_Upgrades_Ui_Alert_NotInStock_Desc".Translate());
        text.AppendLine();
        text.AppendLine();
        foreach (var (def, mechs) in _wantedBy.OrderBy(kv => kv.Key.label).Select(kv => (kv.Key, kv.Value)))
            text.AppendLine($"  - {def.LabelCap}: {mechs.Select(m => m.LabelShort).ToCommaList()}");
        return text.ToString().TrimEndNewlines();
    }
}

/// <summary>Mechs whose policy wants changes, with no auto upgrader they can use to make them.</summary>
public class Alert_UpgradePolicyBlocked : Alert_MechUpgradesBase
{
    private readonly List<Pawn> _culprits = [];

    private readonly Dictionary<Pawn, string> _reasons = new();

    public Alert_UpgradePolicyBlocked()
    {
        defaultLabel = "MU_Upgrades_Ui_Alert_Blocked".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    public override AlertReport GetReport()
    {
        _culprits.Clear();
        _reasons.Clear();
        if (!MechUpgradesUiMod.Settings.ShowBlockedAlert)
            return false;

        foreach (var (mech, status) in ManagedMechs().Where(x => x.status.Blocker != null))
        {
            _culprits.Add(mech);
            _reasons[mech] = status.Blocker;
        }

        return AlertReport.CulpritsAre(_culprits);
    }

    public override TaggedString GetExplanation()
    {
        var text = new StringBuilder("MU_Upgrades_Ui_Alert_Blocked_Desc".Translate());
        text.AppendLine();
        text.AppendLine();
        foreach (var mech in _culprits)
            text.AppendLine($"  - {mech.LabelShort}: {_reasons[mech]}");
        return text.ToString().TrimEndNewlines();
    }
}
