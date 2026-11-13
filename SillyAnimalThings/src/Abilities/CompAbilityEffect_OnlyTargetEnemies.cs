using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Vanilla's CompAbilityEffect_OnlyTargetHostiles, but usable by a player's roaming animals (see HostilityUtility)
public class CompAbilityEffect_OnlyTargetEnemies : CompAbilityEffect
{
	public override bool Valid(
		LocalTargetInfo target,
		bool throwMessages = false
	)
	{
		return parent.pawn != null && target.Thing != null && target.Thing.IsEnemyOf(parent.pawn);
	}

	// Only enemies in range. Otherwise the AI, finding no one to aim at, casts it on itself and wastes it on nobody.
	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		return Valid(target) && target.Cell.InHorDistOf(parent.pawn.Position, parent.verb.verbProps.range);
	}
}
