using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StardewDelegateHelper;
internal static class Utilities {

	internal static bool TryGetArgument<T>(this AttributeData input, string name, int index, [NotNullWhen(true)] out T? result) {
		if (input.ConstructorArguments.Length > index && input.ConstructorArguments[index].Value is T val) {
			result = val;
			return result is not null;
		}

		foreach (var arg in input.NamedArguments) {
			if (arg.Key.Equals(name, System.StringComparison.OrdinalIgnoreCase)) {
				if (arg.Value.Value is T val2) {
					result = val2;
					return result is not null;
				}
				break;
			}
		}

		result = default;
		return false;
	}

	internal static string ToLiteral(this bool input) {
		return SymbolDisplay.FormatPrimitive(input, true, false);
	}

	internal static string ToLiteral(this string? input, bool quote = true) {
		return input is null ? "null" : SymbolDisplay.FormatLiteral(input, quote: quote);
	}

	internal static string GetVersionCheck(string helperName, EquatableArray<ModVersionData> mods) {
		StringBuilder sb = new();

		foreach (var mod in mods) {
			if (sb.Length > 0)
				sb.Append(", ");
			sb.Append("new Leclair.StardewDelegateHelper.SDHInternal.ModData(");
			sb.Append(mod.UniqueID.ToLiteral());
			if (mod.MinVersion != null || mod.MaxVersion != null || mod.Inverted) {
				sb.Append(",");
				sb.Append(mod.MinVersion.ToLiteral());
				if (mod.MaxVersion != null || mod.Inverted) {
					sb.Append(",");
					sb.Append(mod.MaxVersion.ToLiteral());
					if (mod.Inverted) {
						sb.Append(",");
						sb.Append(mod.Inverted.ToLiteral());
					}
				}
			}
			sb.Append(")");
		}

		return $"Leclair.StardewDelegateHelper.SDHInternal.CheckModVersions({helperName}, {sb})";
	}


	internal static void ReportError(this SourceProductionContext ctx, string id, string title, string description, EquatableLocation? location) {
		ctx.ReportError(id, title, description, location?.Value);
	}


	internal static void ReportError(this SourceProductionContext ctx, string id, string title, string description, Location? location) {
		ctx.ReportDiagnostic(Diagnostic.Create(
			new DiagnosticDescriptor(
				id,
				title,
				description,
				"Usage",
				DiagnosticSeverity.Error,
				true
			),
			location
		));
	}

	internal static bool IsContainingTypePartial(this SyntaxNode symbol) {
		var containingType = symbol.FirstAncestorOrSelf<TypeDeclarationSyntax>();
		return containingType != null && containingType.Modifiers.Any(mod => mod.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));
	}


	internal static IEnumerable<ConditionData> GetConditionAttributes(this IMethodSymbol input, string type) {
		type = $"Leclair.StardewDelegateHelper.{type}";

		return input.GetAttributes()
			.Where(ad => ad.AttributeClass?.ToDisplayString() == type)
			.Select(ConditionData.Parse);
	}


	internal static IEnumerable<ModVersionData> GetModLoadedAttributes(this IMethodSymbol input) {
		return input.GetAttributes()
			.Where(ad => {
				string? className = ad.AttributeClass?.ToDisplayString();
				return className == "Leclair.StardewDelegateHelper.IfModLoadedAttribute" || className == "Leclair.StardewDelegateHelper.IfNotModLoadedAttribute";
			})
			.Select(ModVersionData.Parse);
	}


	internal static ISymbol? GetMemberByType(this ITypeSymbol input, string typeName, bool isStatic = false) {
		return input.GetBaseTypesAndThis()
			.SelectMany(type => type.GetMembers())
			.FirstOrDefault(symbol => symbol.CanBeReferencedByName && symbol.IsStatic == isStatic && (
				symbol is IPropertySymbol prop && prop.Type.ToDisplayString() == typeName ||
				symbol is IFieldSymbol field && field.Type.ToDisplayString() == typeName
			));
	}


	internal static IEnumerable<ITypeSymbol> GetBaseTypesAndThis(this ITypeSymbol? type) {
		var current = type;
		while (current != null) {
			yield return current;
			current = current.BaseType;
		}
	}

	internal static string GetAccessibilityString(Accessibility input) {
		return input switch {
			Accessibility.Public => "public",
			Accessibility.Protected => "protected",
			Accessibility.Internal => "internal",
			Accessibility.Private => "private",
			Accessibility.ProtectedAndInternal => "protected internal",
			_ => "private"
		};
	}


	internal static string GetTypeKindString(ITypeSymbol input) {
		if (input.IsRecord)
			return input.TypeKind == TypeKind.Struct
				? "record struct"
				: "record";

		return input.TypeKind switch {
			TypeKind.Class => "class",
			TypeKind.Interface => "interface",
			TypeKind.Struct => "struct",
			_ => "class"
		};
	}


