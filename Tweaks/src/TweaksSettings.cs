using UnityEngine;
using XmlExtensions;

namespace Ordo.Tweaks;

/// <summary>Reads the settings declared in Defs/TweaksSettings.xml, for features implemented in C#.</summary>
internal static class TweaksSettings
{
    private const string ModId = "Ordo.Tweaks";

    public static bool ArmorIndicators => GetBool("ArmorIndicators", true);
    public static bool HaulDownedMechs => GetBool("HaulDownedMechs", true);
    public static bool LimitHemogenFarming => GetBool("LimitHemogenFarming", true);
    public static int HemogenPackCap => Mathf.RoundToInt(GetFloat("HemogenPackCap", 30f));
    public static bool ToolsCategory => GetBool("ToolsCategory", true);

    private static bool GetBool(string key, bool defaultValue) =>
        SettingsManager.TryGetSetting(ModId, key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static float GetFloat(string key, float defaultValue) =>
        SettingsManager.TryGetSetting(ModId, key, out var value) && float.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;
}
