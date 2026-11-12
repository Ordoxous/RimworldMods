using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.SillyAnimalThings;

// Draws as the chunk it was made from and drops that chunk where it lands
public class Projectile_ThrownChunk : Bullet
{
	public ThingDef ChunkDef;

	public override Material DrawMat => ChunkDef?.graphic?.MatSingle ?? base.DrawMat;

	public override void Impact(
		Thing hitThing,
		bool blockedByShield = false
	)
	{
		var map = Map;
		var cell = Position;
		if (hitThing is Pawn)
		{
			cell = BounceBack(cell);
		}

		base.Impact(hitThing, blockedByShield);

		if (ChunkDef != null && map != null)
		{
			GenPlace.TryPlaceThing(ThingMaker.MakeThing(ChunkDef), cell, map, ThingPlaceMode.Near);
		}
	}

	// A chunk dropped in the cell of the pawn it hit is drawn underneath that pawn and can't be seen, so it bounces one cell
	// back the way it came
	private IntVec3 BounceBack(
		IntVec3 cell
	)
	{
		var back = origin.ToIntVec3() - cell;
		var bounced = cell + new IntVec3(Mathf.Clamp(back.x, -1, 1), 0, Mathf.Clamp(back.z, -1, 1));
		return bounced.InBounds(Map) && bounced.Standable(Map) ? bounced : cell;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Defs.Look(ref ChunkDef, "chunkDef");
	}
}
