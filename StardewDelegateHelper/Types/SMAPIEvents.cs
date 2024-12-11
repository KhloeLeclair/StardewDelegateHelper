using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StardewDelegateHelper.Types;


internal static class SMAPIEvents {

	public const string INVALID_EVENT = "__INVALID_ _EVENT__";

	internal static void Initialize(IncrementalGeneratorInitializationContext context) {
		var modEventsType = context.CompilationProvider.Select(GetEventSymbol);
		var methods = context.SyntaxProvider.CreateSyntaxProvider(
			predicate: Utilities.IsDecoratedMethod,
			transform: TransformMethods
		).Where(m => m is not null);

		var combined = methods.Collect().Combine(modEventsType);

		context.RegisterSourceOutput(combined, (ctx, tuple) => {
			var eventMap = BuildSMAPIEventMap(tuple.Right);
			Utilities.GenerateMethodCode(
				ctx, tuple.Left, "Events", eventMap,
				GetEntriesForMethod,
				MakeFinalMethods
			);
		});
	}


	internal static ITypeSymbol? GetEventSymbol(Compilation compilation, CancellationToken ct) {
		// Get IModEvents symbol
		return compilation.GetTypeByMetadataName("StardewModdingAPI.Events.IModEvents");
	}

	/// <summary>
	/// This method locates the <c>IModEvents</c> interface from the target project's
	/// dependencies and recursively reads all the available events from it, putting
	/// them into a dictionary that we can use to map event argument types
	/// to where within the helper namespace the events are located.
	/// </summary>
	internal static Dictionary<ITypeSymbol, string> BuildSMAPIEventMap(ITypeSymbol? modEvents) {
		var result = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

		// No modEvents? No events.
		if (modEvents is null)
			return result;

		// Our recursive method for scanning a type's properties and events.
		void ScanThing(ITypeSymbol symbol, string path, int depth = 0) {
			if (depth < 5)
				foreach (var prop in symbol.GetMembers().OfType<IPropertySymbol>()) {
					string subpath = $"{path}{prop.Name}.";
					ScanThing(prop.Type, subpath, depth + 1);
				}

			foreach (var evt in symbol.GetMembers().OfType<IEventSymbol>()) {
				// Sanity checking for the type.
				if (evt.Type is not INamedTypeSymbol type || type.TypeArguments.Length != 1 || type.TypeArguments[0] is not ITypeSymbol argType)
					continue;

				// If we already encountered this type somehow, abort. Not
				// just abort but set this to an invalid value so we will
				// annotate the method with an error instead of generating code.
				if (result.ContainsKey(argType))
					result[argType] = INVALID_EVENT;
				else
					result.Add(argType, path + evt.Name);
			}
		}

		// Alright, scan our thing and then return.
		ScanThing(modEvents, string.Empty);
		return result;
	}


	/// <summary>
	/// This method takes a syntax node, determines if it's properly decorated with SMAPIEvent and has the
	/// correct arguments, and returns an EventMethodInfo if it looks correct that can later be used for
	/// source generation.
	/// </summary>
	/// <param name="context">The context of the syntax node</param>
	/// <param name="ct">A cancellation token we pass along to stuff</param>
	internal static MethodInfo? TransformMethods(GeneratorSyntaxContext context, CancellationToken ct) {
		var methodNode = (MethodDeclarationSyntax) context.Node;
		if (context.SemanticModel.GetDeclaredSymbol(methodNode, ct) is not IMethodSymbol method || !method.CanBeReferencedByName)
			return null;

		// Check if the method has our attribute.
		if (!method.GetAttributes().Any(ad => ad.AttributeClass?.ToDisplayString() == "Leclair.StardewDelegateHelper.SMAPIEventAttribute"))
			return null;

		// Get the type of the second argument, assuming we have two arguments.
		var argType = method.Parameters.Length == 2
			? method.Parameters[1].Type
			: null;

		// Get the mod version attributes.
		var modData = method.GetModLoadedAttributes();

		// Check if the containing type is partial, since that's important.
		bool isPartial = methodNode.IsContainingTypePartial();

		return new(method.ToEquatable(), argType?.ToEquatable(), modData.ToEquatableArray(), methodNode.GetEquatableLocation(), isPartial);
	}

	internal static IEnumerable<KeyValuePair<string, string>> GetEntriesForMethod(SourceProductionContext ctx, MethodInfo info, Dictionary<ITypeSymbol, string> eventMap, State state) {
		// Do we have a matching event type?
		if (!info.ArgType.HasValue) {
			ctx.ReportError(
				"SDH101",
				"Invalid Event Delegate",
				$"Method has invalid signature for event delegate.",
				info.Location
			);
			yield break;
		}

		var method = info.Method.Value;
		var argType = info.ArgType.Value.Value;

		if (info.ModVersionData.Length > 0)
			state.HadModCheck = true;

		if (eventMap.TryGetValue(argType, out string? eventPath)) {
			if (eventPath == INVALID_EVENT) {
				// If it's an invalid event, report it now.
				ctx.ReportError(
					"SDH102",
					"Unknown Event Type",
					$"Multiple potential matching events found for argument type '{argType.ToDisplayString()}', please register event manually.",
					info.Location
				);

			} else {
				if (info.ModVersionData.Length > 0)
					yield return new("add", $"\t\tif ({Utilities.GetVersionCheck("helper", info.ModVersionData)})\n\t\t\thelper.Events.{eventPath} += {method.Name};");
				else
					yield return new("add", $"\t\thelper.Events.{eventPath} += {method.Name};");

				yield return new("remove", $"\t\thelper.Events.{eventPath} -= {method.Name};");
			}

		} else {
			// If we don't, add a diagnostic thing.
			ctx.ReportError(
				"SDH103",
				"Unknown Event Type",
				$"No SMAPI event found for argument type '{argType.ToDisplayString()}'",
				info.Location
			);
		}
	}

	internal static string? MakeFinalMethods(string key, INamedTypeSymbol containingType, bool isStatic, IEnumerable<string> content, State state) {
		// Check which values we can read off the containing type.
		var modHelper = containingType.GetMemberByType(Constants.IModHelper, isStatic);

		StringBuilder sb = new();

		string ss = isStatic ? "static " : "";
		string? methodName = key switch {
			"add" => isStatic ? "RegisterStaticEvents" : "RegisterEvents",
			"remove" => isStatic ? "UnregisterStaticEvents" : "UnregisterEvents",
			_ => null
		};

		if (methodName is null)
			return null;

		// We always need access to the mod registry from the mod helper.
		// Depending on availability, the signature of these methods may change. We'll
		// have either:

		// RegisterEvents(IModHelper helper)
		// RegisterEvents(IModHelper? helper = null)

		if (modHelper is null) {
			// RegisterEvents(IModHelper helper)
			sb.AppendLine($"\tinternal {ss}void {methodName}({Constants.IModHelper} helper) {{");

		} else {
			// RegisterEvents(IModHelper? helper = null)
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
		EquatableSymbol<ITypeSymbol>? ArgType,
		EquatableArray<ModVersionData> ModVersionData,
		EquatableLocation? Location,
		bool ContainingTypeIsPartial
	) : IMethodInfo;

}
