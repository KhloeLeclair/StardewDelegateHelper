using Microsoft.CodeAnalysis;

using StardewDelegateHelper.Types;

namespace StardewDelegateHelper;

[Generator]
public partial class SourceGenerator : IIncrementalGenerator {

	public void Initialize(IncrementalGeneratorInitializationContext context) {

		context.RegisterPostInitializationOutput(ctx => {
			ctx.AddSource(
				"StardewDelegateHelper.Attributes.g.cs", $@"
{Constants.Header}
namespace Leclair.StardewDelegateHelper;

{Constants.Attributes}"
			);

			ctx.AddSource(
				"StardewDelegateHelper.Helpers.g.cs", $@"
{Constants.Header}
namespace Leclair.StardewDelegateHelper;

{Constants.Helpers}"
			);

		});

		ConsoleCommands.Initialize(context);
		GSQConditions.Initialize(context);
		ItemResolvers.Initialize(context);
		SMAPIEvents.Initialize(context);
		TileActions.Initialize(context);
		TouchActions.Initialize(context);
		TriggerActions.Initialize(context);

	}

}
