using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace Ordo.BridgeTools;

// Which running mod a tool acts on. Tools take a mod by packageId, name, folder or assembly name; with none given they use the
// only running mod of ours (packageId starting "Ordo.") that has code, which is the one under test in a GABS profile.
public static class ModTarget
{
	public const string OurPrefix = "ordo.";

	public const string ParameterDescription = "The mod, by packageId, name, folder or assembly name. Leave empty when only one Ordo mod with code is loaded.";

	public static (ModContentPack Mod, string Error) Find(
		string name
	)
	{
		var withCode = LoadedModManager.RunningModsListForReading.Where(mod => mod.assemblies.loadedAssemblies.Count > 0).ToList();
		if (string.IsNullOrWhiteSpace(name))
		{
			var ours = withCode.Where(mod => mod.PackageId.StartsWith(OurPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
			return ours.Count == 1
				? (ours[0], null)
				: (null, $"Pass mod: {(ours.Count == 0 ? "no Ordo mod with code is loaded" : $"more than one is loaded ({string.Join(", ", ours.Select(mod => mod.PackageId))})")}");
		}

		var match = withCode.FirstOrDefault(mod => Matches(mod, name));
		return match != null
			? (match, null)
			: (null, $"No running mod with code is called {name}. Loaded: {string.Join(", ", withCode.Select(mod => mod.PackageId))}");
	}

	// The mod's own assembly: the one named after its folder or packageId, else its only one
	public static Assembly MainAssembly(
		ModContentPack mod
	)
	{
		var assemblies = mod.assemblies.loadedAssemblies;
		var names = ModNames(mod).ToList();
		return assemblies.FirstOrDefault(assembly => names.Contains(assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)) ?? assemblies.Last();
	}

	public static (Assembly Assembly, string Error) FindAssembly(
		string name
	)
	{
		var (mod, error) = Find(name);
		return mod == null ? (null, error) : (MainAssembly(mod), null);
	}

	// Whether name is one of the mod's names, as Find takes them
	public static bool Matches(
		ModContentPack mod,
		string name
	)
	{
		return Names(mod).Any(candidate => string.Equals(candidate, name.Trim(), StringComparison.OrdinalIgnoreCase));
	}

	// Lists kept per mod, such as scenarios or showcase clips: those for running mods, or only for the named one
	public static (List<(ModContentPack Mod, List<T> Items)> Sets, string Error) Running<T>(
		IEnumerable<(string PackageId, Func<List<T>> Items)> sets,
		string name
	)
	{
		var running = sets
			.Select(set => (Mod: LoadedModManager.RunningModsListForReading.FirstOrDefault(mod => string.Equals(mod.PackageId, set.PackageId, StringComparison.OrdinalIgnoreCase)), set.Items))
			.Where(set => set.Mod != null)
			.Where(set => string.IsNullOrWhiteSpace(name) || Matches(set.Mod, name))
			.Select(set => (set.Mod, set.Items()))
			.ToList();
		return running.Count > 0
			? (running, null)
			: (running, string.IsNullOrWhiteSpace(name) ? "None of the mods with sets here is running" : $"No running mod called {name} has a set here");
	}

	private static IEnumerable<string> Names(
		ModContentPack mod
	)
	{
		return ModNames(mod).Concat(mod.assemblies.loadedAssemblies.Select(assembly => assembly.GetName().Name));
	}

	private static IEnumerable<string> ModNames(
		ModContentPack mod
	)
	{
		yield return mod.PackageId;
		yield return mod.Name;
		yield return mod.FolderName;
		var dot = mod.PackageId.LastIndexOf('.');
		if (dot >= 0)
		{
			yield return mod.PackageId.Substring(dot + 1);
		}
	}
}
