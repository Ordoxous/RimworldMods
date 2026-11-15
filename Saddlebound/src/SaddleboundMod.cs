using HarmonyLib;
using UnityEngine;
using Verse;

namespace Ordo.Saddlebound;

public class SaddleboundMod : Mod
{
    public static SaddleboundSettings Settings;

    public SaddleboundMod(
        ModContentPack content
    ) : base(content)
    {
        Settings = GetSettings<SaddleboundSettings>();
        SaddleboundPatches.Apply(new Harmony("Ordo.Saddlebound"));
    }

    public override string SettingsCategory() => "Saddlebound";

    public override void DoSettingsWindowContents(
        Rect inRect
    )
    {
        var list = new Listing_Standard();
        list.Begin(inRect);
        list.CheckboxLabeled(
            "Ordo_Saddlebound_HordeRaids".Translate(),
            ref Settings.hordeRaids,
            "Ordo_Saddlebound_HordeRaidsTip".Translate(SaddleboundSettings.HordeMountChance)
        );
        list.End();
    }
}

public class SaddleboundSettings : ModSettings
{
    // The share of raiders a Horde Riders faction tries to mount, in place of Giddy-Up's enemy mount chance
    public const int HordeMountChance = 90;

    public bool hordeRaids = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref hordeRaids, "hordeRaids", true);
    }
}
