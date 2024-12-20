using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using StardewDelegateHelper.Models;
using StardewDelegateHelper.SystemStuff;

namespace StardewDelegateHelper.Types;

internal static class TileActions {

	internal static void Initialize(IncrementalGeneratorInitializationContext context) {
		var checker = context.CompilationProvider.Select(GetChecker);

		var methods = context.SyntaxProvider.ForAttributeWithMetadataName(
			@"Leclair.StardewDelegateHelper.TileActionAttribute",
			predicate: Utilities.IsDecoratedMethod,
			transform: TransformMethods
		).Where(m => m is not null);

		var combined = methods.Collect().Combine(checker);

		context.RegisterSourceOutput(combined, (ctx, tuple) => Utilities.GenerateMethodCode(
			ctx, tuple.Left, "TileActions", tuple.Right,
			GetEntriesForMethod,
			MakeFinalMethods
		));
	}


	internal static MethodChecker GetChecker(Compilation compilation, CancellationToken ct) {
		return MethodChecker.CreateWithReturnType(
			compilation.GetSpecialType(SpecialType.System_Boolean),
			compilation.GetTypeByMetadataName(@"StardewValley.GameLocation"),
			compilation.CreateArrayTypeSymbol(compilation.GetSpecialType(SpecialType.System_String)),
			compilation.GetTypeByMetadataName(@"StardewValley.Farmer"),
			compilation.GetTypeByMetadataName(@"Microsoft.Xna.Framework.Point")
		);
	}

	internal static MethodInfo? TransformMethods(GeneratorAttributeSyntaxContext context, CancellationToken ct) {
		var methodNode = (MethodDeclarationSyntax) context.TargetNode;
		if (context.TargetSymbol is not IMethodSymbol method || !method.CanBeReferencedByName)
			return null;

		// Check if the method has our attribute.
		var attrs = method.GetConditionAttributes("TileActionAttribute").ToEquatableArray();
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
				"SDH401",
				"Invalid Tile Action Delegate",
				$"Method has invalid signature for tile action delegate: {error}",
				info.Location
			);

			yield break;
		}

		if (info.ModVersionData.Length > 0)
			state.HadModCheck = true;

		// Variables in the generated method:
		// - string prefix: The prefix that should be prepended to each entry. If this is null, it defaults to "{ModManifest.UniqueID}_"
		//                  assuming, of course, we have access to the ModManifest to read it.

		HashSet<string> visitedNames = [];
		bool first = true;
		string indentation = "\t\t";

		foreach (var data in info.Data) {
			string name = data.Name ?? method.Name;
			string prefixed = data.IncludePrefix ? $"SOME UNLIKELY _PREFIX_{name}" : name;
			if (visitedNames.Contains(prefixed)) {
				ctx.ReportError(
					"SDH402",
					"Duplicate Tile Action Name",
					$"Method '{method.ToDisplayString()}' has been registered with the same name multiple times.",
					info.Location
				);
				continue;
			}

			visitedNames.Add(prefixed);

			string nameWriter;
			if (data.Name is null)
				nameWriter = $"nameof({method.Name})";
			else
				nameWriter = data.Name.ToLiteral();

			if (data.IncludePrefix)
				nameWriter = $"prefix + {nameWriter}";

			if (first) {
				first = false;
				if (info.ModVersionData.Length > 0) {
					yield return new("add", $"\t\tif ({Utilities.GetVersionCheck("helper", info.ModVersionData)}) {{");
					indentation = "\t\t\t";
				}
			}

			yield return new("add", $"{indentation}StardewValley.GameLocation.RegisterTileAction({nameWriter}, {method.Name});");
		}

		if (!first && info.ModVersionData.Length > 0)
			yield return new("add", "\t\t}");
	}

	internal static string? MakeFinalMethods(string key, INamedTypeSymbol containingType, bool isStatic, IEnumerable<string> content, State state) {
		// Check which values we can read off the containing type.
		var modHelper = containingType.GetMemberByType(Constants.IModHelper, isStatic);
		bool needsHelper = state.HadModCheck;

		StringBuilder sb = new();

		string ss = isStatic ? "static " : "";
		string? methodName = key switch {
			"add" => isStatic ? "RegisterStaticTileActions" : "RegisterTileActions",
			_ => null
		};

		if (methodName is null)
			return null;

		// We always need access to the mod's unique ID. This can be read
		// from either the mod manifest or the mod helper.

		// We *may* need access to the mod registry from the mod helper.

		// Possible signatures:
		// RegisterTileActions(IModHelper helper, string? prefix = null)
		// RegisterTileActions(IModHelper? helper = null, string? prefix = null)
		// RegisterTileActions(string? prefix)
		// RegisterTileActions(string prefix)

		if (modHelper is null) {
			if (needsHelper) {
				// RegisterTileActions(IModHelper helper, string? prefix = null)
				sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper} helper, string? prefix = null) {{");
				sb.AppendLine($"\t\tprefix ??= $\"{{helper.ModRegistry.ModID}}_\";");

				// Do not emit prefix-only, since a helper is required.

			} else {
				// RegisterTileActions(IModHelper helper, string? prefix = null)
				// this calls the prefix-only method.
				sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper} helper, string? prefix = null) {{");
				sb.AppendLine($"\t\t{methodName}(prefix ?? $\"{{helper.ModRegistry.ModID}}_\");");
				sb.AppendLine($"\t}}");

				// RegisterTileActions(string prefix)
				sb.AppendLine($"\tinternal {ss}void {methodName}(string prefix) {{");
			}

		} else {
			// RegisterTileActions(string? prefix)
			// this calls the optional-helper method
			sb.AppendLine($"\tinternal {ss}void {methodName}(string? prefix) {{");
			sb.AppendLine($"\t\t{methodName}({modHelper.Name}, prefix);");
			sb.AppendLine($"\t}}");

			// RegisterTileActions(IModHelper? helper = null, string? prefix = null)
			sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper}? helper = null, string? prefix = null) {{");
			sb.AppendLine($"\t\thelper ??= {modHelper.Name};");
			sb.AppendLine($"\t\tprefix ??= $\"{{helper.ModRegistry.ModID}}_\";");
		}

		// Method body
		foreach (string line in content)
			sb.AppendLine(line);

		sb.AppendLine("\t}");

		return sb.ToString();
	}

	internal record MethodInfo(
		EquatableSymbol<IMethodSymbol> Method,
		EquatableArray<ConditionData> Data,
		EquatableArray<ModVersionData> ModVersionData,
		EquatableLocation? Location,
		bool ContainingTypeIsPartial
	) : IMethodInfo;

}
