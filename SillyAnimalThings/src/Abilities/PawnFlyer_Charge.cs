using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// On landing, rams every hostile pawn at or next to the landing cell, knocking them away from where the charge started
public class PawnFlyer_Charge : PawnFlyer
{
	private const float ImpactDamage = 25f;

	private const float ImpactArmorPenetration = 0.3f;

	private const int StaggerTicks = 120;

	private static readonly List<Pawn> _tmpVictims = new();

	public override void RespawnPawn()
	{
		var charger = FlyingPawn;
		var map = Map;
		var landingCell = destCell;
		var startCell = startVec.ToIntVec3();

		base.RespawnPawn();

		if (charger == null || map == null)
		{
			return;
		}

		_tmpVictims.Clear();
		foreach (var cell in GenAdj.CellsAdjacent8Way(new TargetInfo(landingCell, map)))
		{
			if (cell.InBounds(map))
			{
				CollectVictims(cell, map, charger);
			}
		}

		CollectVictims(landingCell, map, charger);

		foreach (var victim in _tmpVictims)
		{
			victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, ImpactDamage, ImpactArmorPenetration, instigator: charger));
			if (victim.Spawned && !victim.Dead)
			{
				KnockbackUtility.KnockBack(victim, startCell, charger, StaggerTicks);
			}
		}

		_tmpVictims.Clear();
	}

	private static void CollectVictims(
		IntVec3 cell,
		Map map,
		Pawn charger
	)
	{
		foreach (var thing in cell.GetThingList(map))
		{
			if (thing is Pawn pawn && pawn != charger && pawn.HostileTo(charger) && !_tmpVictims.Contains(pawn))
			{
				_tmpVictims.Add(pawn);
			}
		}
	}
}
