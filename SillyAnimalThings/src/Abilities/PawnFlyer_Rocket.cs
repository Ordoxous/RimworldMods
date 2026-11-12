using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.SillyAnimalThings;

// Detonate: the animal arcs through the air trailing fire and smoke like a rocket, then bursts where it lands. Killing it
// fires its race's death action, which is the explosion (or the toxalope's tox cloud).
public class PawnFlyer_Rocket : PawnFlyer_Trail
{
	protected override void Trail(
		Vector3 position
	)
	{
		FleckMaker.ThrowFireGlow(position, Map, Rand.Range(0.8f, 1.2f));
		FleckMaker.ThrowSmoke(position, Map, Rand.Range(1f, 1.6f));
		FleckMaker.ThrowMicroSparks(position, Map);
	}

	public override void RespawnPawn()
	{
		var pawn = FlyingPawn;
		base.RespawnPawn();

		if (pawn is { Spawned: true, Dead: false })
		{
			pawn.Kill(null);
		}
	}
}
