using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.SillyAnimalThings;

// A flyer that leaves a trail the whole way rather than only where it starts. Dust by default, for flyers that travel along
// the ground.
public class PawnFlyer_Trail : PawnFlyer
{
	private const int TrailIntervalTicks = 2;

	private static readonly Color DustColor = new(0.85f, 0.8f, 0.7f, 0.9f);

	public override void Tick()
	{
		base.Tick();

		if (Spawned && this.IsHashIntervalTick(TrailIntervalTicks))
		{
			Trail(DrawPos);
		}
	}

	protected virtual void Trail(
		Vector3 position
	)
	{
		FleckMaker.ThrowDustPuffThick(position, Map, Rand.Range(0.8f, 1.2f), DustColor);
	}
}
