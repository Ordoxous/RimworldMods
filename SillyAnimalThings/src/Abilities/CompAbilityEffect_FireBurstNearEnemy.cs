using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Vanilla fire burst only lets the AI cast while being aimed at; animals should burst when an enemy gets close instead
public class CompAbilityEffect_FireBurstNearEnemy : CompAbilityEffect_FireBurst
{
	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		var pawn = parent.pawn;
		if (target.Pawn is not { } targetPawn || targetPawn == pawn)
		{
			return false;
		}

		var radius = ((CompProperties_AbilityFireBurst)props).radius;
		return !targetPawn.Downed
		       && targetPawn.IsEnemyOf(pawn)
		       && targetPawn.Position.InHorDistOf(pawn.Position, radius);
	}
}
