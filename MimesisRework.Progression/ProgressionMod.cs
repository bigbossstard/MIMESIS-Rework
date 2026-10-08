using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Globalization;
using Bifrost.ConstEnum;
using Bifrost.Cooked;
using Bifrost.ItemConsumable;
using Bifrost.ShopGroup;
using HarmonyLib;
using MelonLoader;
using Mimic.Actors;
using ReluProtocol;
using ReluProtocol.Enum;
using ReluNetwork.ConstEnum;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(MimesisRework.Progression.ProgressionMod), "MIMESIS Rework - Progression", "0.4.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisRework.Progression
{
    public sealed class ProgressionMod : MelonMod
    {
        internal const int FirstItemId = 990101;
        internal const int FirstShopGroupId = 19001;
        private const int PlaceholderModelSourceItemId = 3104;
        private static readonly FieldInfo InventorySelfField = AccessTools.Field(typeof(InventoryController), "_self");
        private static readonly PropertyInfo ItemInfoDictProperty = AccessTools.Property(typeof(ExcelDataManager), "ItemInfoDict");
        private static readonly PropertyInfo ShopGroupDictProperty = AccessTools.Property(typeof(ExcelDataManager), "ShopGroupDict");
        private static readonly PropertyInfo TableManagerProperty = AccessTools.Property(typeof(Hub), "tableman");
        private static readonly FieldInfo LocalizationManagerField = AccessTools.Field(typeof(Hub), "lcman");
        private static readonly FieldInfo LootingObjectTableField = AccessTools.Field(AccessTools.TypeByName("TableManager"), "lootingObject");
        private static readonly FieldInfo PdataField = AccessTools.Field(typeof(Hub), "pdata");
        private static readonly Dictionary<int, int> StaminaEfficiencyByPlayer = new Dictionary<int, int>();
        private static readonly HashSet<int> PurchasedUpgradeItemsThisDepot = new HashSet<int>();
        private static readonly UpgradeDefinition[] Upgrades =
        {
            new UpgradeDefinition(0, "HP", 3, 140), new UpgradeDefinition(1, "HP", 4, 165),
            new UpgradeDefinition(2, "HP", 5, 190), new UpgradeDefinition(3, "HP", 6, 220),
            new UpgradeDefinition(4, "Stamina", 3, 140), new UpgradeDefinition(5, "Stamina", 4, 165),
            new UpgradeDefinition(6, "Stamina", 5, 190), new UpgradeDefinition(7, "Stamina", 6, 220)
        };
        private static readonly Dictionary<int, UpgradeDefinition> ByItemId = Upgrades.ToDictionary(x => FirstItemId + x.Index);
        private static readonly HashSet<long> AppliedUseRequests = new HashSet<long>();
        private static readonly Queue<long> AppliedUseRequestOrder = new Queue<long>();
        private static bool _registered;
        private static string _purchaseDepotKey = string.Empty;
        private static UIPrefab_InGame _hpReadoutOwner;
        private static TMP_Text _hpReadout;
        private static long _lastLoggedHpMaximum = long.MinValue;
        private static int _hpReadoutOverrideActorId = -1;
        private static long _hpReadoutOverrideMaximum = long.MinValue;
        private static float _hpReadoutOverrideExpiresAt;

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(ProgressionMod).Assembly);
            LoggerInstance.Msg("Consumable health and stamina upgrade box module loaded; registration runs after vanilla master data initializes.");
        }

        private static bool RegisterUpgradeItems(ExcelDataManager data)
        {
            if (_registered || data == null || data.ItemInfoDict == null || data.ShopGroupDict == null) return _registered;
            string lootObjectId = SelectPlaceholderLootObjectId(data);
            if (string.IsNullOrEmpty(lootObjectId))
            {
                MelonLogger.Error("Upgrade box registration skipped: no valid vanilla item model could be resolved.");
                return false;
            }

            var items = data.ItemInfoDict.ToBuilder();
            var groups = data.ShopGroupDict.ToBuilder();
            foreach (UpgradeDefinition upgrade in Upgrades)
            {
                int itemId = FirstItemId + upgrade.Index;
                int groupId = FirstShopGroupId + upgrade.Index;
                if (items.ContainsKey(itemId) || groups.ContainsKey(groupId))
                {
                    MelonLogger.Error("Upgrade registration aborted because custom item/group ID is already occupied: " + itemId + "/" + groupId + ".");
                    return false;
                }

                var itemData = new ItemConsumable_MasterData
                {
                    id = itemId,
                    name = NameKey(upgrade.Index),
                    looting_object_id = lootObjectId,
                    vending_machine_tooltip_string = DescriptionKey(upgrade.Index),
                    consume_type = (int)ConsumeItemType.Potion,
                    max_stack_count = 1,
                    default_provide_count = 1,
                    weight = 0,
                    price_for_sell_min = 0,
                    price_for_sell_max = 0,
                    item_drop_id = 0,
                    attach_socket_name = string.Empty,
                    puppet_handheld_state = 0,
                    use_vending_machine_exchange = false,
                    is_preserved_on_wipe = false
                };
                itemData.tool_tip_string.Add(DescriptionKey(upgrade.Index));
                items.Add(itemId, new ItemConsumableInfo(itemData));

                var group = new ShopGroup_MasterData { id = groupId, item_masterid = itemId, item_price = upgrade.Price };
                group.ShopGroup_rateval.Add(new ShopGroup_rate { item_rate = 10000, discount_ratio = 0 });
                groups.Add(groupId, group);
            }

            ItemInfoDictProperty.GetSetMethod(true).Invoke(data, new object[] { items.ToImmutable() });
            ShopGroupDictProperty.GetSetMethod(true).Invoke(data, new object[] { groups.ToImmutable() });
            _registered = true;
            MelonLogger.Msg("Registered 8 custom consumables and shop groups 19001-19008. All use new item IDs; vanilla rows were left unchanged. Placeholder model row: " + lootObjectId + ".");
            return true;
        }

        private static string SelectPlaceholderLootObjectId(ExcelDataManager data)
        {
            object tableManager = TableManagerProperty?.GetValue(Hub.s);
            MMLootingObjectTable table = LootingObjectTableField?.GetValue(tableManager) as MMLootingObjectTable;
            if (table != null && table.rows != null)
            {
                string[] modelHints = { "box", "crate", "case", "package", "container", "supply" };
                MMLootingObjectTable.Row best = null;
                int bestScore = 0;
                foreach (MMLootingObjectTable.Row row in table.rows)
                {
                    if (row == null || row.prefab == null || string.IsNullOrEmpty(row.id) || row.prefab.GetComponent<LootingLevelObject>() == null) continue;
                    string candidate = (row.id + " " + row.prefab.name).ToLowerInvariant();
                    int score = modelHints.Count(hint => candidate.Contains(hint));
                    if (score > bestScore) { best = row; bestScore = score; }
                }
                if (best != null)
                {
                    MelonLogger.Msg("Using existing box-like item prefab as a temporary visual for upgrade boxes: " + best.id + "/" + best.prefab.name + ".");
                    return best.id;
                }
            }

            ItemMasterInfo fallback = data.GetItemInfo(PlaceholderModelSourceItemId);
            if (fallback != null && !string.IsNullOrEmpty(fallback.LootingObjectID)) return fallback.LootingObjectID;
            foreach (ItemMasterInfo item in data.ItemInfoDict.Values)
                if (item != null && !string.IsNullOrEmpty(item.LootingObjectID)) return item.LootingObjectID;
            return null;
        }

        private static string NameKey(int index) => "MIMESIS_REWORK_UPGRADE_NAME_" + index;
        private static string DescriptionKey(int index) => "MIMESIS_REWORK_UPGRADE_DESC_" + index;

        private static bool TryGetLocalizedText(string key, string language, out string text)
        {
            foreach (UpgradeDefinition upgrade in Upgrades)
            {
                if (key == NameKey(upgrade.Index))
                {
                    string stat = upgrade.Stat == "HP" ? (language == "ru" ? "здоровья" : "health") : (language == "ru" ? "выносливости" : "stamina");
                    text = language == "ru" ? "Улучшение " + stat : "" + char.ToUpperInvariant(stat[0]) + stat.Substring(1) + " Upgrade";
                    return true;
                }
                if (key == DescriptionKey(upgrade.Index))
                {
                    text = language == "ru"
                        ? (upgrade.Stat == "HP"
                            ? "Максимальное здоровье +" + upgrade.Percent + "% от исходного запаса. Текущее здоровье тоже восстанавливается на эту величину."
                            : "Расход выносливости при беге -" + upgrade.Percent + "%. Эффекты складываются до 50%.")
                        : (upgrade.Stat == "HP"
                            ? "Maximum health +" + upgrade.Percent + "% of base. Current health is also restored by the same amount."
                            : "Running stamina consumption -" + upgrade.Percent + "%. Effects stack up to 50%.");
                    return true;
                }
            }
            text = null;
            return false;
        }

        private static void ApplyConsumedUpgrade(InventoryController inventory, int itemId, int hashCode)
        {
            UpgradeDefinition upgrade;
            if (!ByItemId.TryGetValue(itemId, out upgrade) || Hub.s == null) return;
            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || pdata.ClientMode != NetworkClientMode.Host) return;
            VPlayer player = InventorySelfField?.GetValue(inventory) as VPlayer;
            if (player == null || player.StatControlUnit == null) return;

            long requestKey = ((long)player.ObjectID << 32) | (uint)hashCode;
            if (!AppliedUseRequests.Add(requestKey))
            {
                MelonLogger.Warning("Ignored duplicate upgrade-use request: player=" + player.ObjectID + ", hash=" + hashCode + ", item=" + itemId + ".");
                return;
            }
            AppliedUseRequestOrder.Enqueue(requestKey);
            while (AppliedUseRequestOrder.Count > 2048)
                AppliedUseRequests.Remove(AppliedUseRequestOrder.Dequeue());

            StatType statType = upgrade.Stat == "HP" ? StatType.HP : StatType.Stamina;
            long maxBefore = player.StatControlUnit.GetSpecificStatValue(statType);
            long baseMaximum = Math.Max(1L, maxBefore - player.StatControlUnit.GetGrowthStat(statType));
            if (statType == StatType.HP)
            {
                long amount = Math.Max(1L, baseMaximum * upgrade.Percent / 100L);
                player.StatControlUnit.AddGrowthStat(statType, amount);
                var recalculatedStats = new StatCollection();
                player.StatControlUnit.GetStatCollection(ref recalculatedStats);
                long newMaximum = player.StatControlUnit.GetSpecificStatValue(statType);
                SetLocalHpReadoutMaximum(player, newMaximum);
                MelonLogger.Msg("Consumed upgrade item " + itemId + ": HP max " + maxBefore + "→" + newMaximum + " (growth +" + amount + ", " + upgrade.Percent + "% base; request=" + hashCode + ").");
            }
            else
            {
                int previous;
                StaminaEfficiencyByPlayer.TryGetValue(player.ObjectID, out previous);
                int current = Math.Min(50, previous + upgrade.Percent);
                StaminaEfficiencyByPlayer[player.ObjectID] = current;
                MelonLogger.Msg("Consumed upgrade item " + itemId + ": running stamina cost reduction=" + current + "% (added " + (current - previous) + "%; request=" + hashCode + ").");
            }
        }

        private static int GetStaminaEfficiency(int playerId)
        {
            int percent;
            return StaminaEfficiencyByPlayer.TryGetValue(playerId, out percent) ? Math.Min(50, percent) : 0;
        }

        private static int ItemIdForGroup(int groupId)
        {
            return groupId >= FirstShopGroupId && groupId < FirstShopGroupId + Upgrades.Length
                ? FirstItemId + (groupId - FirstShopGroupId) : 0;
        }

        private static string CurrentDepotKey()
        {
            Hub.PersistentData pdata = PdataField?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || string.IsNullOrEmpty(pdata.ClientRoomSessionID)) return string.Empty;
            return pdata.ClientRoomSessionID + ":" + pdata.StageCount + ":" + pdata.CycleCount;
        }

        private static void RefreshDepotPurchaseState()
        {
            string key = CurrentDepotKey();
            if (string.IsNullOrEmpty(key) || key == _purchaseDepotKey) return;
            _purchaseDepotKey = key;
            PurchasedUpgradeItemsThisDepot.Clear();
        }

        private static bool WasUpgradePurchasedThisDepot(int itemId)
        {
            RefreshDepotPurchaseState();
            return PurchasedUpgradeItemsThisDepot.Contains(itemId);
        }

        private static void MarkUpgradePurchasedThisDepot(int itemId)
        {
            RefreshDepotPurchaseState();
            if (itemId != 0 && PurchasedUpgradeItemsThisDepot.Add(itemId))
                MelonLogger.Msg("Upgrade item " + itemId + " is now limited to one purchase for this depot visit.");
        }

        private static void UpdateHpReadout(UIPrefab_InGame ui, long current, long maximum)
        {
            if (ui == null) return;
            try
            {
                Image hpBar = ui.UE_HP_bar;
                if (hpBar == null) return;
                if (_hpReadoutOwner != ui || _hpReadout == null)
                {
                    Transform parent = hpBar.transform.parent;
                    if (parent == null) return;
                    var readoutObject = new GameObject("MimesisRework_HPReadout", typeof(RectTransform), typeof(TextMeshProUGUI));
                    readoutObject.transform.SetParent(parent, false);
                    RectTransform rect = (RectTransform)readoutObject.transform;
                    RectTransform barRect = hpBar.rectTransform;
                    rect.anchorMin = barRect.anchorMin;
                    rect.anchorMax = barRect.anchorMax;
                    rect.pivot = barRect.pivot;
                    rect.anchoredPosition = barRect.anchoredPosition + new Vector2(0f, -22f);
                    rect.sizeDelta = new Vector2(150f, 24f);
                    _hpReadout = readoutObject.GetComponent<TextMeshProUGUI>();
                    _hpReadout.font = parent.GetComponentInChildren<TMP_Text>(true)?.font ?? TMP_Settings.defaultFontAsset;
                    _hpReadout.fontSize = 16f;
                    _hpReadout.enableAutoSizing = true;
                    _hpReadout.fontSizeMin = 11f;
                    _hpReadout.fontSizeMax = 16f;
                    _hpReadout.color = Color.white;
                    _hpReadout.outlineWidth = 0.18f;
                    _hpReadout.outlineColor = Color.black;
                    _hpReadout.alignment = TextAlignmentOptions.Left;
                    _hpReadout.raycastTarget = false;
                    _hpReadoutOwner = ui;
                }
                _hpReadout.text = "HP " + FormatHp(current) + " / " + FormatHp(maximum);
                if (_lastLoggedHpMaximum != maximum)
                {
                    _lastLoggedHpMaximum = maximum;
                    MelonLogger.Msg("HP HUD numeric value synchronized: " + current + "/" + maximum + ".");
                }
            }
            catch (Exception ex) { MelonLogger.Warning("Native HP readout could not be updated: " + ex.Message); }
        }

        private static void SetLocalHpReadoutMaximum(VPlayer player, long maximum)
        {
            ProtoActor avatar = Hub.Main?.GetMyAvatar();
            if (player == null || avatar == null || avatar.ActorID != player.ObjectID) return;
            _hpReadoutOverrideActorId = player.ObjectID;
            _hpReadoutOverrideMaximum = maximum;
            _hpReadoutOverrideExpiresAt = Time.realtimeSinceStartup + 5f;
            if (_hpReadoutOwner != null)
                UpdateHpReadout(_hpReadoutOwner, player.StatControlUnit.GetCurrentHP(), maximum);
        }

        private static long GetHpReadoutMaximum(UIPrefab_InGame ui, long receivedMaximum)
        {
            if (_hpReadoutOverrideActorId < 0 || Time.realtimeSinceStartup > _hpReadoutOverrideExpiresAt)
            {
                _hpReadoutOverrideActorId = -1;
                _hpReadoutOverrideMaximum = long.MinValue;
                return receivedMaximum;
            }

            ProtoActor avatar = Hub.Main?.GetMyAvatar();
            if (avatar == null || avatar.ActorID != _hpReadoutOverrideActorId)
                return receivedMaximum;
            if (receivedMaximum >= _hpReadoutOverrideMaximum)
            {
                _hpReadoutOverrideActorId = -1;
                _hpReadoutOverrideMaximum = long.MinValue;
                return receivedMaximum;
            }
            return _hpReadoutOverrideMaximum;
        }

        private static string FormatHp(long raw)
        {
            return raw.ToString("N0", CultureInfo.InvariantCulture);
        }

        private sealed class UpgradeDefinition
        {
            internal UpgradeDefinition(int index, string stat, int percent, int price) { Index = index; Stat = stat; Percent = percent; Price = price; }
            internal readonly int Index;
            internal readonly string Stat;
            internal readonly int Percent;
            internal readonly int Price;
        }

        [HarmonyPatch(typeof(UIPrefab_InGame), nameof(UIPrefab_InGame.OnHpChanged))]
        private static class NativeHpReadoutPatch
        {
            private static void Postfix(UIPrefab_InGame __instance, long curr, long maxHP)
            {
                UpdateHpReadout(__instance, curr, GetHpReadoutMaximum(__instance, maxHP));
            }
        }

        [HarmonyPatch(typeof(StatController), nameof(StatController.ConsumeStamina))]
        private static class StaminaEfficiencyPatch
        {
            private static void Prefix(StatController __instance, ref long amount)
            {
                VPlayer player = __instance?.Self as VPlayer;
                if (player == null) return;
                int reduction = GetStaminaEfficiency(player.ObjectID);
                if (reduction > 0) amount = Math.Max(1L, amount * (100L - reduction) / 100L);
            }
        }

        [HarmonyPatch(typeof(VendingMachineLevelObject), "OnBuyItem")]
        private static class LimitUpgradePurchasePatch
        {
            private static void Postfix(VendingMachineLevelObject __instance, int newState)
            {
                if (__instance == null || newState != 1) return;
                MarkUpgradePurchasedThisDepot(ItemIdForGroup(__instance.shopGroupID));
            }
        }

        [HarmonyPatch(typeof(VendingMachineLevelObject), "IsTriggerable")]
        private static class BlockRepeatUpgradePurchasePatch
        {
            private static bool Prefix(VendingMachineLevelObject __instance, ref bool __result)
            {
                int itemId = __instance == null ? 0 : ItemIdForGroup(__instance.shopGroupID);
                if (itemId == 0 || !WasUpgradePurchasedThisDepot(itemId)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(VendingMachineLevelObject), nameof(VendingMachineLevelObject.GetSimpleText))]
        private static class ExplainUpgradePurchaseLimitPatch
        {
            private static void Postfix(VendingMachineLevelObject __instance, ref string __result)
            {
                int itemId = __instance == null ? 0 : ItemIdForGroup(__instance.shopGroupID);
                if (itemId == 0 || !WasUpgradePurchasedThisDepot(itemId)) return;
                L10NManager localizationManager = LocalizationManagerField?.GetValue(Hub.s) as L10NManager;
                __result = localizationManager != null && localizationManager.language == "ru"
                    ? "Это улучшение уже куплено в этом депо."
                    : "This upgrade was already purchased at this depot.";
            }
        }

        [HarmonyPatch(typeof(ExcelDataManager), "Initialize")]
        private static class RegisterUpgradeDataPatch
        {
            private static void Postfix(ExcelDataManager __instance, bool __result)
            {
                if (!__result) return;
                try { RegisterUpgradeItems(__instance); }
                catch (Exception ex) { MelonLogger.Error("Upgrade item/table injection failed safely: " + ex); }
            }
        }

        [HarmonyPatch(typeof(Hub), "GetL10NText")]
        private static class UpgradeLocalizationPatch
        {
            private static void Postfix(string key, ref string __result)
            {
                L10NManager localizationManager = LocalizationManagerField?.GetValue(Hub.s) as L10NManager;
                string language = localizationManager == null ? "en" : localizationManager.language;
                string text;
                if (TryGetLocalizedText(key, language, out text)) __result = text;
            }
        }

        [HarmonyPatch(typeof(InventoryController), nameof(InventoryController.UseItem))]
        private static class ConsumeUpgradePatch
        {
            private static void Prefix(InventoryController __instance, int hashCode, out int __state)
            {
                __state = __instance == null ? 0 : __instance.GetCurrentInventorySlotItemMasterID();
            }

            private static void Postfix(InventoryController __instance, int hashCode, bool sync, MsgErrorCode __result, int __state)
            {
                if (!sync || __result != MsgErrorCode.Success || __state < FirstItemId || __state >= FirstItemId + Upgrades.Length) return;
                try { ApplyConsumedUpgrade(__instance, __state, hashCode); }
                catch (Exception ex) { MelonLogger.Error("Applying consumed upgrade failed: " + ex); }
            }
        }
    }
}
