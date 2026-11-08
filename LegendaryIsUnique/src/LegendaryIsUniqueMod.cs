using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.LegendaryIsUnique;

public class LegendaryIsUniqueSettings : ModSettings
{
	private const QualityCategory DefaultMinimumQuality = QualityCategory.Legendary;

	private const float DefaultUniqueChance = 1f;

	public QualityCategory MinimumQuality = DefaultMinimumQuality;

	public float UniqueChance = DefaultUniqueChance;

	public override void ExposeData()
	{
		Scribe_Values.Look(ref MinimumQuality, nameof(MinimumQuality), DefaultMinimumQuality);
		Scribe_Values.Look(ref UniqueChance, nameof(UniqueChance), DefaultUniqueChance);
		base.ExposeData();
	}
}

public class LegendaryIsUniqueMod : Mod
{
	private static readonly QualityCategory[] SelectableQualities =
	[
		QualityCategory.Excellent,
		QualityCategory.Masterwork,
		QualityCategory.Legendary,
	];

	public static LegendaryIsUniqueSettings Settings { get; private set; }

	public LegendaryIsUniqueMod(
		ModContentPack content
	) : base(content)
	{
		Settings = GetSettings<LegendaryIsUniqueSettings>();
	}

	public override void DoSettingsWindowContents(
		Rect inRect
	)
	{
		var listingStandard = new Listing_Standard();

		listingStandard.Begin(inRect);

		listingStandard.Label("Ordo_LegendaryIsUnique_MinimumQuality".Translate());
		foreach (var quality in SelectableQualities)
		{
			if (listingStandard.RadioButton(quality.GetLabel().CapitalizeFirst(), Settings.MinimumQuality == quality))
			{
				Settings.MinimumQuality = quality;
			}
		}

		listingStandard.Gap();
		Settings.UniqueChance = listingStandard.SliderLabeled(
			"Ordo_LegendaryIsUnique_UniqueChance".Translate(Settings.UniqueChance.ToStringPercent()),
			Settings.UniqueChance,
			0.05f,
			1f
		);
		Settings.UniqueChance = GenMath.RoundTo(Settings.UniqueChance, 0.05f);

		listingStandard.End();

		base.DoSettingsWindowContents(inRect);
	}

	public override string SettingsCategory()
	{
		return "LegendaryIsUnique";
	}
}
