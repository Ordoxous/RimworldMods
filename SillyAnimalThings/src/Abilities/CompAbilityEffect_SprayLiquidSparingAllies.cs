using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Vanilla's spray only avoids the caster's own faction, so wild animals, which have none, would spray their own herd
public class CompAbilityEffect_SprayLiquidSparingAllies : CompAbilityEffect_SprayLiquid
{
	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		if (!base.AICanTargetNow(target))
		{
			return false;
		}

		var pawn = parent.pawn;
		foreach (var cell in AffectedCells(target))
		{
			if (cell.GetFirstPawn(pawn.Map) is { } other && other.IsAllyOf(pawn))
			{
				return false;
			}
		}

		return true;
	}
}
