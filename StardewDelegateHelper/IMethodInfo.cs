using Microsoft.CodeAnalysis;

using StardewDelegateHelper.Models;

namespace StardewDelegateHelper;


internal interface IMethodInfo {

	public EquatableSymbol<IMethodSymbol> Method { get; }

	public bool ContainingTypeIsPartial { get; }

}
