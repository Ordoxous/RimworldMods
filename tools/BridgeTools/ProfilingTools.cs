using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimBridgeServer.Sdk;

namespace Ordo.BridgeTools;

public sealed class ProfilingTools
{
	private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

	[Tool("ordo/profiling_targets", Description = "List every method a mod defines as Type:Method targets, ready for rimworld/dpa_patch_methods with inputMode Method or a Dubs Performance Analyzer Analyzer.xml. Skips constructors, property accessors, abstract methods and compiler-generated code.")]
	public static object ProfilingTargets(
		[ToolParameter(Description = ModTarget.ParameterDescription)] string mod = null
	)
	{
		var (assembly, error) = ModTarget.FindAssembly(mod);
		if (assembly == null)
		{
			return new { success = false, message = error };
		}

		var targets = Targets(assembly);
		return new { success = true, mod = assembly.GetName().Name, count = targets.Count, targets = string.Join(";", targets), list = targets };
	}

	// Each method by name, because DPA's Type input mode only finds methods in a few of a mod's types
	public static List<string> Targets(
		Assembly assembly
	)
	{
		return assembly.GetTypes()
			.Where(type => !type.IsInterface && !type.IsDefined(typeof(CompilerGeneratedAttribute)) && !type.ContainsGenericParameters)
			.SelectMany(type => type.GetMethods(Declared))
			.Where(method => !method.IsAbstract && !method.IsSpecialName && !method.ContainsGenericParameters && !method.Name.Contains('<') && method.GetMethodBody() != null)
			.Select(method => $"{method.DeclaringType!.FullName}:{method.Name}")
			.Distinct()
			.OrderBy(target => target)
			.ToList();
	}
}
