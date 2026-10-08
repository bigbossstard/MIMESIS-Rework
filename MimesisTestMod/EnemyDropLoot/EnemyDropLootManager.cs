using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Bifrost.ConstEnum;
using Bifrost.Cooked;
using ReluNetwork.ConstEnum;
using ReluProtocol;
using ReluProtocol.Enum;
using UnityEngine;

namespace MimesisTestMod.EnemyDropLoot
{
    internal static class EnemyDropLootManager
    {
        private static readonly List<RandomSpawnedItemActorData> LootSources = new List<RandomSpawnedItemActorData>();
        private static readonly List<int> FallbackItemIds = new List<int>();
        private static IVroom _activeRoom;
        private static bool _hasActiveRoom;

        internal static void Reset()
        {
            LootSources.Clear();
            FallbackItemIds.Clear();
            _activeRoom = null;
            _hasActiveRoom = false;
        }

        internal static void ConfigureForDungeon(DungeonRoom room)
        {
            Reset();
            _activeRoom = room;
            _hasActiveRoom = true;
            try
            {
                // EnemyDropLoot uses MimicAPI for this private game field. The mod already uses Harmony,
                // so resolve the same field directly and keep MimicAPI as an optional dependency.
                var spawnDict = AccessTools.Field(typeof(IVroom), "_spawnedActorDatas")?.GetValue(room) as IDictionary;
                if (spawnDict == null)
                {
                    MelonLogger.Warning("MIMESIS Rework: unable to read dungeon spawn data; enemy loot disabled for this room.");
                    return;
                }

                foreach (DictionaryEntry entry in spawnDict)
                {
                    if (!(entry.Value is RandomSpawnedItemActorData randomData) || randomData.MarkerType != MapMarkerType.LootingObject)
                        continue;
                    LootSources.Add(randomData);
                    foreach (int itemId in randomData.Candidates.Keys)
                        if (!FallbackItemIds.Contains(itemId))
                            FallbackItemIds.Add(itemId);
                }

                if (FallbackItemIds.Count == 0)
                {
                    // Some generated rooms have no randomized item spawn markers. Keep the feature
                    // working with valid game items instead of silently making every roll empty.
                    foreach (ItemMasterInfo item in GetDebugItemCandidates())
                        FallbackItemIds.Add(item.MasterID);
                }

                MelonLogger.Msg("MIMESIS Rework: loaded {0} loot spawn bundles with {1} unique items.", LootSources.Count, FallbackItemIds.Count);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("MIMESIS Rework: failed to read dungeon loot pool. " + ex);
            }
        }

        internal static void TryHandleMonsterDeath(VMonster monster)
        {
            if (!EnemyDropLootPreferences.Enabled || FallbackItemIds.Count == 0 || !_hasActiveRoom || monster.VRoom != _activeRoom)
                return;

            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || pdata.ClientMode != NetworkClientMode.Host)
                return;

            DataManager dataManager = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            MonsterInfo info = dataManager?.ExcelDataManager?.GetMonsterInfo(monster.MasterID);
            if (info == null)
                return;

            // VMonster.OnDying already handles the game's configured ItemDropMasterID. Do not
            // stack a second mod reward on top of that native drop (currently BabyRilla uses it).
            if (info.ItemDropMasterID != 0)
                return;

            float chance = GetThreatDropChance(info) * EnemyDropLootPreferences.DropChance;
            int dropCount = EnemyDropLootPreferences.RollDropCount(chance);
            for (int i = 0; i < dropCount; i++)
            {
                if (TryPickItem(out int itemMasterId))
                    TrySpawnLoot(monster, itemMasterId);
            }
        }

        private static float GetThreatDropChance(MonsterInfo info)
        {
            // Values come from MonsterData.json: 40 is the common pool, 60-70 are
            // higher-threat/less frequent encounters, and 100 is the rare top tier.
            if (info.IsMimic())
                return 0.35f;
            if (info.ThreatValue >= 100)
                return 0.60f;
            if (info.ThreatValue >= 70)
                return 0.45f;
            if (info.ThreatValue >= 60)
                return 0.30f;
            // HeavyD is a low-speed outlier in the common ThreatValue=40 pool, but
            // its 360 HP makes a kill cost substantially more than other common mobs.
            if (info.HP >= 300)
                return 0.25f;
            if (info.ThreatValue >= 40)
                return 0.15f;
            return 0f;
        }

        private static bool TryPickItem(out int itemMasterId)
        {
            itemMasterId = 0;
            if (FallbackItemIds.Count == 0)
                return false;
            if (LootSources.Count == 0)
            {
                itemMasterId = FallbackItemIds[UnityEngine.Random.Range(0, FallbackItemIds.Count)];
                return itemMasterId != 0;
            }
            RandomSpawnedItemActorData source = LootSources[UnityEngine.Random.Range(0, LootSources.Count)];
            int pickedItem = source.GetPickedItemValue();
            if (pickedItem == 0 && FallbackItemIds.Count > 0)
                pickedItem = FallbackItemIds[UnityEngine.Random.Range(0, FallbackItemIds.Count)];
            itemMasterId = pickedItem;
            return itemMasterId != 0;
        }

        private static bool TrySpawnLoot(VMonster monster, int itemMasterId)
        {
            ItemElement itemElement = monster.VRoom.GetNewItemElement(itemMasterId, false);
            if (itemElement == null)
                return false;

            Vector3 spawnPos = monster.PositionVector;
            Vector3 nearestPos = spawnPos;
            VWorld world = AccessTools.Property(typeof(Hub), "vworld")?.GetValue(Hub.s) as VWorld;
            if (world != null)
            {
                Vector3 navPos = world.FindNearestPoly(spawnPos);
                if (navPos != NavMeshConstants.INVALID_POSITION)
                    nearestPos = navPos;
            }

            PosWithRot dropPos = monster.Position.Clone();
            dropPos.x = nearestPos.x;
            dropPos.y = nearestPos.y;
            dropPos.z = nearestPos.z;
            int spawnResult = monster.VRoom.SpawnLootingObject(itemElement, dropPos, monster.IsIndoor, ReasonOfSpawn.ActorDying);
            if (spawnResult == 0)
            {
                MelonLogger.Warning("MIMESIS Rework: failed to spawn loot item {0} for monster {1}.", itemMasterId, monster.MasterID);
                return false;
            }
            MelonLogger.Msg("MIMESIS Rework: monster {0} ({1}) dropped item {2}.", monster.ActorName, monster.MasterID, itemMasterId);
            return true;
        }

        internal static List<ItemMasterInfo> GetDebugItemCandidates()
        {
            var items = new List<ItemMasterInfo>();
            var dataManager = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            if (dataManager?.ExcelDataManager == null)
                return items;
            foreach (ItemMasterInfo item in dataManager.ExcelDataManager.ItemInfoDict.Values)
            {
                if (item == null || item.IsPromotionItemHidden || item.IsPromotionItem)
                    continue;
                if (item.ItemType == ItemType.Consumable || item.ItemType == ItemType.Equipment || item.ItemType == ItemType.Miscellany)
                    items.Add(item);
            }
            return items;
        }
    }
}
