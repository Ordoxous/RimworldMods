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

	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		return Valid(target);
	}
}
