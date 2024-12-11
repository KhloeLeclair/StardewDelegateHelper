using Microsoft.CodeAnalysis;

namespace StardewDelegateHelper;

internal record CommandData(string? Name, string? Description) {

	internal static CommandData Parse(AttributeData data) {

		if (!data.TryGetArgument("Name", -1, out string? name) && data.ConstructorArguments.Length > 1)
			data.TryGetArgument(null, 0, out name);

		data.TryGetArgument("Description", data.ConstructorArguments.Length > 1 ? 1 : 0, out string? description);

		return new(name, description);
	}

}
