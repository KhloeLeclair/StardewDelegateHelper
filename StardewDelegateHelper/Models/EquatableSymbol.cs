using System;

using Microsoft.CodeAnalysis;

namespace StardewDelegateHelper.Models;


internal readonly struct EquatableSymbol<T>(T value) : IEquatable<EquatableSymbol<T>> where T : ISymbol {

	public readonly T Value = value;

	public bool Equals(EquatableSymbol<T> other) {
		return SymbolEqualityComparer.Default.Equals(Value, other.Value);
	}

}


internal static class EquatableSymbol {

	public static EquatableSymbol<T> ToEquatable<T>(this T value) where T : ISymbol {
		return new(value);
	}

}
