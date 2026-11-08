using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// The reverse of vanilla's CompAbilityEffect_OnlyTargetHostiles, for area buffs
public class CompAbilityEffect_OnlyTargetAllies : CompAbilityEffect
{
	public override bool Valid(
		LocalTargetInfo target,
		bool throwMessages = false
	)
	{
		var pawn = parent.pawn;
		if (pawn == null || target.Pawn is not { } targetPawn || targetPawn.HostileTo(pawn))
		{
			return false;
		}

		// Wild animals have no faction, so their allies are their own kind
		return pawn.Faction != null
			? targetPawn.Faction == pawn.Faction
			: targetPawn.Faction == null && targetPawn.def == pawn.def;
	}
}
