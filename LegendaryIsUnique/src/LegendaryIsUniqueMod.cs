using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.LegendaryIsUnique;

public class LegendaryIsUniqueSettings : ModSettings
{
	private const QualityCategory DefaultMinimumQuality = QualityCategory.Legendary;

	public QualityCategory MinimumQuality = DefaultMinimumQuality;

	public override void ExposeData()
	{
		Scribe_Values.Look(ref MinimumQuality, nameof(MinimumQuality), DefaultMinimumQuality);
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

		listingStandard.Label("Minimum quality for a crafted weapon to become unique:");
		foreach (var quality in SelectableQualities)
		{
			if (listingStandard.RadioButton(quality.GetLabel().CapitalizeFirst(), Settings.MinimumQuality == quality))
			{
				Settings.MinimumQuality = quality;
			}
		}

		listingStandard.End();

		base.DoSettingsWindowContents(inRect);
	}

	public override string SettingsCategory()
	{
		return "LegendaryIsUnique";
	}
}
