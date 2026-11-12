using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class CompProperties_AbilityDetonate : CompProperties_AbilityEffect
{
	// How far away a hostile can be before a wild or enemy caster decides to charge it
	public float aiMaxDistance = 15f;

	public CompProperties_AbilityDetonate()
	{
		compClass = typeof(CompAbilityEffect_Detonate);
	}
}

// Decides when the AI launches. The burst itself happens where the animal lands, in PawnFlyer_Rocket, because the jump verb
// applies ability comps at take-off.
public class CompAbilityEffect_Detonate : CompAbilityEffect
{
	public new CompProperties_AbilityDetonate Props => (CompProperties_AbilityDetonate)props;

	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		var pawn = parent.pawn;
		if (pawn.Faction == Faction.OfPlayer || target.Pawn is not { } targetPawn)
		{
			return false;
		}

		return !targetPawn.Downed
		       && targetPawn.HostileTo(pawn)
		       && targetPawn.Position.InHorDistOf(pawn.Position, Props.aiMaxDistance);
	}
}
