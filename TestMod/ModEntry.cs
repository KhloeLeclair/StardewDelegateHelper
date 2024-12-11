using System;
using System.Collections.Generic;

using Leclair.StardewDelegateHelper;

using Microsoft.Xna.Framework;

using StardewModdingAPI;
using StardewModdingAPI.Events;

using StardewValley;
using StardewValley.Delegates;
using StardewValley.Internal;

namespace TestMod;

public partial class ModEntry : Mod {

	public override void Entry(IModHelper helper) {
		RegisterEvents();
		RegisterStaticGSQConditions(helper);
		RegisterTriggerActions();
		RegisterTouchActions();
		RegisterItemResolvers();
		RegisterConsoleCommands();
	}

	[ConsoleCommand("Hello there.")]
	[ConsoleCommand("general_kenobi", "what even")]
	private void SomeCommand(string name, string[] args) {

	}

	[ItemResolver]
	[IfModLoaded("spacechase0.SpaceCore")]
	private IEnumerable<ItemQueryResult> Test(string key, string arguments, ItemQueryContext context, bool avoidRepeat, HashSet<string> avoidItemIds, Action<string, string> errorLog) {
		yield break;
	}

	[TileAction]
	private static bool Test(GameLocation loc, string[] args, Farmer who, Point where) {
		return false;
	}

	[SMAPIEvent]
	[IfModLoaded("spacechase0.SpaceCore")]
	[IfNotModLoaded("some.OtherMod")]
	private void OnAssetRequested(object? sender, AssetRequestedEventArgs e) {

	}

	[SMAPIEvent]
	private static void OnSomethingElse(object? sender, RenderedEventArgs e) {

	}

	[SMAPIEvent]
	private void OnGameStarted(object? sender, GameLaunchedEventArgs e) {

	}

	[GSQCondition]
	private static bool TestCondition(string[] args, GameStateQueryContext ctx) {
		return false;
	}

	[TouchAction]
	private void SomeTileThing(GameLocation location, string[] args, Farmer who, Vector2 where) {

	}

	[TriggerAction]
	[IfModLoaded("spacechase0.SpaceCore")]
	private bool Woop(string[] args, TriggerActionContext ctx, out string? error) {
		error = null;
		return false;
	}

}
