using System;

using Microsoft.CodeAnalysis;

namespace StardewDelegateHelper;

internal record ModVersionData(string UniqueID, string? MinVersion, string? MaxVersion, bool Inverted) {

	internal static ModVersionData Parse(AttributeData data) {
		bool inverted = data.AttributeClass?.Name == "IfNotModLoadedAttribute";

		if (!data.TryGetArgument("UniqueID", 0, out string? uniqueID))
			throw new ArgumentNullException("UniqueID");

		data.TryGetArgument("MinVersion", 1, out string? minVersion);
		data.TryGetArgument("MaxVersion", 2, out string? maxVersion);

		return new(uniqueID, minVersion, maxVersion, inverted);
	}

}
