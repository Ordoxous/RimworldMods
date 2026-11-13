using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// The reverse of vanilla's CompAbilityEffect_OnlyTargetHostiles, for area buffs. The caster counts as its own ally.
public class CompAbilityEffect_OnlyTargetAllies : CompAbilityEffect
{
	public override bool Valid(
		LocalTargetInfo target,
		bool throwMessages = false
	)
	{
		var pawn = parent.pawn;
		return pawn != null && target.Pawn is { } targetPawn && (targetPawn == pawn || targetPawn.IsAllyOf(pawn));
	}
}
