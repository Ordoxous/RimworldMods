using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Puts out fires in the cell it lands in and shoves pawns there one cell away from the launcher
public class Projectile_WaterBlast : Projectile
{
	private const int StaggerTicks = 90;

	private static readonly List<Thing> _tmpThings = new();

	public override void Impact(
		Thing hitThing,
		bool blockedByShield = false
	)
	{
		var map = Map;
		var cell = Position;
		var launcherCell = launcher?.Position ?? origin.ToIntVec3();

		if (!blockedByShield)
		{
			_tmpThings.Clear();
			_tmpThings.AddRange(cell.GetThingList(map));
			foreach (var thing in _tmpThings)
			{
				if (thing.Destroyed)
				{
					continue;
				}

				if (thing is Fire)
				{
					thing.Destroy();
					continue;
				}

				thing.GetAttachment(ThingDefOf.Fire)?.Destroy();

				if (thing is Pawn pawn && pawn != launcher && (!preventFriendlyFire || launcher is not Pawn caster || pawn.IsEnemyOf(caster)))
				{
					KnockbackUtility.KnockBack(pawn, launcherCell, launcher, StaggerTicks);
				}
			}

			_tmpThings.Clear();
		}

		base.Impact(hitThing, blockedByShield);
	}
}
