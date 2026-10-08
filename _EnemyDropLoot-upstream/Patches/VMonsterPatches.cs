using EnemyDropLoot.Managers;
using HarmonyLib;

namespace EnemyDropLoot.Patches
{
	// Hook the exact event the game itself uses to drop monster loot: VMonster.OnDying(ActorDyingSig), which fires
	// once on the death transition. In 0.3.0 the game moved its own monster-drop logic out of OnDead into OnDying;
	// OnDead is now empty and can RE-fire on Rescue/Revive (and runs on force-kills the game never drops on), which
	// would make us drop extra loot. Postfix so our additional loot is spawned alongside the game's own drop.
	[HarmonyPatch(typeof(VMonster), nameof(VMonster.OnDying))]
	internal static class VMonsterOnDyingPatch
	{
		private static void Postfix(VMonster __instance)
		{
			LootPoolManager.TryHandleMonsterDeath(__instance);
		}
	}
}

