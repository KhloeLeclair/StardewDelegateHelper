using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using StardewDelegateHelper.Models;
using StardewDelegateHelper.SystemStuff;

namespace StardewDelegateHelper.Types;

internal static class ConsoleCommands {

	internal static void Initialize(IncrementalGeneratorInitializationContext context) {
		var checker = context.CompilationProvider.Select(GetChecker);

		var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
			@"Leclair.StardewDelegateHelper.ConsoleCommandAttribute",
			predicate: Utilities.IsDecoratedMethod,
			transform: TransformMethods
		).Where(m => m is not null);

		var combined = methods.Collect().Combine(checker);

		context.RegisterSourceOutput(combined, (ctx, tuple) => Utilities.GenerateMethodCode(
			ctx, tuple.Left, "ConsoleCommands", tuple.Right,
			GetEntriesForMethod,
			MakeFinalMethods
		));
	}


	internal static MethodChecker GetChecker(Compilation compilation, CancellationToken ct) {
		return MethodChecker.Create(
			compilation.GetSpecialType(SpecialType.System_String),
			compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_String))
		);
	}

	internal static MethodInfo? TransformMethods(GeneratorAttributeSyntaxContext context, CancellationToken ct) {
		var methodNode = (MethodDeclarationSyntax) context.TargetNode;
		if (context.TargetSymbol is not IMethodSymbol method || !method.CanBeReferencedByName)
			return null;

		// Check if the method has our attribute.
		var attrs = method.GetCommandAttributes("ConsoleCommandAttribute").ToEquatableArray();
		if (attrs.IsEmpty)
			return null;

		// Get the mod version attributes.
		var modData = method.GetModLoadedAttributes();
		// Check if the containing type is partial, since that's important.
		bool isPartial = methodNode.IsContainingTypePartial();

		return new(method.ToEquatable(), attrs, modData.ToEquatableArray(), methodNode.GetEquatableLocation(), isPartial);
	}

	internal static IEnumerable<KeyValuePair<string, string>> GetEntriesForMethod(SourceProductionContext ctx, MethodInfo info, MethodChecker checker, State state) {
		var method = info.Method.Value;

		// Check the method has valid parameters.
		if (!checker.Matches(method, out string? error)) {
			ctx.ReportError(
				"SDH701",
				"Invalid Console Command Delegate",
				$"Method has invalid signature for console command delegate: {error}",
				info.Location
			);

			yield break;
		}

		if (info.ModVersionData.Length > 0)
			state.HadModCheck = true;

		// Variables in the generated method:
		// - IModHelper helper: A mod helper, needed for registering stuff.

		HashSet<string> visitedNames = [];
		bool first = true;
		string indentation = "\t\t";

		foreach (var data in info.Data) {
			string name = data.Name ?? method.Name;
			if (visitedNames.Contains(name)) {
				ctx.ReportError(
					"SDH702",
					"Duplicate Console Command Name",
					$"Method '{method.ToDisplayString()}' has been registered with the same name multiple times.",
					info.Location
				);
				continue;
			}

			visitedNames.Add(name);

			string nameWriter;
			if (data.Name is null)
				nameWriter = $"nameof({method.Name})";
			else
				nameWriter = data.Name.ToLiteral();

			if (first) {
				first = false;
				if (info.ModVersionData.Length > 0) {
					yield return new("add", $"\t\tif ({Utilities.GetVersionCheck("helper", info.ModVersionData)}) {{");
					indentation = "\t\t\t";
				}
			}

			yield return new("add", $"{indentation}helper.ConsoleCommands.Add({nameWriter}, {data.Description.ToLiteral()}, {method.Name});");
		}

		if (!first && info.ModVersionData.Length > 0)
			yield return new("add", "\t\t}");
	}

	internal static string? MakeFinalMethods(string key, INamedTypeSymbol containingType, bool isStatic, IEnumerable<string> content, State state) {
		// Check which values we can read off the containing type.
		var modHelper = containingType.GetMemberByType(Constants.IModHelper, isStatic);

		StringBuilder sb = new();

		string ss = isStatic ? "static " : "";
		string? methodName = key switch {
			"add" => isStatic ? "RegisterStaticConsoleCommands" : "RegisterConsoleCommands",
			_ => null
		};

		if (methodName is null)
			return null;

		// We always need access to the mod helper. We never need a prefix.

		// Possible signatures:
		// RegisterConsoleCommands(IModHelper helper)
		// RegisterConsoleCommands(IModHelper? helper = null)

		if (modHelper is null) {
			// RegisterConsoleCommands(IModHelper helper)
			sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper} helper) {{");

		} else {
			// RegisterConsoleCommands(IModHelper? helper = null)
			sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper}? helper = null) {{");
			sb.AppendLine($"\t\thelper ??= {modHelper.Name};");
		}

		// Method body
		foreach (string line in content)
			sb.AppendLine(line);

		sb.AppendLine("\t}");

		return sb.ToString();
	}

	internal record MethodInfo(
		EquatableSymbol<IMethodSymbol> Method,
		EquatableArray<CommandData> Data,
		EquatableArray<ModVersionData> ModVersionData,
		EquatableLocation? Location,
		bool ContainingTypeIsPartial
	) : IMethodInfo;

}
