using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Ordo.HotReloadDelta;

// Makes a .NET hot reload delta for a running game: the part dotnet watch does on the build side. It compiles the source the
// loaded DLL was built from and the source as it is now, with the same compiler arguments, diffs their methods, and has Roslyn
// emit the metadata, IL and PDB deltas that MetadataUpdater.ApplyUpdate takes (ordo/apply_update in the game).
//
// Usage: HotReloadDelta <csc arguments file> <project dir> <loaded dll> <output dir> <snapshot dir>...
// The arguments file holds the csc command line from a build of the project. Each snapshot dir mirrors the project dir: the
// first as it was when the loaded DLL was built, then one for each delta applied since, in order. Deltas chain, so this replays
// the earlier ones to get the baseline for the next. Handles changed method bodies, new methods, new fields and new types;
// anything else (removals, signature changes, new constructors or virtual methods on existing types) is reported and needs a
// restart.
public static class Program
{
	private static readonly Guid EncLocalSlotMap = new("755F52A8-91C5-45BE-B4B8-209571E552BD");

	private static readonly Guid EncLambdaAndClosureMap = new("A643004C-0240-496F-A783-30D64F4979DE");

	private static readonly Guid EncStateMachineStateMap = new("8B78CD68-2EDE-420B-980B-E15884B8AAA3");

