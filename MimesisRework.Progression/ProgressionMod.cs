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
        private const string HealthBoxLootObjectId = "mimesis_upgrade_health_box";
        private const string StaminaBoxLootObjectId = "mimesis_upgrade_stamina_box";
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
            if (Upgrades.Any(upgrade => data.ItemInfoDict.ContainsKey(FirstItemId + upgrade.Index) ||
                                        data.ShopGroupDict.ContainsKey(FirstShopGroupId + upgrade.Index)))
            {
                MelonLogger.Error("Upgrade registration aborted because a custom item or shop-group ID is already occupied.");
                return false;
            }

            if (!TryRegisterUpgradeBoxPrefabs(out string error))
            {
                MelonLogger.Error("Upgrade item registration skipped: " + error);
                return false;
            }

            var items = data.ItemInfoDict.ToBuilder();
            var groups = data.ShopGroupDict.ToBuilder();
            foreach (UpgradeDefinition upgrade in Upgrades)
            {
                int itemId = FirstItemId + upgrade.Index;
                int groupId = FirstShopGroupId + upgrade.Index;

                var itemData = new ItemConsumable_MasterData
                {
                    id = itemId,
                    name = NameKey(upgrade.Index),
                    looting_object_id = upgrade.Stat == "HP" ? HealthBoxLootObjectId : StaminaBoxLootObjectId,
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
            MelonLogger.Msg("Registered 8 custom consumables and shop groups 19001-19008 with separate health-cross and stamina-lightning box prefabs. Vanilla item rows and loot prefabs were left unchanged.");
            return true;
        }

        private static bool TryRegisterUpgradeBoxPrefabs(out string error)
        {
            error = null;
            object tableManager = TableManagerProperty?.GetValue(Hub.s);
            MMLootingObjectTable table = LootingObjectTableField?.GetValue(tableManager) as MMLootingObjectTable;
            if (table == null || table.rows == null)
            {
                error = "the native looting-object table is unavailable.";
                return false;
            }

            MMLootingObjectTable.Row existingHealth = table.rows.FirstOrDefault(row => row != null && row.id == HealthBoxLootObjectId);
            MMLootingObjectTable.Row existingStamina = table.rows.FirstOrDefault(row => row != null && row.id == StaminaBoxLootObjectId);
            if (existingHealth != null || existingStamina != null)
            {
                if (existingHealth?.prefab != null && existingStamina?.prefab != null)
                    return true;
                error = "only one custom upgrade-box prefab row is present.";
                return false;
            }

            const string cardboardBoxModelId = "miscellnary_cardboardbox";
            MMLootingObjectTable.Row source = table.rows.FirstOrDefault(row =>
                row != null && row.id == cardboardBoxModelId && row.prefab != null &&
                row.prefab.GetComponent<LootingLevelObject>() != null);
            if (source == null)
            {
                error = "the native cardboard-box prefab could not be found.";
                return false;
            }

            GameObject healthPrefab = null;
            GameObject staminaPrefab = null;
            try
            {
                healthPrefab = CreateUpgradeBoxPrefab(source.prefab, true);
                staminaPrefab = CreateUpgradeBoxPrefab(source.prefab, false);
                table.rows.Add(CloneLootingObjectRow(source, HealthBoxLootObjectId, healthPrefab));
                table.rows.Add(CloneLootingObjectRow(source, StaminaBoxLootObjectId, staminaPrefab));
                MelonLogger.Msg("Created independent upgrade-box prefabs from the native cardboard box; added only custom loot rows.");
                return true;
            }
            catch
            {
                if (healthPrefab != null)
                    UnityEngine.Object.Destroy(healthPrefab);
                if (staminaPrefab != null)
                    UnityEngine.Object.Destroy(staminaPrefab);
                throw;
            }
        }

        private static MMLootingObjectTable.Row CloneLootingObjectRow(
            MMLootingObjectTable.Row source, string id, GameObject prefab)
        {
            return new MMLootingObjectTable.Row
            {
                id = id,
                prefab = prefab,
                iconSpriteId = source.iconSpriteId,
                posterIconSpriteId = source.posterIconSpriteId,
                pickAudioClipId = source.pickAudioClipId,
                dropAudioClipId = source.dropAudioClipId
            };
        }

        private static GameObject CreateUpgradeBoxPrefab(GameObject source, bool healthBox)
        {
            GameObject staging = new GameObject("MimesisRework_UpgradeBox_Staging");
            staging.SetActive(false);
            GameObject prefab = UnityEngine.Object.Instantiate(source, staging.transform, false);
            prefab.name = healthBox ? "MimesisRework_HealthUpgradeBox" : "MimesisRework_StaminaUpgradeBox";
            AddUpgradeBadge(prefab, healthBox);
            prefab.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(staging);
            UnityEngine.Object.DontDestroyOnLoad(prefab);
            return prefab;
        }

        private static void AddUpgradeBadge(GameObject prefab, bool healthBox)
        {
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default(Bounds);
            bool hasBounds = false;
            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;
                Bounds localBounds = renderer.localBounds;
                for (int mask = 0; mask < 8; mask++)
                {
                    Vector3 corner = localBounds.center + Vector3.Scale(localBounds.extents, new Vector3(
                        (mask & 1) == 0 ? -1f : 1f,
                        (mask & 2) == 0 ? -1f : 1f,
                        (mask & 4) == 0 ? -1f : 1f));
                    Vector3 rootPoint = rootInverse.MultiplyPoint3x4(renderer.transform.TransformPoint(corner));
                    if (!hasBounds)
                    {
                        bounds = new Bounds(rootPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootPoint);
                    }
                }
            }

            if (!hasBounds)
                throw new InvalidOperationException("Native cardboard box has no renderable geometry.");

            int normalAxis = SmallestAxis(bounds.size);
            int uAxis = (normalAxis + 1) % 3;
            int vAxis = (normalAxis + 2) % 3;
            float iconSize = Mathf.Min(Axis(bounds.size, uAxis), Axis(bounds.size, vAxis)) * 0.48f;
            if (iconSize <= 0.005f)
                throw new InvalidOperationException("Native cardboard box is too small for an upgrade badge.");

            Vector3 faceCenter = bounds.center;
            SetAxis(ref faceCenter, normalAxis, Axis(bounds.center, normalAxis) + Axis(bounds.extents, normalAxis) + 0.001f);
            Material sourceMaterial = renderers.First(renderer => renderer != null && renderer.sharedMaterial != null).sharedMaterial;
            Material badgeMaterial = CreateBadgeMaterial(sourceMaterial, new Color(0.055f, 0.052f, 0.044f, 1f));
            Material symbolMaterial = CreateBadgeMaterial(sourceMaterial,
                healthBox ? new Color(0.18f, 0.62f, 0.32f, 1f) : new Color(0.96f, 0.7f, 0.16f, 1f));

            float thickness = Mathf.Max(0.0015f, iconSize * 0.035f);
            float halfBadge = iconSize * 0.62f;
            AddBadgeBar(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                -halfBadge, halfBadge, -halfBadge, halfBadge, thickness, badgeMaterial, "Dark badge backing");

            float halfSymbol = iconSize * 0.40f;
            float barHalfWidth = iconSize * 0.075f;
            if (healthBox)
            {
                AddBadgeBar(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                    -halfSymbol, halfSymbol, -barHalfWidth, barHalfWidth, thickness * 1.2f, symbolMaterial, "Health cross horizontal");
                AddBadgeBar(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                    -barHalfWidth, barHalfWidth, -halfSymbol, halfSymbol, thickness * 1.2f, symbolMaterial, "Health cross vertical");
            }
            else
            {
                AddBadgeSegment(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                    new Vector2(-0.12f, 0.40f) * iconSize, new Vector2(0.10f, 0.10f) * iconSize, barHalfWidth, thickness * 1.2f, symbolMaterial, "Stamina bolt upper");
                AddBadgeSegment(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                    new Vector2(0.10f, 0.10f) * iconSize, new Vector2(-0.08f, 0.10f) * iconSize, barHalfWidth, thickness * 1.2f, symbolMaterial, "Stamina bolt middle");
                AddBadgeSegment(prefab.transform, uAxis, vAxis, normalAxis, faceCenter,
                    new Vector2(-0.08f, 0.10f) * iconSize, new Vector2(0.12f, -0.42f) * iconSize, barHalfWidth, thickness * 1.2f, symbolMaterial, "Stamina bolt lower");
            }
        }

        private static Material CreateBadgeMaterial(Material source, Color color)
        {
            Material material = new Material(source);
            material.name = source.name + "_MimesisUpgradeBadge";
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            return material;
        }

        private static void AddBadgeBar(
            Transform parent, int uAxis, int vAxis, int normalAxis, Vector3 center,
            float minU, float maxU, float minV, float maxV, float thickness, Material material, string name)
        {
            Vector2 start = new Vector2(minU, (minV + maxV) * 0.5f);
            Vector2 end = new Vector2(maxU, (minV + maxV) * 0.5f);
            Vector2 perpendicular = new Vector2(0f, (maxV - minV) * 0.5f);
            AddBadgeMesh(parent, uAxis, vAxis, normalAxis, center, start, end, perpendicular, thickness, material, name, minV, maxV);
        }

        private static void AddBadgeSegment(
            Transform parent, int uAxis, int vAxis, int normalAxis, Vector3 center,
            Vector2 start, Vector2 end, float halfWidth, float thickness, Material material, string name)
        {
            Vector2 direction = (end - start).normalized;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x) * halfWidth;
            AddBadgeMesh(parent, uAxis, vAxis, normalAxis, center, start, end, perpendicular, thickness, material, name, 0f, 0f);
        }

        private static void AddBadgeMesh(
            Transform parent, int uAxis, int vAxis, int normalAxis, Vector3 center,
            Vector2 start, Vector2 end, Vector2 perpendicular, float thickness,
            Material material, string name, float minV, float maxV)
        {
            if (minV != maxV)
            {
                start = new Vector2(start.x, (minV + maxV) * 0.5f);
                end = new Vector2(end.x, (minV + maxV) * 0.5f);
            }

            Vector2[] corners =
            {
                start + perpendicular, end + perpendicular,
                end - perpendicular, start - perpendicular
            };
            Vector3[] vertices = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = center;
                SetAxis(ref point, uAxis, Axis(center, uAxis) + corners[i].x);
                SetAxis(ref point, vAxis, Axis(center, vAxis) + corners[i].y);
                vertices[i] = point;
                vertices[i + 4] = point + AxisVector(normalAxis) * thickness;
            }

            Mesh mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.triangles = new[]
            {
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
                4, 5, 6, 4, 6, 7, 3, 2, 1, 3, 1, 0
            };
            mesh.RecalculateNormals();

            GameObject badge = new GameObject(name);
            badge.layer = parent.gameObject.layer;
            badge.transform.SetParent(parent, false);
            MeshFilter filter = badge.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = badge.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
        }

        private static int SmallestAxis(Vector3 size)
        {
            if (size.x <= size.y && size.x <= size.z) return 0;
            return size.y <= size.z ? 1 : 2;
        }

        private static float Axis(Vector3 vector, int axis)
        {
            return axis == 0 ? vector.x : axis == 1 ? vector.y : vector.z;
        }

        private static void SetAxis(ref Vector3 vector, int axis, float value)
        {
            if (axis == 0) vector.x = value;
            else if (axis == 1) vector.y = value;
            else vector.z = value;
        }

        private static Vector3 AxisVector(int axis)
        {
            return axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
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
