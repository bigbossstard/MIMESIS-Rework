using EnemyDropLoot.Managers;
using HarmonyLib;

namespace EnemyDropLoot.Patches
{
	[HarmonyPatch(typeof(DungeonRoom), nameof(DungeonRoom.Initialize))]
	internal static class DungeonRoomInitializePatch
	{
		private static void Postfix(DungeonRoom __instance)
		{
			LootPoolManager.ConfigureForDungeon(__instance);
		}
	}

	[HarmonyPatch(typeof(IVroom), nameof(IVroom.OnVacateRoom), new[] { typeof(bool) })]
	internal static class DungeonRoomVacatePatch
	{
		private static void Postfix(IVroom __instance)
		{
			if (__instance is DungeonRoom)
			{
				LootPoolManager.Reset();
			}
		}
	}
}

