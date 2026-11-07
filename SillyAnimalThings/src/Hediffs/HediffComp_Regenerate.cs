using System.Collections.Generic;
using Verse;

namespace Ordo.SillyAnimalThings;

public class HediffCompProperties_Regenerate : HediffCompProperties
{
	public int intervalTicks = 60;

	// Healed from every non-permanent injury each interval
	public float healPerInterval = 1f;

	public HediffCompProperties_Regenerate()
	{
		compClass = typeof(HediffComp_Regenerate);
	}
}

public class HediffComp_Regenerate : HediffComp
{
	private static List<Hediff_Injury> _tmpInjuries = new();

	public HediffCompProperties_Regenerate Props => (HediffCompProperties_Regenerate)props;

	public override void CompPostTick(
		ref float severityAdjustment
	)
	{
		if (!Pawn.IsHashIntervalTick(Props.intervalTicks))
		{
			return;
		}

		_tmpInjuries.Clear();
		Pawn.health.hediffSet.GetHediffs(ref _tmpInjuries, injury => !injury.IsPermanent());
		foreach (var injury in _tmpInjuries)
		{
			injury.Heal(Props.healPerInterval);
		}

		_tmpInjuries.Clear();
	}
}