#pragma warning disable IDE0060 // Remove unused parameter
	internal static bool IsDecoratedMethod(SyntaxNode node, CancellationToken ct) {
		return node is MethodDeclarationSyntax md && md.AttributeLists.Count > 0;
	}
#pragma warning restore IDE0060 // Remove unused parameter

	internal delegate IEnumerable<KeyValuePair<string, TResult>>? GetEntriesForMethodDelegate<TInfo, TExtraInfo, TResult>(SourceProductionContext context, TInfo info, TExtraInfo extraInfo, State state) where TInfo : IMethodInfo;
	internal delegate string? MakeFinalMethodDelegate<TResult>(string key, INamedTypeSymbol containingType, bool isStatic, IEnumerable<TResult> content, State state);

	internal static void GenerateMethodCode<TInfo, TExtraInfo, TResult>(
		SourceProductionContext ctx,
		ImmutableArray<TInfo?> methods,
		string type,
		TExtraInfo extraInfo,
		GetEntriesForMethodDelegate<TInfo, TExtraInfo, TResult> entryDelegate,
		MakeFinalMethodDelegate<TResult> methodDelegate
	) where TInfo : IMethodInfo {

		// First, group methods by their declaring type.
		var byType = methods
			.OfType<TInfo>()
			.GroupBy(m => m.Method.Value.ContainingType, SymbolEqualityComparer.Default);

		// Now, iterate those groups
		foreach (var typeGroup in byType) {
			if (typeGroup.Key is not INamedTypeSymbol containingType)
				continue;

			if (!containingType.CanBeReferencedByName || !containingType.ContainingNamespace.CanBeReferencedByName) {
				ctx.ReportDiagnostic(Diagnostic.Create(
					new DiagnosticDescriptor(
						"SDH001",
						"Unnamable Type",
						"StardewDelegateHelper cannot generate code for this type as it cannot be referenced by name.",
						"Usage",
						DiagnosticSeverity.Error,
						true
					),
					containingType.Locations.FirstOrDefault(),
					containingType.Locations
				));
				continue;
			}

			bool isPartial = typeGroup.First().ContainingTypeIsPartial;
			if (!isPartial) {
				ctx.ReportDiagnostic(Diagnostic.Create(
					new DiagnosticDescriptor(
						"SDH002",
						"Type Not Partial",
						$"StardewDelegateHelper relies on partial classes to generate its helper methods but the type '{containingType.ToDisplayString()}' is not partial despite using SDH features.",
						"Usage",
						DiagnosticSeverity.Error,
						true
					),
					containingType.Locations.FirstOrDefault(),
					containingType.Locations
				));

				continue;
			}

			StringBuilder sb = new();

			// Alright, let's further separate things by whether or not they're static.
			var byStatic = typeGroup.GroupBy(m => m.Method.Value.IsStatic);

			foreach (var staticGroup in byStatic) {
				var results = new Dictionary<string, List<TResult>>();
				State state = new();

				foreach (var item in staticGroup) {
					var itemResult = entryDelegate(ctx, item, extraInfo, state);
					if (itemResult != null)
						foreach (var entry in itemResult) {
							if (!results.TryGetValue(entry.Key, out var resultList)) {
								resultList = [];
								results[entry.Key] = resultList;
							}

							resultList.Add(entry.Value);
						}
				}

				if (results.Count <= 0)
					continue;

				// We got some stuff. Let's build some methods.
				foreach (var entry in results) {
					string? method = methodDelegate(entry.Key, containingType, staticGroup.Key, entry.Value, state);
					if (!string.IsNullOrWhiteSpace(method))
						sb.AppendLine(method!.TrimEnd());
				}
			}

			if (sb.Length <= 0)
				continue;

			// If we got here, then we had code for this type. Let's emit some source!
			string namespaceName = containingType.ContainingNamespace.ToDisplayString();
			string typeName = containingType.Name;
			string accessName = GetAccessibilityString(containingType.DeclaredAccessibility);
			string staticName = containingType.IsStatic ? "static " : "";
			string kindName = GetTypeKindString(containingType);

			StringBuilder result = new();
			result.AppendLine(Constants.Header);
			result.AppendLine($"namespace {namespaceName};");
			result.AppendLine("");
			result.AppendLine($"{accessName} {staticName}partial {kindName} {typeName} {{");
			result.Append(sb);
			result.AppendLine("}");

			ctx.AddSource($"{namespaceName}.{typeName}.{type}.g.cs", result.ToString());
		}
	}

}
