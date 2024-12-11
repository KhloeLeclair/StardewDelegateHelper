using System;

using Microsoft.CodeAnalysis;

namespace StardewDelegateHelper.Models;

internal readonly struct EquatableLocation(Location location) : IEquatable<EquatableLocation> {

	public readonly Location Value = location;

	public bool Equals(EquatableLocation other) {
		if (Value.SourceTree?.FilePath != other.Value.SourceTree?.FilePath)
			return false;

		if (Value.SourceSpan != other.Value.SourceSpan)
			return false;

		return true;
	}

	public static EquatableLocation? CreateFrom(SyntaxNode node) => new(node.GetLocation());
	public static EquatableLocation? CreateFrom(Location location) => new(location);

}


internal static class EquatableLocationHelpers {
	public static EquatableLocation? ToEquatable(this Location location) => new(location);

	public static EquatableLocation? GetEquatableLocation(this SyntaxNode node) => new(node.GetLocation());
}
