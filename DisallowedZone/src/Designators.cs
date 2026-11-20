using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.DisallowedZone;

[StaticConstructorOnStartup]
public static class Textures
{
    public static readonly Texture2D Expand = ContentFinder<Texture2D>.Get("UI/Designators/AreaAllowedExpand");
    public static readonly Texture2D Clear = ContentFinder<Texture2D>.Get("UI/Designators/AreaAllowedClear");
    public static readonly Texture2D Toggle = ContentFinder<Texture2D>.Get("UI/Designators/ForbidOn");
}

public abstract class Designator_DisallowedZone : Designator_Cells
{
    public override bool DragDrawMeasurements => true;

    public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.Areas;

    protected DisallowedZone Zone => DisallowedZone.For(Map);

    protected Designator_DisallowedZone()
    {
        defaultIconColor = DisallowedZone.ZoneColor;
        useMouseIcon = true;
    }

    public override void SelectedUpdate()
    {
        GenUI.RenderMouseoverBracket();
        Zone?.MarkForDraw();
    }
}

public class Designator_DisallowedZoneExpand : Designator_DisallowedZone
{
    public Designator_DisallowedZoneExpand()
    {
        defaultLabel = "Ordo_DisallowedZone_Expand".Translate();
        defaultDesc = "Ordo_DisallowedZone_ExpandDesc".Translate();
        icon = Textures.Expand;
        soundDragSustain = SoundDefOf.Designate_DragAreaAdd;
        soundDragChanged = SoundDefOf.Designate_DragZone_Changed;
        soundSucceeded = SoundDefOf.Designate_ZoneAdd_AllowedArea;
    }

    public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Map) && Zone != null && !Zone[c];

    public override void DesignateSingleCell(IntVec3 c) => Zone.Set(c, true);

    public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
    {
        base.DesignateMultiCell(cells);
        // Growing a zone that's on is like shrinking a pawn's area: stop work that's now inside it.
        Zone?.InterruptJobsInside();
    }
}

public class Designator_DisallowedZoneClear : Designator_DisallowedZone
{
    public Designator_DisallowedZoneClear()
    {
        defaultLabel = "Ordo_DisallowedZone_Clear".Translate();
        defaultDesc = "Ordo_DisallowedZone_ClearDesc".Translate();
        icon = Textures.Clear;
        soundDragSustain = SoundDefOf.Designate_DragAreaDelete;
        soundDragChanged = null;
        soundSucceeded = SoundDefOf.Designate_ZoneDelete;
    }

    public override AcceptanceReport CanDesignateCell(IntVec3 c) => c.InBounds(Map) && Zone != null && Zone[c];

    public override void DesignateSingleCell(IntVec3 c) => Zone.Set(c, false);
}

/// <summary>A button in the Zone tab that switches the zone on or off instead of being selected.</summary>
public class Designator_DisallowedZoneToggle : Designator
{
    public Designator_DisallowedZoneToggle()
    {
        defaultDesc = "Ordo_DisallowedZone_ToggleDesc".Translate();
        icon = Textures.Toggle;
    }

    private DisallowedZone Zone => DisallowedZone.For(Find.CurrentMap);

    public override string Label =>
        (Zone?.Active == true ? "Ordo_DisallowedZone_TurnOff" : "Ordo_DisallowedZone_TurnOn").Translate();

    public override Color IconDrawColor => Zone?.Active == true ? DisallowedZone.ZoneColor : Color.white;

    public override void ProcessInput(Event ev)
    {
        if (!CheckCanInteract())
            return;
        Zone?.Toggle();
    }

    public override AcceptanceReport CanDesignateCell(IntVec3 c) => false;
}

public class Alert_DisallowedZoneActive : Alert
{
    public Alert_DisallowedZoneActive()
    {
        defaultLabel = "Ordo_DisallowedZone_Alert".Translate();
        defaultPriority = AlertPriority.Medium;
    }

    public override TaggedString GetExplanation()
    {
        var maps = new List<string>();
        foreach (var zone in DisallowedZone.ActiveZones)
            maps.Add(zone.map.Parent?.LabelCap ?? zone.map.ToString());
        return "Ordo_DisallowedZone_AlertDesc".Translate(maps.ToCommaList());
    }

    public override AlertReport GetReport() => DisallowedZone.ActiveZones.Count > 0;

    public override void OnClick()
    {
        // Jump to the first map with a zone on.
        var zone = DisallowedZone.ActiveZones.Count > 0 ? DisallowedZone.ActiveZones[0] : null;
        if (zone != null && Find.CurrentMap != zone.map)
            CameraJumper.TryJump(zone.map.Center, zone.map);
    }
}
