using Microsoft.CodeAnalysis;

namespace StardewDelegateHelper;

internal record ConditionData(string? Name, bool IncludePrefix) {

	internal static ConditionData Parse(AttributeData data) {

		data.TryGetArgument("Name", 0, out string? name);

		if (!data.TryGetArgument("IncludePrefix", 1, out bool includePrefix))
			includePrefix = true;

		return new(name, includePrefix);
	}

}
