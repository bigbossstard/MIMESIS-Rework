using HarmonyLib;

namespace MimesisTestMod.EnemyDropLoot
{
    // DungeonRoom.Initialize runs before DungeonRoom.InitSpawn populates _spawnedActorDatas.
    // Hook after InitSpawn or the loot pool is always empty for this game build.
    [HarmonyPatch(typeof(DungeonRoom), nameof(DungeonRoom.InitSpawn))]
    internal static class DungeonRoomInitSpawnPatch
    {
        private static void Postfix(DungeonRoom __instance) => EnemyDropLootManager.ConfigureForDungeon(__instance);
    }

    [HarmonyPatch(typeof(IVroom), nameof(IVroom.OnVacateRoom), new[] { typeof(bool) })]
    internal static class DungeonRoomVacatePatch
    {
        private static void Postfix(IVroom __instance)
        {
            if (__instance is DungeonRoom)
                EnemyDropLootManager.Reset();
        }
    }

    [HarmonyPatch(typeof(VMonster), nameof(VMonster.OnDying))]
    internal static class VMonsterOnDyingPatch
    {
        private static void Postfix(VMonster __instance) => EnemyDropLootManager.TryHandleMonsterDeath(__instance);
    }
}
