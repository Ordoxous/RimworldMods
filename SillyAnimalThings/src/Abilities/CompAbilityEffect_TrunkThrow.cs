using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class CompProperties_AbilityTrunkThrow : CompProperties_AbilityEffect
{
	public ThingDef projectileDef;

	public float chunkSearchRadius = 2.9f;

	public CompProperties_AbilityTrunkThrow()
	{
		compClass = typeof(CompAbilityEffect_TrunkThrow);
	}
}

// Picks up a chunk near the caster and launches it; the chunk lands where the projectile does
public class CompAbilityEffect_TrunkThrow : CompAbilityEffect
{
	public new CompProperties_AbilityTrunkThrow Props => (CompProperties_AbilityTrunkThrow)props;

	public override void Apply(
		LocalTargetInfo target,
		LocalTargetInfo dest
	)
	{
		base.Apply(target, dest);

		var pawn = parent.pawn;
		var chunk = FindChunk();
		if (chunk == null)
		{
			return;
		}

		var chunkDef = chunk.def;
		chunk.Destroy();

		var projectile = (Projectile_ThrownChunk)GenSpawn.Spawn(Props.projectileDef, pawn.Position, pawn.Map);
		projectile.ChunkDef = chunkDef;
		projectile.Launch(pawn, pawn.DrawPos, target, target, ProjectileHitFlags.IntendedTarget, parent.verb.preventFriendlyFire);
	}

	public override bool GizmoDisabled(
		out string reason
	)
	{
		if (parent.pawn.Spawned && FindChunk() == null)
		{
			reason = "Ordo_SillyAnimalThings_TrunkThrowNoChunk".Translate(parent.pawn.Named("PAWN"));
			return true;
		}

		return base.GizmoDisabled(out reason);
	}

	public override bool Valid(
		LocalTargetInfo target,
		bool throwMessages = false
	)
	{
		if (FindChunk() == null)
		{
			if (throwMessages)
			{
				Messages.Message(
					"Ordo_SillyAnimalThings_TrunkThrowNoChunk".Translate(parent.pawn.Named("PAWN")),
					parent.pawn,
					MessageTypeDefOf.RejectInput,
					historical: false
				);
			}

			return false;
		}

		return base.Valid(target, throwMessages);
	}

	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		return target.Pawn is { Downed: false } targetPawn && targetPawn.IsEnemyOf(parent.pawn) && FindChunk() != null;
	}

	private Thing FindChunk()
	{
		var pawn = parent.pawn;
		if (!pawn.Spawned)
		{
			return null;
		}

		foreach (var thing in GenRadial.RadialDistinctThingsAround(pawn.Position, pawn.Map, Props.chunkSearchRadius, useCenter: true))
		{
			if (thing.def.IsWithinCategory(ThingCategoryDefOf.Chunks) && !thing.IsBurning() && pawn.Map.reservationManager.FirstRespectedReserver(thing, pawn) == null)
			{
				return thing;
			}
		}

		return null;
	}
}
