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

		base.Impact(hitThing, blockedByShield);

		if (ChunkDef != null && map != null)
		{
			GenPlace.TryPlaceThing(ThingMaker.MakeThing(ChunkDef), cell, map, ThingPlaceMode.Near);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Defs.Look(ref ChunkDef, "chunkDef");
	}
}
