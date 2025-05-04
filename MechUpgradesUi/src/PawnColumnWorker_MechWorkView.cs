using RimWorld;
using Verse;

namespace Ordo.MechUpgradesUi;

public class PawnColumnWorker_MechWorkView : PawnColumnWorker_Text {

    /// <inheritdoc />
    protected override string GetTextFor(Pawn pawn)
    {
        return pawn.jobs.curDriver?.GetReport() ?? "";
    }

    /// <inheritdoc />
    protected override string GetTip(Pawn pawn)
    {
        return GetTextFor(pawn);
    }
}