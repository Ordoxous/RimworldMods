using UnityEngine;
using Verse;

namespace Ordo.MechUpgradesUi;

public class MechUpgradesUiSettings : ModSettings
{
    private const bool DefaultUseDefaultPolicyForUnassigned = false;
    private const bool DefaultShowDialogOnManualInsert = true;
    private const float DefaultPolicyCheckIntervalHours = 3f;

    public bool UseDefaultPolicyForUnassigned = DefaultUseDefaultPolicyForUnassigned;
    public bool ShowDialogOnManualInsert = DefaultShowDialogOnManualInsert;
    public float PolicyCheckIntervalHours = DefaultPolicyCheckIntervalHours;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref UseDefaultPolicyForUnassigned, nameof(UseDefaultPolicyForUnassigned), DefaultUseDefaultPolicyForUnassigned);
        Scribe_Values.Look(ref ShowDialogOnManualInsert, nameof(ShowDialogOnManualInsert), DefaultShowDialogOnManualInsert);
        Scribe_Values.Look(ref PolicyCheckIntervalHours, nameof(PolicyCheckIntervalHours), DefaultPolicyCheckIntervalHours);
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

        listing.CheckboxLabeled(
            "MU_Upgrades_Ui_Settings_UseDefault".Translate(),
            ref Settings.UseDefaultPolicyForUnassigned,
            "MU_Upgrades_Ui_Settings_UseDefault_Tip".Translate()
        );
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

        listing.End();

        base.DoSettingsWindowContents(inRect);
    }

    public override string SettingsCategory() => "Mech Upgrade Policies";
}
