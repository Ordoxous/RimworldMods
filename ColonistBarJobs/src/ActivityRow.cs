using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.ColonistBarJobs;

// Puts the job icon first in the row of activity icons at a portrait's bottom left. Vanilla fills the row in DrawIcons:
// at most one activity (forming caravan, mental break, medical rest, sleeping, fleeing, attacking, idle), then burning,
// inspired, and the slave, ideology role or faction icon. While the job icon shows, the calm activities it stands in for
// (idle, sleeping, medical rest) leave the row; the urgent ones stay. Sleeping and resting in a medical bed show no job
// icon (Defs/JobIcons.xml), so those keep vanilla's icon.
//
// The job icon takes a slot as a clear placeholder entry, so the row sizes and spaces itself as usual, and is drawn
// over that slot. CM Color Coded Mood Bar replaces DrawColonist with its own copy, with its own icon list built every few
// hundred milliseconds, so it gets the same treatment through its own methods.
[StaticConstructorOnStartup]
public static class ActivityRow
{
    // Recognised by reference, so a clear texture of our own
    public static readonly Texture2D Placeholder = SolidColorMaterials.NewSolidColorTexture(Color.clear);

    // The vanilla row's sizing: each icon is the row's width over the icon count, at most 20 px, scaled with the bar
    private const float BaseIconAreaWidth = 46f;
    private const float BaseIconMaxSize = 20f;

    private static readonly HashSet<Texture2D> Calm = new();

    private static readonly Type VanillaCall = AccessTools.Inner(typeof(ColonistBarColonistDrawer), "IconDrawCall");
    private static readonly IList VanillaIcons =
        (IList)AccessTools.Field(typeof(ColonistBarColonistDrawer), "tmpIconsToDraw").GetValue(null);

    private static Type _moodBarCall;
    private static AccessTools.FieldRef<object, IList> _moodBarIcons;
    private static AccessTools.FieldRef<object, float> _moodBarScale;
    private static AccessTools.FieldRef<object, int> _moodBarPawnId;
    private static readonly Dictionary<int, Pawn> MoodBarPawns = new();

    // Whether every drawer in use has the row hooked; if not, the icon goes to its corner instead
    public static bool Hooked { get; private set; }

    static ActivityRow()
    {
        foreach (var name in new[] { "Icon_Idle", "Icon_Sleeping", "Icon_MedicalRest" })
        {
            if (AccessTools.Field(typeof(ColonistBarColonistDrawer), name)?.GetValue(null) is Texture2D texture)
            {
                Calm.Add(texture);
            }
        }

        var harmony = new Harmony(typeof(ActivityRow).FullName);
        harmony.Patch(
            AccessTools.Method(typeof(ColonistBarColonistDrawer), "DrawIcons"),
            transpiler: new HarmonyMethod(typeof(ActivityRow), nameof(Transpile_DrawIcons))
        );
        Hooked = _vanillaHooked && HookMoodBar(harmony);
    }

    private static bool _vanillaHooked;

    // Calls Arrange(rect, colonist) once the row is filled, just before vanilla works out the icon size
    private static IEnumerable<CodeInstruction> Transpile_DrawIcons(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var areaWidth = AccessTools.Field(typeof(ColonistBarColonistDrawer), "BaseIconAreaWidth");
        foreach (var instruction in instructions)
        {
            if (!_vanillaHooked && instruction.LoadsField(areaWidth))
            {
                _vanillaHooked = true;
                yield return new CodeInstruction(OpCodes.Ldarg_1).MoveLabelsFrom(instruction);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return CodeInstruction.Call(typeof(ActivityRow), nameof(ArrangeVanilla));
            }

            yield return instruction;
        }

        if (!_vanillaHooked)
        {
            Log.Warning("[ColonistBarJobs] Couldn't find where DrawIcons sizes its icons; job icons go in the corner instead.");
        }
    }

    private static void ArrangeVanilla(
        Rect rect,
        Pawn colonist
    )
    {
        if (ColonistBarJobsMod.Settings.Placement != IconPlacement.ActivityRow)
        {
            return;
        }

        var icon = ColonistBarJobIcons.IconFor(colonist);
        if (Rearrange(VanillaIcons, VanillaCall, !icon.IsEmpty))
        {
            ColonistBarJobIcons.DrawWithTooltip(SlotRect(rect, VanillaIcons.Count, Find.ColonistBar.Scale), icon, colonist);
        }
    }

