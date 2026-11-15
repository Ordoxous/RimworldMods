using System.Linq;
using RimBridgeServer.Sdk;
using UnityEngine;
using Verse;

namespace Ordo.BridgeTools;

// The game's textures are packed, so the files can't be listed on disk; this asks the game what a folder holds
public sealed class ContentTools
{
    [Tool("ordo/list_textures", Description = "List the textures the game can load from a folder, such as UI/Icons or UI/Designators, with their names and sizes, for picking texPaths. Includes textures from loaded mods.")]
    public static object ListTextures(
        [ToolParameter(Description = "Folder path as texPaths use it, without a trailing slash, e.g. UI/Icons/ColonistBar")] string folder,
        [ToolParameter(Description = "Only names containing this (case-insensitive)")] string filter = null
    )
    {
        var textures = ContentFinder<Texture2D>.GetAllInFolder(folder)
            .Where(texture => texture != null && (filter == null || texture.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0))
            .Select(texture => new { texture.name, texture.width, texture.height })
            .OrderBy(texture => texture.name, System.StringComparer.Ordinal)
            .ToList();
        return new { success = true, folder, count = textures.Count, textures };
    }

    [Tool("ordo/textures_exist", Description = "Check which exact texture paths the game can load, such as UI/Icons/Joy. Separate paths with commas.")]
    public static object TexturesExist(
        [ToolParameter(Description = "Texture paths, comma separated")] string paths
    )
    {
        var results = paths.Split(',')
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Select(path => new { path, exists = ContentFinder<Texture2D>.Get(path, reportFailure: false) != null })
            .ToList();
        return new { success = true, results };
    }
}
