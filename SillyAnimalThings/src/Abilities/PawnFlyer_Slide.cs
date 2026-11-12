using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Belly slide: the pawn is drawn lying down for the length of the slide, so it flops onto its belly rather than gliding upright
public class PawnFlyer_Slide : PawnFlyer_Trail
{
	public override void Tick()
	{
		if (FlyingPawn?.jobs is { } jobs && jobs.posture != PawnPosture.LayingOnGroundNormal)
		{
			jobs.posture = PawnPosture.LayingOnGroundNormal;
			FlyingPawn.Drawer.renderer.SetAllGraphicsDirty();
		}

		base.Tick();
	}

	// Stands the pawn back up once it's on the map again; before then its drawing doesn't refresh
	public override void RespawnPawn()
	{
		var pawn = FlyingPawn;
		base.RespawnPawn();

		if (pawn?.jobs is { } jobs)
		{
			jobs.posture = PawnPosture.Standing;
			pawn.Drawer.renderer.SetAllGraphicsDirty();
		}
	}
}
