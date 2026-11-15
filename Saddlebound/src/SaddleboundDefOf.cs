using RimWorld;

namespace Ordo.Saddlebound;

[DefOf]
public static class SaddleboundDefOf
{
    public static MemeDef Ordo_HordeRiders;

    // Fired with the rider as doer each time a pawn mounts. Riding: Disliked turns it into a memory.
    public static HistoryEventDef Ordo_Mounted;

    public static PreceptDef Ordo_Riding_Respected;
    public static PreceptDef Ordo_Riding_Essential;
    public static PreceptDef Ordo_PreferredMount;

    // Fired alongside Ordo_Mounted when the rider's ideoligion has preferred mounts: the mount is one of them
    public static HistoryEventDef Ordo_RodePreferredMount;

    // The mount isn't one of them, and the ideoligion holds riding essential
    public static HistoryEventDef Ordo_RodeLesserMount;

    // A ridden player mount died. No doer, so every free colonist hears of it.
    public static HistoryEventDef Ordo_MountDied;

    // The same death, with the mount's usual rider as doer, for their own stronger memory
    public static HistoryEventDef Ordo_OwnMountDied;

    // A mounted pawn killed an enemy, with the killer as doer
    public static HistoryEventDef Ordo_KilledFromSaddle;

    static SaddleboundDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SaddleboundDefOf));
}