	public static int Main(
		string[] args
	)
	{
		if (args.Length < 5)
		{
			Console.Error.WriteLine("Usage: HotReloadDelta <csc arguments file> <project dir> <loaded dll> <output dir> <snapshot dir>...");
			return 2;
		}

		var (argumentsFile, projectDir, loadedDll, outputDir) = (args[0], Path.GetFullPath(args[1]), args[2], args[3]);
		var snapshots = args.Skip(4).Select(Path.GetFullPath).ToList();

		var parsed = CSharpCommandLineParser.Default.Parse(
			CommandLineParser.SplitCommandLineIntoArguments(File.ReadAllText(argumentsFile), removeHashComments: false),
			projectDir,
			sdkDirectory: null
		);
		if (parsed.Errors.Any(error => error.Severity == DiagnosticSeverity.Error))
		{
			parsed.Errors.ToList().ForEach(error => Console.Error.WriteLine(error));
			return 1;
		}

		var references = parsed.MetadataReferences.Select(reference => (MetadataReference)MetadataReference.CreateFromFile(reference.Reference, reference.Properties)).ToList();
		var name = Path.GetFileNameWithoutExtension(parsed.OutputFileName);

		// The source at each step: the snapshots in order, then the project as it is now
		var versions = snapshots
			.Select(snapshot => Compile(name, parsed, references, path => SnapshotPath(path, projectDir, snapshot)))
			.Append(Compile(name, parsed, references, path => path))
			.ToList();

		var image = File.ReadAllBytes(loadedDll).ToImmutableArray();
		using var peReader = new PEReader(image);
		var embedded = peReader.ReadDebugDirectory().First(entry => entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
		using var pdbProvider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
		var pdb = pdbProvider.GetMetadataReader();
		var metadata = peReader.GetMetadataReader();

		var baseline = EmitBaseline.CreateInitialBaseline(
			versions[0],
			ModuleMetadata.CreateFromImage(image),
			handle => EditAndContinueMethodDebugInformation.Create(
				CustomDebugInfo(pdb, handle, EncLocalSlotMap),
				CustomDebugInfo(pdb, handle, EncLambdaAndClosureMap),
				CustomDebugInfo(pdb, handle, EncStateMachineStateMap)
			),
			handle =>
			{
				var rva = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
				return rva == 0 ? default : peReader.GetMethodBody(rva).LocalSignature;
			},
			hasPortableDebugInformation: true
		);

		// Replays the deltas already applied, to reach the baseline the game is at now
		for (var step = 1; step < versions.Count - 1; step++)
		{
			var replayed = Emit(versions[step - 1], versions[step], baseline, null, out _);
			if (replayed == null)
			{
				Console.Error.WriteLine($"couldn't replay snapshot {step}");
				return 1;
			}

			baseline = replayed;
		}

		var result = Emit(versions[^2], versions[^1], baseline, outputDir, out var exitCode);
		return result == null ? exitCode : 0;
	}

	// The baseline after the delta from oldVersion to newVersion, or null if it failed. Writes the delta when outputDir is set.
	private static EmitBaseline Emit(
		Compilation oldVersion,
		Compilation newVersion,
		EmitBaseline baseline,
		string outputDir,
		out int exitCode
	)
	{
		exitCode = 0;
		var edits = Edits(oldVersion, newVersion, out var unsupported);
		foreach (var problem in unsupported)
		{
			Console.WriteLine($"needs a restart: {problem}");
		}

		if (unsupported.Count > 0)
		{
			exitCode = 3;
			return null;
		}

		if (edits.Count == 0 && outputDir != null)
		{
			Console.WriteLine("no changes");
			exitCode = 4;
			return null;
		}

		var writing = outputDir != null;
		if (writing)
		{
			Directory.CreateDirectory(outputDir);
		}

		using var metadataStream = writing ? File.Create(Path.Combine(outputDir, "delta.meta")) : (Stream)new MemoryStream();
		using var ilStream = writing ? File.Create(Path.Combine(outputDir, "delta.il")) : (Stream)new MemoryStream();
		using var pdbStream = writing ? File.Create(Path.Combine(outputDir, "delta.pdb")) : (Stream)new MemoryStream();
		var added = edits.Where(edit => edit.Kind == SemanticEditKind.Insert).Select(edit => edit.NewSymbol).ToHashSet(SymbolEqualityComparer.Default);
		var result = newVersion.EmitDifference(baseline, edits, symbol => added.Contains(symbol), metadataStream, ilStream, pdbStream);

		foreach (var diagnostic in result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
		{
			Console.Error.WriteLine(diagnostic);
		}

		if (!result.Success)
		{
			exitCode = 1;
			return null;
		}

		if (writing)
		{
			foreach (var edit in edits)
			{
				Console.WriteLine($"{edit.Kind}: {edit.NewSymbol.ToDisplayString()}");
			}
		}

		return result.Baseline;
	}

	private static CSharpCompilation Compile(
		string name,
		CSharpCommandLineArguments parsed,
		IEnumerable<MetadataReference> references,
		Func<string, string> sourcePath
	)
	{
		var trees = parsed.SourceFiles
			.Select(file => (file.Path, Read: sourcePath(file.Path)))
			.Where(file => File.Exists(file.Read))
			.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file.Read), parsed.ParseOptions, file.Path, Encoding.UTF8));
		return CSharpCompilation.Create(name, trees, references, parsed.CompilationOptions);
	}

	// Files outside the project, and generated ones under obj, are the same in every version
	private static string SnapshotPath(
		string path,
		string projectDir,
		string snapshotDir
	)
	{
		var relative = Path.GetRelativePath(projectDir, path);
		if (relative.StartsWith("..") || relative.StartsWith("obj"))
		{
			return path;
		}

		return Path.Combine(snapshotDir, relative);
	}

	private static List<SemanticEdit> Edits(
		Compilation oldCompilation,
		Compilation newCompilation,
		out List<string> unsupported
	)
	{
		unsupported = new List<string>();
		var edits = new List<SemanticEdit>();
		var oldMembers = SourceMethods(oldCompilation).ToDictionary(method => DocumentationCommentId.CreateDeclarationId(method));
		var newMembers = SourceMethods(newCompilation).ToDictionary(method => DocumentationCommentId.CreateDeclarationId(method));

		foreach (var (id, method) in newMembers)
		{
			if (!oldMembers.TryGetValue(id, out var old))
			{
				if (method.MethodKind == MethodKind.Ordinary && !method.IsVirtual && !method.IsOverride && !method.IsAbstract)
				{
					edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, method));
				}
				else
				{
					unsupported.Add($"new {method.MethodKind} {method.ToDisplayString()}");
				}

				continue;
			}

			if (Body(old) != Body(method))
			{
				edits.Add(new SemanticEdit(SemanticEditKind.Update, old, method));
			}
		}

		foreach (var id in oldMembers.Keys.Where(id => !newMembers.ContainsKey(id)))
		{
			unsupported.Add($"removed {id}");
		}

		// A new type comes in whole, members and all, so its methods aren't added again one by one. The .NET 10 runtime takes new
		// types and new fields on existing types.
		var oldTypes = SourceTypes(oldCompilation).Select(type => type.ToDisplayString()).ToHashSet();
		var newTypes = SourceTypes(newCompilation).Where(type => !oldTypes.Contains(type.ToDisplayString())).ToList();
		edits.RemoveAll(edit => edit.Kind == SemanticEditKind.Insert && newTypes.Any(type => SymbolEqualityComparer.Default.Equals(edit.NewSymbol.ContainingType, type)));
		unsupported.RemoveAll(problem => newTypes.Any(type => problem.Contains(type.ToDisplayString())));
		foreach (var type in newTypes.Where(type => type.ContainingType == null || oldTypes.Contains(type.ContainingType.ToDisplayString())))
		{
			edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, type));
		}

		// Removed types and fields can't be taken out of a running game, but nothing uses them any more, so they're only noted
		var newTypeNames = SourceTypes(newCompilation).Select(type => type.ToDisplayString()).ToHashSet();
		foreach (var removed in oldTypes.Where(type => !newTypeNames.Contains(type)))
		{
			Console.WriteLine($"note: {removed} was removed, but stays loaded until the game restarts");
		}

		var oldFields = SourceTypes(oldCompilation).SelectMany(type => type.GetMembers().OfType<IFieldSymbol>()).Select(field => field.ToDisplayString()).ToHashSet();
		var newFieldNames = SourceTypes(newCompilation).SelectMany(type => type.GetMembers().OfType<IFieldSymbol>()).Select(field => field.ToDisplayString()).ToHashSet();
		foreach (var removed in oldFields.Where(field => !newFieldNames.Contains(field) && !oldTypes.Any(type => field.StartsWith(type + ".") && !newTypeNames.Contains(type))))
		{
			Console.WriteLine($"note: {removed} was removed, but stays loaded until the game restarts");
		}

		foreach (var field in SourceTypes(newCompilation)
			         .Where(type => oldTypes.Contains(type.ToDisplayString()))
			         .SelectMany(type => type.GetMembers().OfType<IFieldSymbol>())
			         .Where(field => !field.IsImplicitlyDeclared && !oldFields.Contains(field.ToDisplayString())))
		{
			edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, field));
		}

		return edits;
	}

	private static IEnumerable<INamedTypeSymbol> SourceTypes(
		Compilation compilation
	)
	{
		var stack = new Stack<INamespaceOrTypeSymbol>();
		stack.Push(compilation.Assembly.GlobalNamespace);
		while (stack.Count > 0)
		{
			foreach (var member in stack.Pop().GetMembers())
			{
				if (member is INamespaceSymbol ns)
				{
					stack.Push(ns);
				}
				else if (member is INamedTypeSymbol type && type.Locations.Any(location => location.IsInSource))
				{
					yield return type;
					stack.Push(type);
				}
			}
		}
	}

	private static IEnumerable<IMethodSymbol> SourceMethods(
		Compilation compilation
	)
	{
		return SourceTypes(compilation)
			.SelectMany(type => type.GetMembers().OfType<IMethodSymbol>())
			.Where(method => !method.IsImplicitlyDeclared && method.DeclaringSyntaxReferences.Length > 0);
	}

	private static string Body(
		IMethodSymbol method
	)
	{
		return string.Join("\n", method.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax().ToFullString()));
	}

	private static ImmutableArray<byte> CustomDebugInfo(
		MetadataReader pdb,
		MethodDefinitionHandle method,
		Guid kind
	)
	{
		foreach (var handle in pdb.GetCustomDebugInformation(method))
		{
			var info = pdb.GetCustomDebugInformation(handle);
			if (pdb.GetGuid(info.Kind) == kind)
			{
				return pdb.GetBlobContent(info.Value);
			}
		}

		return default;
	}
}
