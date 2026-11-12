using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Vanilla's mental state comp, plus a halo on each pawn it terrifies for as long as the terror lasts
public class CompAbilityEffect_GiveTerror : CompAbilityEffect_GiveMentalState
{
	public override void Apply(
		LocalTargetInfo target,
		LocalTargetInfo dest
	)
	{
		base.Apply(target, dest);

		if (target.Pawn is { Spawned: true } pawn && pawn.MentalStateDef == Props.stateDef && !pawn.health.hediffSet.HasHediff(SillyAnimalThingsDefOf.Ordo_Terrified))
		{
			pawn.health.AddHediff(SillyAnimalThingsDefOf.Ordo_Terrified);
		}
	}
}
