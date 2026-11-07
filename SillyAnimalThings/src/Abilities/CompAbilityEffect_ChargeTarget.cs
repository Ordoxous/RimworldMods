using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class CompProperties_AbilityChargeTarget : CompProperties_AbilityEffect
{
	// Closer than this the AI just melees instead of charging
	public float aiMinDistance = 3.9f;

	public CompProperties_AbilityChargeTarget()
	{
		compClass = typeof(CompAbilityEffect_ChargeTarget);
	}
}

// Only lets wild or enemy casters charge at hostile pawns that aren't already in melee range
public class CompAbilityEffect_ChargeTarget : CompAbilityEffect
{
	public new CompProperties_AbilityChargeTarget Props => (CompProperties_AbilityChargeTarget)props;

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
		       && !targetPawn.Position.InHorDistOf(pawn.Position, Props.aiMinDistance);
	}
}
