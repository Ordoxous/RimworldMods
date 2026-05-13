using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

public class PawnColumnWorker_MechWorkView : PawnColumnWorker_Text {

    /// <inheritdoc />
    public override string GetTextFor(Pawn pawn)
    {
        return pawn.jobs.curDriver?.GetReport() ?? "";
    }

    /// <inheritdoc />
    public override string GetTip(Pawn pawn)
    {
        return GetTextFor(pawn);
    }
}