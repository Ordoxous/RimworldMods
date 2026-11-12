using UnityEngine;
using Verse;

namespace Ordo.SillyAnimalThings;

public static class KnockbackUtility
{
	// Staggers the pawn and shoves it one cell directly away from fromCell, unless it's bigger than the instigator
	public static void KnockBack(
		Pawn pawn,
		IntVec3 fromCell,
		Thing instigator,
		int staggerTicks
	)
	{
		pawn.stances?.stagger.StaggerFor(staggerTicks);
		Daze(pawn, staggerTicks);

		if (instigator is Pawn instigatorPawn && pawn.BodySize > instigatorPawn.BodySize)
		{
			return;
		}

		var map = pawn.Map;
		var offset = pawn.Position - fromCell;
		var direction = new IntVec3(Mathf.Clamp(offset.x, -1, 1), 0, Mathf.Clamp(offset.z, -1, 1));
		if (direction == IntVec3.Zero)
		{
			return;
		}

		var destination = pawn.Position + direction;
		if (!destination.InBounds(map) || !destination.Standable(map) || destination.GetFirstPawn(map) != null)
		{
			return;
		}

		pawn.Position = destination;
		pawn.Notify_Teleported(endCurrentJob: true, resetTweenedPos: false);
	}

	// Stagger has no visual of its own, so this shows stun stars for as long as it lasts
	private static void Daze(
		Pawn pawn,
		int ticks
	)
	{
		var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(SillyAnimalThingsDefOf.Ordo_Dazed)
		             ?? pawn.health.AddHediff(SillyAnimalThingsDefOf.Ordo_Dazed);
		if (hediff.TryGetComp<HediffComp_Disappears>() is { } disappears)
		{
			disappears.ticksToDisappear = Mathf.Max(disappears.ticksToDisappear, ticks);
		}
	}
}
