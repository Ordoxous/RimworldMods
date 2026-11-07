using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Vanilla fire burst only lets the AI cast while being aimed at; boomrats should burst when an enemy gets close instead
public class CompAbilityEffect_FireBurstNearEnemy : CompAbilityEffect_FireBurst
{
	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		var pawn = parent.pawn;
		if (pawn.Faction == Faction.OfPlayer || target.Pawn is not { } targetPawn || targetPawn == pawn)
		{
			return false;
		}

		var radius = ((CompProperties_AbilityFireBurst)props).radius;
		return !targetPawn.Downed
		       && targetPawn.HostileTo(pawn)
		       && targetPawn.Position.InHorDistOf(pawn.Position, radius);
	}
}
