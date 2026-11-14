using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

public class MechUpgradesUiSettings : ModSettings
{
    private const bool DefaultUseDefaultPolicyForUnassigned = false;
    private const bool DefaultShowDialogOnManualInsert = true;
    private const float DefaultPolicyCheckIntervalHours = 3f;
    private const bool DefaultShowNotInStockAlert = true;
    private const bool DefaultShowBlockedAlert = true;

    public bool UseDefaultPolicyForUnassigned = DefaultUseDefaultPolicyForUnassigned;
    public bool ShowDialogOnManualInsert = DefaultShowDialogOnManualInsert;
    public float PolicyCheckIntervalHours = DefaultPolicyCheckIntervalHours;
    public bool ShowNotInStockAlert = DefaultShowNotInStockAlert;
    public bool ShowBlockedAlert = DefaultShowBlockedAlert;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref UseDefaultPolicyForUnassigned, nameof(UseDefaultPolicyForUnassigned), DefaultUseDefaultPolicyForUnassigned);
        Scribe_Values.Look(ref ShowDialogOnManualInsert, nameof(ShowDialogOnManualInsert), DefaultShowDialogOnManualInsert);
        Scribe_Values.Look(ref PolicyCheckIntervalHours, nameof(PolicyCheckIntervalHours), DefaultPolicyCheckIntervalHours);
        Scribe_Values.Look(ref ShowNotInStockAlert, nameof(ShowNotInStockAlert), DefaultShowNotInStockAlert);
        Scribe_Values.Look(ref ShowBlockedAlert, nameof(ShowBlockedAlert), DefaultShowBlockedAlert);
        base.ExposeData();
    }
}

public class MechUpgradesUiMod : Mod
{
    public static MechUpgradesUiSettings Settings { get; private set; }

    public MechUpgradesUiMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<MechUpgradesUiSettings>();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        // Turning this on can strip upgrades from every mech without a policy, so it asks first
        var useDefault = Settings.UseDefaultPolicyForUnassigned;
        listing.CheckboxLabeled(
            "MU_Upgrades_Ui_Settings_UseDefault".Translate(),
            ref useDefault,
            "MU_Upgrades_Ui_Settings_UseDefault_Tip".Translate()
        );
        if (useDefault && !Settings.UseDefaultPolicyForUnassigned)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                UseDefaultConfirmation(),
                () => Settings.UseDefaultPolicyForUnassigned = true,
                destructive: true
            ));
        }
        else
        {
            Settings.UseDefaultPolicyForUnassigned = useDefault;
        }

        listing.CheckboxLabeled(
            "MU_Upgrades_Ui_Settings_ManualDialog".Translate(),
            ref Settings.ShowDialogOnManualInsert,
            "MU_Upgrades_Ui_Settings_ManualDialog_Tip".Translate()
        );

        listing.Gap();
        Settings.PolicyCheckIntervalHours = listing.SliderLabeled(
            "MU_Upgrades_Ui_Settings_Interval".Translate(Settings.PolicyCheckIntervalHours.ToString("0.#")),
            Settings.PolicyCheckIntervalHours,
            1f,
            24f,
            tooltip: "MU_Upgrades_Ui_Settings_Interval_Tip".Translate()
        );
        Settings.PolicyCheckIntervalHours = GenMath.RoundTo(Settings.PolicyCheckIntervalHours, 0.5f);

        listing.Gap();
        listing.CheckboxLabeled(
            "MU_Upgrades_Ui_Settings_NotInStockAlert".Translate(),
            ref Settings.ShowNotInStockAlert,
            "MU_Upgrades_Ui_Settings_NotInStockAlert_Tip".Translate()
        );
        listing.CheckboxLabeled(
            "MU_Upgrades_Ui_Settings_BlockedAlert".Translate(),
            ref Settings.ShowBlockedAlert,
            "MU_Upgrades_Ui_Settings_BlockedAlert_Tip".Translate()
        );

        listing.End();

        base.DoSettingsWindowContents(inRect);
    }

    /// <summary>The warning before mechs without a policy start following the default one, with what it would cost in a game.</summary>
    public static string UseDefaultConfirmation()
    {
        string text = "MU_Upgrades_Ui_Settings_UseDefault_Confirm".Translate();
        if (Current.ProgramState != ProgramState.Playing)
            return text;

        var policy = Find.World.GetComponent<WorldComponent_UpgradeDatabase>().DefaultPolicy();
        var losing = CompUpgradableMechWithPolicy.PlayerMechs()
            .Where(c => c.CurrentUpgradePolicy == null && c.upgrades.Any(u => !policy.Filter.Allows(u.def.linkedThingDef)))
            .Select(c => c.Mech.LabelShort)
            .ToList();
        return losing.Count == 0
            ? $"{text}\n\n{"MU_Upgrades_Ui_Settings_UseDefault_Confirm_None".Translate(policy.label)}"
            : $"{text}\n\n{"MU_Upgrades_Ui_Settings_UseDefault_Confirm_Losing".Translate(policy.label, losing.ToCommaList())}";
    }

    public override string SettingsCategory() => "Mech Upgrade Policies";
}
