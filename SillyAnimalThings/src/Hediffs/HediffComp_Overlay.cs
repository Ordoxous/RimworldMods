using UnityEngine;
using Verse;

namespace Ordo.SillyAnimalThings;

public class HediffCompProperties_Overlay : HediffCompProperties
{
	public ThingDef moteDef;

	// Zero keeps one mote on the pawn for as long as the hediff lasts. Above zero, a new one is made this often, for motes that
	// play once and fade.
	public int intervalTicks;

	public float scale = 1f;

	public Color color = Color.white;

	// When set, the hediff removes itself once the pawn is no longer in this mental state
	public MentalStateDef whileMentalState;

	public HediffCompProperties_Overlay()
	{
		compClass = typeof(HediffComp_Overlay);
	}
}

// Shows a debuff on the pawn that has it
public class HediffComp_Overlay : HediffComp
{
	private Mote _mote;

	// The most recent mote shown, which tests check for: the game doesn't list motes with the map's other things
	public Mote LastMote { get; private set; }

	public HediffCompProperties_Overlay Props => (HediffCompProperties_Overlay)props;

	public override bool CompShouldRemove => base.CompShouldRemove || (Props.whileMentalState != null && Pawn.MentalStateDef != Props.whileMentalState);

	public override void CompPostTick(
		ref float severityAdjustment
	)
	{
		if (!Pawn.Spawned || Props.moteDef == null)
		{
			return;
		}

		if (Props.intervalTicks > 0)
		{
			if (Pawn.IsHashIntervalTick(Props.intervalTicks))
			{
				Make();
			}

			return;
		}

		if (_mote == null || _mote.Destroyed)
		{
			_mote = Make();
		}

		_mote.Maintain();
	}

	private Mote Make()
	{
		var mote = (Mote)ThingMaker.MakeThing(Props.moteDef);
		switch (mote)
		{
			case MoteDualAttached dual:
				dual.Attach(Pawn, TargetInfo.Invalid);
				break;
			case MoteAttached attached:
				attached.Attach(Pawn);
				break;
		}

		mote.Scale = Props.scale;
		mote.instanceColor = Props.color;
		mote.exactPosition = Pawn.DrawPos;
		GenSpawn.Spawn(mote, Pawn.Position, Pawn.Map);
		LastMote = mote;
		return mote;
	}
}
