using EnemyDropLoot.Config;
using MelonLoader;

[assembly: MelonInfo(typeof(EnemyDropLoot.Core), "EnemyDropLoot", DooDesch.ModVersion.Current, "DooDesch", null)]
[assembly: MelonGame("ReLUGames", "MIMESIS")]
[assembly: MelonOptionalDependencies("MimicAPI")]

namespace EnemyDropLoot
{
	public sealed class Core : MelonMod
	{
		public override void OnInitializeMelon()
		{
			EnemyDropLootPreferences.Initialize();
			// MelonLoader auto-applies this assembly's Harmony patches via HarmonyInit(); calling PatchAll()
			// here too would double-apply every patch (each prefix/postfix runs twice -> double loot drops).
			// Do NOT add it back. (See FakePlayers/Core.cs.)
			MelonLogger.Msg(
				$"EnemyDropLoot initialized. Enabled={EnemyDropLootPreferences.Enabled}, DropChance={EnemyDropLootPreferences.DropChance:P0}, MaxDropsPerKill={EnemyDropLootPreferences.MaxDropsPerKill}");
#if DEBUG
			MelonLogger.Msg("EnemyDropLoot DEBUG profiler active: F8 overlay, F9 log, F10 spawn +25 loot, F11 reset stats.");
#endif
		}

#if DEBUG
		public override void OnUpdate()
		{
			Debugging.DebugTools.Update();
		}

		public override void OnGUI()
		{
			Debugging.DebugTools.DrawOverlay();
		}
#endif
	}
}

