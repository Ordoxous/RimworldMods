using System.Globalization;
using UnityEngine;
using Verse;
using XmlExtensions;

namespace Ordo.Tweaks;

/// <summary>Reads the settings declared in Defs/TweaksSettings.xml, for features implemented in C#.</summary>
internal static class TweaksSettings
{
    private const string ModId = "Ordo.Tweaks";

    public static bool ArmorIndicators => GetBool("ArmorIndicators", true);
    public static bool DeteriorationAlertSkipTainted => GetBool("DeteriorationAlertSkipTainted", true);
    public static bool HaulDownedMechs => GetBool("HaulDownedMechs", true);
    public static bool HaulUnpoweredMechs => GetBool("HaulUnpoweredMechs", true);
    public static bool LimitHemogenFarming => GetBool("LimitHemogenFarming", true);
    public static int HemogenPackCap => Mathf.RoundToInt(GetFloat("HemogenPackCap", 30f));
    public static bool ToolsCategory => GetBool("ToolsCategory", true);

    // Off without MultiFloors, so nothing reaches its types
    public static bool ShutdownZonesAcrossLevels => MultiFloorsActive && GetBool("ShutdownZonesAcrossLevels", true);
    public static bool ChargersAcrossLevels => MultiFloorsActive && GetBool("ChargersAcrossLevels", true);
    public static bool ResourcesAcrossLevels => MultiFloorsActive && GetBool("ResourcesAcrossLevels", true);
    public static bool ReadoutAcrossLevels => MultiFloorsActive && GetBool("ReadoutAcrossLevels", true);
    public static bool BillsAcrossLevels => MultiFloorsActive && GetBool("BillsAcrossLevels", true);

    // Off without Simple Sidearms, which carries the tools, so nothing reaches its types
    public static bool ToolsSwitchForWork => ToolsForWork.SimpleSidearmsActive && GetBool("ToolsSwitchForWork", true);
    // Also off with Simple Sidearms' own tool autoswitch off: fetched tools would never come out
    public static bool ToolsFetch => ToolsForWork.SimpleSidearmsActive && GetBool("ToolsFetch", true) && ToolsForWork.SidearmsToolAutoSwitch();
    public static int ToolsPerPawn => Mathf.Clamp(Mathf.RoundToInt(GetFloat("ToolsPerPawn", 2f)), 1, 4);
    public static int ToolsMainWorkPriority => Mathf.Clamp(Mathf.RoundToInt(GetFloat("ToolsMainWorkPriority", 2f)), 1, 9);

    private static readonly bool MultiFloorsActive = ModsConfig.IsActive("telardo.MultiFloors");

    private static bool GetBool(string key, bool defaultValue) =>
        SettingsManager.TryGetSetting(ModId, key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static float GetFloat(string key, float defaultValue) =>
        SettingsManager.TryGetSetting(ModId, key, out var value) && TryParseFloat(value, out var parsed)
            ? parsed
            : defaultValue;

    // XmlExtensions may have saved the value in the player's locale ("30,5"), so fall back to it
    private static bool TryParseFloat(string value, out float parsed) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
        || float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed);
}