    // Takes the calm activities out and puts the placeholder first, when there's a job icon; true when the placeholder is in
    public static bool Rearrange(
        IList icons,
        Type callType,
        bool hasJobIcon
    )
    {
        var texture = AccessTools.Field(callType, "texture");
        for (var i = icons.Count - 1; i >= 0; i--)
        {
            var entry = (Texture2D)texture.GetValue(icons[i]);
            if (entry == Placeholder || hasJobIcon && Calm.Contains(entry))
            {
                icons.RemoveAt(i);
            }
        }

        if (!hasJobIcon)
        {
            return false;
        }

        icons.Insert(0, Activator.CreateInstance(callType, Placeholder, null, null));
        return true;
    }

    // The row's first slot, sized as vanilla sizes a row of `count` icons
    public static Rect SlotRect(
        Rect portrait,
        int count,
        float scale
    )
    {
        var size = Mathf.Min(BaseIconAreaWidth / Mathf.Max(count, 1), BaseIconMaxSize) * scale;
        return new Rect(portrait.x + 1f, portrait.yMax - size - 1f, size, size);
    }

    public static bool IsPlaceholderFirst(
        IList icons,
        Type callType
    ) => icons.Count > 0 && (Texture2D)AccessTools.Field(callType, "texture").GetValue(icons[0]) == Placeholder;

    // CM Color Coded Mood Bar: true when it isn't loaded, or its icon list and drawing are both hooked
    private static bool HookMoodBar(
        Harmony harmony
    )
    {
        var cache = AccessTools.TypeByName("ColoredMoodBar13.MoodCache");
        if (cache == null)
        {
            return true;
        }

        var build = AccessTools.Method(cache, "DoIconVars");
        var draw = AccessTools.Method("ColoredMoodBar13.MoodPatch:DrawIcons");
        _moodBarCall = AccessTools.TypeByName("ColoredMoodBar13.IconDrawCall");
        if (build == null || draw == null || _moodBarCall == null)
        {
            Log.Warning("[ColonistBarJobs] CM Color Coded Mood Bar has changed; job icons go in the corner instead of its icon row.");
            return false;
        }

        _moodBarIcons = AccessTools.FieldRefAccess<object, IList>(AccessTools.Field(cache, "IconsToDraw"));
        _moodBarScale = AccessTools.FieldRefAccess<object, float>(AccessTools.Field(cache, "Scale"));
        _moodBarPawnId = AccessTools.FieldRefAccess<object, int>(AccessTools.Field(cache, "PawnId"));
        harmony.Patch(build, postfix: new HarmonyMethod(typeof(ActivityRow), nameof(Postfix_MoodBarBuild)));
        harmony.Patch(draw, postfix: new HarmonyMethod(typeof(ActivityRow), nameof(Postfix_MoodBarDraw)));
        return true;
    }

    private static void Postfix_MoodBarBuild(
        object __instance,
        Pawn colonist
    )
    {
        if (ColonistBarJobsMod.Settings.Placement != IconPlacement.ActivityRow || colonist.Dead)
        {
            return;
        }

        MoodBarPawns[_moodBarPawnId(__instance)] = colonist;
        Rearrange(_moodBarIcons(__instance), _moodBarCall, !ColonistBarJobIcons.IconFor(colonist).IsEmpty);
    }

    // Draws over the placeholder. Between the mood bar's rebuilds the job can change, so the icon is looked up again and
    // the slot left empty if there's no icon any more.
    private static void Postfix_MoodBarDraw(
        ref Rect rect,
        object cache
    )
    {
        if (ColonistBarJobsMod.Settings.Placement != IconPlacement.ActivityRow)
        {
            return;
        }

        var icons = _moodBarIcons(cache);
        if (!IsPlaceholderFirst(icons, _moodBarCall) || !MoodBarPawns.TryGetValue(_moodBarPawnId(cache), out var colonist))
        {
            return;
        }

        var icon = ColonistBarJobIcons.IconFor(colonist);
        if (!icon.IsEmpty)
        {
            ColonistBarJobIcons.DrawWithTooltip(SlotRect(rect, icons.Count, _moodBarScale(cache)), icon, colonist);
        }
    }
}
