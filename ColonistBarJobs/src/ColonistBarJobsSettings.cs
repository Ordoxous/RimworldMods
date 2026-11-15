using UnityEngine;
using Verse;

namespace Ordo.ColonistBarJobs;

public enum IconCorner
{
    TopRight,
    TopLeft,
    BottomRight,
    BottomLeft
}

// Where the icon sits on a portrait and how it looks. Read on every draw, so changes apply at once.
public class ColonistBarJobsSettings : ModSettings
{
    public const float DefaultSize = 0.3f;
    public const float DefaultBackdrop = 0.75f;

    // Offsets are in pixels at the vanilla portrait width, and scale with the bar
    public const float VanillaPortraitWidth = 48f;
    public const float MaxOffset = 16f;

    public IconCorner Corner = IconCorner.TopRight;

    // The icon's share of the portrait's width
    public float Size = DefaultSize;

    // Away from the corner's edges: positive moves it inward, towards the portrait's middle
    public float OffsetX;
    public float OffsetY;

    // How dark the square behind the icon is: 0 for none
    public float BackdropOpacity = DefaultBackdrop;

    public override void ExposeData()
    {
        // Kept explicit so TopRight stays the default if the enum is reordered
        // ReSharper disable once RedundantArgumentDefaultValue
        Scribe_Values.Look(ref Corner, "corner", IconCorner.TopRight);
        Scribe_Values.Look(ref Size, "size", DefaultSize);
        Scribe_Values.Look(ref OffsetX, "offsetX");
        Scribe_Values.Look(ref OffsetY, "offsetY");
        Scribe_Values.Look(ref BackdropOpacity, "backdropOpacity", DefaultBackdrop);
    }

    public void Reset()
    {
        Corner = IconCorner.TopRight;
        Size = DefaultSize;
        OffsetX = 0f;
        OffsetY = 0f;
        BackdropOpacity = DefaultBackdrop;
    }

    // The rect the icon takes in a portrait's rect
    public Rect IconRect(
        Rect portrait
    )
    {
        var size = Mathf.Round(portrait.width * Size);
        var scale = portrait.width / VanillaPortraitWidth;
        var inX = 1f + OffsetX * scale;
        var inY = 1f + OffsetY * scale;
        var x = Corner is IconCorner.TopLeft or IconCorner.BottomLeft ? portrait.x + inX : portrait.xMax - size - inX;
        var y = Corner is IconCorner.TopLeft or IconCorner.TopRight ? portrait.y + inY : portrait.yMax - size - inY;
        return new Rect(Mathf.Round(x), Mathf.Round(y), size, size);
    }
}

public class ColonistBarJobsMod : Mod
{
    public static ColonistBarJobsSettings Settings { get; private set; } = new();

    public ColonistBarJobsMod(
        ModContentPack content
    ) : base(content)
    {
        Settings = GetSettings<ColonistBarJobsSettings>();
    }

    public override string SettingsCategory() => "Ordo_ColonistBarJobs_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(
        Rect inRect
    )
    {
        var settings = Settings;
        var listing = new Listing_Standard();
        listing.Begin(inRect.LeftPart(0.6f));

        listing.Label("Ordo_ColonistBarJobs_Corner".Translate());
        foreach (IconCorner corner in System.Enum.GetValues(typeof(IconCorner)))
        {
            if (listing.RadioButton(("Ordo_ColonistBarJobs_Corner_" + corner).Translate(), settings.Corner == corner))
            {
                settings.Corner = corner;
            }
        }

        listing.Gap();
        settings.Size = listing.SliderLabeled(
            "Ordo_ColonistBarJobs_Size".Translate(settings.Size.ToStringPercent()), settings.Size, 0.15f, 0.5f,
            tooltip: "Ordo_ColonistBarJobs_SizeTip".Translate());
        settings.OffsetX = Mathf.Round(listing.SliderLabeled(
            "Ordo_ColonistBarJobs_OffsetX".Translate(settings.OffsetX.ToString("0")), settings.OffsetX,
            -ColonistBarJobsSettings.MaxOffset, ColonistBarJobsSettings.MaxOffset,
            tooltip: "Ordo_ColonistBarJobs_OffsetTip".Translate()));
        settings.OffsetY = Mathf.Round(listing.SliderLabeled(
            "Ordo_ColonistBarJobs_OffsetY".Translate(settings.OffsetY.ToString("0")), settings.OffsetY,
            -ColonistBarJobsSettings.MaxOffset, ColonistBarJobsSettings.MaxOffset,
            tooltip: "Ordo_ColonistBarJobs_OffsetTip".Translate()));
        settings.BackdropOpacity = listing.SliderLabeled(
            "Ordo_ColonistBarJobs_Backdrop".Translate(settings.BackdropOpacity.ToStringPercent()), settings.BackdropOpacity, 0f, 1f);

        listing.Gap();
        if (listing.ButtonText("Ordo_ColonistBarJobs_Reset".Translate()))
        {
            settings.Reset();
        }

        listing.End();

        DrawPreview(inRect.RightPart(0.35f).TopPartPixels(140f), settings);
    }

    // A portrait-sized box with the icon where the settings put it
    private static void DrawPreview(
        Rect area,
        ColonistBarJobsSettings settings
    )
    {
        Widgets.Label(area.TopPartPixels(24f), "Ordo_ColonistBarJobs_Preview".Translate());
        var portrait = new Rect(area.x + 8f, area.y + 32f, 96f, 96f);
        Widgets.DrawBoxSolid(portrait, new Color(0.17f, 0.22f, 0.27f));
        Widgets.DrawBox(portrait);
        var icon = new JobIcon(ContentFinder<Texture2D>.Get("UI/Designators/Mine", reportFailure: false), null, null, null);
        if (!icon.IsEmpty)
        {
            ColonistBarJobIcons.Draw(settings.IconRect(portrait), icon);
        }
    }
}
