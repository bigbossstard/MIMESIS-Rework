using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using Bifrost.ShopGroup;
using Bifrost.Cooked;

[assembly: MelonInfo(typeof(MimesisRework.Market.MarketMod), "MIMESIS Rework - Market", "0.6.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisRework.Market
{
    public sealed class MarketMod : MelonMod
    {
        private static readonly int[] VanillaShopGroups =
        {
            1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008, 1009,
            1010, 1011, 1012, 1013, 1014, 1015, 1016, 1017
        };
        private static readonly int[] GuaranteedGroups = { 1001, 1006 };
        private static readonly HashSet<int> ExcludedVanillaItemIds = new HashSet<int> { 5013, 5014, 5015, 5016 };
        private static readonly int[] UpgradeShopGroups = { 19001, 19002, 19003, 19004, 19005, 19006, 19007, 19008 };
        private const int TrialMachineCount = 4;
        private const float TrialRowOffset = 3.2f;
        private const float DisplayTargetMaxSize = 0.38f;
        private const float DisplayMinSize = 0.20f;
        private const string TrialMachineNamePrefix = "MimesisRework_TrialVending_";
        private static readonly FieldInfo PdataField = AccessTools.Field(typeof(Hub), "pdata");
        private static readonly PropertyInfo DataManagerProperty = AccessTools.Property(typeof(Hub), "dataman");
        private static readonly FieldInfo RoomLevelObjectsField = AccessTools.Field(typeof(IVroom), "_levelObjects");
        private static readonly FieldInfo RoomPricesField = AccessTools.Field(typeof(MaintenanceRoom), "_priceForItems");
        private static readonly FieldInfo ItemRootField = AccessTools.Field(typeof(VendingMachineLevelObject), "vendingMachineItemRoot");
        private static readonly FieldInfo VanillaRootField = AccessTools.Field(typeof(VendingMachineLevelObject), "vendingMachineVanillaRoot");
        private static readonly FieldInfo SpawnedItemField = AccessTools.Field(typeof(VendingMachineLevelObject), "vendingMachineItem");
        private static readonly HashSet<int> RandomizedMachineIds = new HashSet<int>();
        private string _lastClientRoll = string.Empty;
        private static Hub.PersistentData Pdata => PdataField?.GetValue(Hub.s) as Hub.PersistentData;
        private static DataManager Data => DataManagerProperty?.GetValue(Hub.s) as DataManager;

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(MarketMod).Assembly);
            LoggerInstance.Msg("Physical vending randomizer loaded. Four trial clones target the MaintenanceScene shop (FirstEnter and later depot visits), before level-object scanning.");
        }

        private static void AddTrialMachinesBeforeRegistration(DynamicDataManager manager, Transform rootNode)
        {
            if (Hub.s == null || rootNode == null) return;
            GameMainBase main = Pdata?.main as GameMainBase;
            if (!(main is MaintenanceScene) || main.GetBGRoot() != rootNode) return;

            var existing = GetMachines();
            int alreadyAdded = 0;
            foreach (VendingMachineLevelObject machine in existing)
                if (machine.name.StartsWith(TrialMachineNamePrefix, StringComparison.Ordinal)) alreadyAdded++;
            if (alreadyAdded > 0)
            {
                MelonLogger.Msg("Depot trial vending clones already present: " + alreadyAdded + ".");
                return;
            }

            var vanillaMachines = new List<VendingMachineLevelObject>();
            foreach (VendingMachineLevelObject machine in existing)
                if (machine != null && machine.transform.IsChildOf(rootNode) && !machine.name.StartsWith(TrialMachineNamePrefix, StringComparison.Ordinal))
                    vanillaMachines.Add(machine);
            if (vanillaMachines.Count == 0)
            {
                MelonLogger.Warning("Trial vending clone pass skipped: no vanilla vending machines found under the depot root.");
                return;
            }

            int targetCount = Math.Min(TrialMachineCount, vanillaMachines.Count);
            int cloneCount = 0;
            for (int i = 0; i < targetCount; i++)
            {
                try
                {
                    VendingMachineLevelObject source = vanillaMachines[i];
                    GameObject cloneObject = UnityEngine.Object.Instantiate(source.gameObject, rootNode, true);
                    cloneObject.name = TrialMachineNamePrefix + (i + 1);
                    VendingMachineLevelObject clone = cloneObject.GetComponent<VendingMachineLevelObject>();
                    if (clone == null)
                    {
                        UnityEngine.Object.Destroy(cloneObject);
                        MelonLogger.Warning("Trial vending clone did not contain VendingMachineLevelObject; clone discarded.");
                        continue;
                    }

                    // Put a second row farther out and face it toward the original row, leaving an aisle between the fronts.
                    clone.transform.position = source.transform.position + source.transform.forward * TrialRowOffset;
                    clone.transform.rotation = Quaternion.AngleAxis(180f, Vector3.up) * source.transform.rotation;
                    cloneCount++;
                    MelonLogger.Msg("Created trial vending clone " + clone.name + " beside source machine " + source.levelObjectID + ".");
                }
                catch (Exception ex) { MelonLogger.Error("Trial vending clone failed; keeping the vanilla machine set. " + ex); }
            }
            MelonLogger.Msg("MaintenanceScene trial vending clone pass complete: " + cloneCount + " clone(s); DynamicDataManager.Build will assign their IDs. LogisticsScene repair vendings are intentionally excluded.");
        }

        [HarmonyPatch(typeof(DynamicDataManager), "Build")]
        private static class DepotTrialMachinePatch
        {
            private static void Prefix(DynamicDataManager __instance, Transform rootNode) => AddTrialMachinesBeforeRegistration(__instance, rootNode);
        }

        public override void OnUpdate()
        {
            try { ApplyClientRollWhenDepotIsReady(); }
            catch (Exception ex) { LoggerInstance.Error("Client stock refresh failed: " + ex); }
        }

        private void ApplyClientRollWhenDepotIsReady()
        {
            if (Hub.s == null) return;
            Hub.PersistentData pdata = PdataField?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || string.IsNullOrEmpty(pdata.ClientRoomSessionID) || pdata.itemPrices == null || pdata.itemPrices.Count == 0) return;
            string key = MakeRollKey(pdata.ClientRoomSessionID, pdata.StageCount, pdata.CycleCount);
            if (key == _lastClientRoll) return;
            if (!ApplyAssignments(key, out string diagnostic)) return;
            _lastClientRoll = key;
            foreach (VendingMachineLevelObject machine in GetMachines()) machine.Refresh();
            ValidateClientPriceRoutes(pdata);
            LoggerInstance.Msg("Depot stock for " + key + ": " + diagnostic);
        }

        private static void BeforeServerBuildLevel(IVroom room)
        {
            if (!(room is MaintenanceRoom)) return;
            string key = MakeRollKey(room.SessionID.ToString(), room.CurrentStage, room.CurrentCycle);
            if (ApplyAssignments(key, out string diagnostic))
                MelonLogger.Msg("Depot stock for " + key + ": " + diagnostic);
        }

        private static void AfterServerBuildLevel(IVroom room)
        {
            if (!(room is MaintenanceRoom) || Data == null) return;
            IDictionary levelObjects = RoomLevelObjectsField?.GetValue(room) as IDictionary;
            if (levelObjects == null) return;
            int corrected = 0;
            foreach (VendingMachineLevelObject machine in GetMachines())
            {
                if (!RandomizedMachineIds.Contains(machine.levelObjectID)) continue;
                InsertLevelObjectInfo info = levelObjects[machine.levelObjectID] as InsertLevelObjectInfo;
                if (info == null || info.InsertLevelObjectType != InsertLevelObjectType.VendingMachine) continue;
                ValueTuple<int, int, float> shop = Data.ExcelDataManager.GetShopGroupInfo(machine.shopGroupID);
                if (shop.Item1 == 0) continue;
                info.OutputItemMasterID = shop.Item1;
                info.InputAmount = shop.Item2;
                info.DiscountRate = shop.Item3;
                corrected++;
            }
            if (corrected != RandomizedMachineIds.Count)
                MelonLogger.Warning("Server vending route mismatch: corrected " + corrected + " of " + RandomizedMachineIds.Count + " machines.");
            else
                MelonLogger.Msg("Server purchase routes synchronized for " + corrected + " randomized machines.");
        }

        private static void ValidateClientPriceRoutes(Hub.PersistentData pdata)
        {
            foreach (VendingMachineLevelObject machine in GetMachines())
            {
                if (!RandomizedMachineIds.Contains(machine.levelObjectID)) continue;
                ShopGroup_MasterData row;
                if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(machine.shopGroupID, out row) != true || row == null || !pdata.itemPrices.ContainsKey(row.item_masterid))
                    MelonLogger.Warning("Client display/price mismatch: machine " + machine.levelObjectID + ", group " + machine.shopGroupID + ".");
            }
        }

        private static void ValidateServerPriceRoutes(MaintenanceRoom room)
        {
            IDictionary levelObjects = RoomLevelObjectsField?.GetValue(room) as IDictionary;
            IDictionary prices = RoomPricesField?.GetValue(room) as IDictionary;
            if (levelObjects == null || prices == null) return;
            int valid = 0;
            foreach (VendingMachineLevelObject machine in GetMachines())
            {
                if (!RandomizedMachineIds.Contains(machine.levelObjectID)) continue;
                InsertLevelObjectInfo info = levelObjects[machine.levelObjectID] as InsertLevelObjectInfo;
                ShopGroup_MasterData row;
                if (info != null && Data?.ExcelDataManager.ShopGroupDict.TryGetValue(machine.shopGroupID, out row) == true && row != null &&
                    info.OutputItemMasterID == row.item_masterid && prices.Contains(info.OutputItemMasterID))
                {
                    valid++;
                }
                else
                {
                    MelonLogger.Error("Server purchase route mismatch: machine " + machine.levelObjectID + ", group " + machine.shopGroupID +
                        ", output " + (info == null ? -1 : info.OutputItemMasterID) + ".");
                }
            }
            MelonLogger.Msg("Server route validation: " + valid + "/" + RandomizedMachineIds.Count + " machines agree on item IDs and price entries.");
        }

        private static bool ApplyAssignments(string key, out string diagnostic)
        {
            diagnostic = string.Empty;
            var machines = GetMachines();
            if (machines.Count == 0) return false;

            var vanillaMachines = new List<VendingMachineLevelObject>();
            var upgradeMachines = new List<VendingMachineLevelObject>();
            foreach (VendingMachineLevelObject machine in machines)
            {
                if (machine.name.StartsWith(TrialMachineNamePrefix, StringComparison.Ordinal)) upgradeMachines.Add(machine);
                else vanillaMachines.Add(machine);
            }

            var available = new List<int>();
            foreach (int groupId in VanillaShopGroups)
            {
                ShopGroup_MasterData row;
                if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(groupId, out row) == true && row != null && !ExcludedVanillaItemIds.Contains(row.item_masterid))
                    available.Add(groupId);
            }
            if (available.Count < vanillaMachines.Count) return false;

            var random = new System.Random(StableHash(key));
            var selectedVanilla = new List<int>();
            foreach (int guaranteed in GuaranteedGroups)
                if (selectedVanilla.Count < vanillaMachines.Count && available.Contains(guaranteed)) selectedVanilla.Add(guaranteed);
            available.RemoveAll(id => selectedVanilla.Contains(id));
            Shuffle(available, random);

            var availableUpgrades = new List<int>();
            foreach (int groupId in UpgradeShopGroups)
            {
                ShopGroup_MasterData row;
                if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(groupId, out row) == true && row != null)
                    availableUpgrades.Add(groupId);
            }

            if (upgradeMachines.Count > availableUpgrades.Count) return false;
            var selectedUpgrades = new List<int>();
            if (upgradeMachines.Count >= 2 && availableUpgrades.Count >= upgradeMachines.Count)
            {
                int hpGroup = availableUpgrades[random.Next(0, 4)];
                int staminaGroup = availableUpgrades[random.Next(4, 8)];
                selectedUpgrades.Add(hpGroup);
                selectedUpgrades.Add(staminaGroup);
                availableUpgrades.Remove(hpGroup);
                availableUpgrades.Remove(staminaGroup);
                Shuffle(availableUpgrades, random);
            }
            while (selectedUpgrades.Count < upgradeMachines.Count)
            {
                if (availableUpgrades.Count == 0) return false;
                selectedUpgrades.Add(availableUpgrades[0]);
                availableUpgrades.RemoveAt(0);
            }
            while (selectedVanilla.Count < vanillaMachines.Count)
            {
                if (available.Count == 0) return false;
                selectedVanilla.Add(available[0]);
                available.RemoveAt(0);
            }
            Shuffle(selectedUpgrades, random);
            Shuffle(selectedVanilla, random);

            var descriptions = new List<string>();
            RandomizedMachineIds.Clear();
            for (int i = 0; i < upgradeMachines.Count; i++)
            {
                upgradeMachines[i].shopGroupID = selectedUpgrades[i];
                RandomizedMachineIds.Add(upgradeMachines[i].levelObjectID);
                ShopGroup_MasterData row;
                if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(selectedUpgrades[i], out row) == true && row != null)
                    descriptions.Add(upgradeMachines[i].levelObjectID + "→upgrade group " + selectedUpgrades[i] + "/item " + row.item_masterid);
            }
            for (int i = 0; i < vanillaMachines.Count; i++)
            {
                vanillaMachines[i].shopGroupID = selectedVanilla[i];
                RandomizedMachineIds.Add(vanillaMachines[i].levelObjectID);
                ShopGroup_MasterData row;
                if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(selectedVanilla[i], out row) == true && row != null)
                    descriptions.Add(vanillaMachines[i].levelObjectID + "→vanilla group " + selectedVanilla[i] + "/item " + row.item_masterid);
            }
            diagnostic = string.Join(", ", descriptions);
            return true;
        }

        private static List<VendingMachineLevelObject> GetMachines()
        {
            var result = new List<VendingMachineLevelObject>(UnityEngine.Object.FindObjectsOfType<VendingMachineLevelObject>());
            result.Sort((a, b) => a.levelObjectID.CompareTo(b.levelObjectID));
            return result;
        }

        private static string MakeRollKey(string session, int stage, int cycle) => session + ":" + stage + ":" + cycle;

        private static int StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text) { hash ^= c; hash *= 16777619; }
                return (int)hash;
            }
        }

        private static void Shuffle<T>(IList<T> list, System.Random random, int keepFirst = 0)
        {
            for (int i = list.Count - 1; i > keepFirst; i--)
            {
                int j = random.Next(keepFirst, i + 1);
                T value = list[i]; list[i] = list[j]; list[j] = value;
            }
        }

        [HarmonyPatch(typeof(IVroom), "InitLevel")]
        private static class ServerStockPatch
        {
            private static void Prefix(IVroom __instance) => BeforeServerBuildLevel(__instance);
            private static void Postfix(IVroom __instance) => AfterServerBuildLevel(__instance);
        }

        [HarmonyPatch(typeof(MaintenanceRoom), "InitShopItems")]
        private static class ServerShopPricePatch
        {
            private static void Postfix(MaintenanceRoom __instance) => ValidateServerPriceRoutes(__instance);
        }

        // Vanilla only shows the original display prop when the item is not skinned.
        // For randomized stock, always show the actual item's existing prefab.
        [HarmonyPatch(typeof(VendingMachineLevelObject), "SetVendingMachineItemProcess")]
        private static class VendingDisplayPatch
        {
            private static bool Prefix(VendingMachineLevelObject __instance, ref IEnumerator __result)
            {
                if (!RandomizedMachineIds.Contains(__instance.levelObjectID)) return true;
                __result = SpawnActualStockItem(__instance);
                return false;
            }
        }

        private static IEnumerator SpawnActualStockItem(VendingMachineLevelObject machine)
        {
            yield return new WaitUntil(() => Hub.s != null && Pdata != null && Pdata.itemPrices.Count > 0);
            Hub.PersistentData pdata = Pdata;
            Transform itemRoot = ItemRootField?.GetValue(machine) as Transform;
            Transform vanillaRoot = VanillaRootField?.GetValue(machine) as Transform;
            if (itemRoot == null) yield break;

            GameObject oldItem = SpawnedItemField?.GetValue(machine) as GameObject;
            if (oldItem != null) UnityEngine.Object.DestroyImmediate(oldItem);
            SpawnedItemField?.SetValue(machine, null);

            ShopGroup_MasterData row;
            if (Data?.ExcelDataManager.ShopGroupDict.TryGetValue(machine.shopGroupID, out row) != true || row == null)
                yield break;
            ValueTuple<ItemMasterInfo, int, float> priceInfo;
            if (!pdata.itemPrices.TryGetValue(row.item_masterid, out priceInfo)) yield break;

            var spawned = pdata.main.TrySpawnItemObject(row.item_masterid, itemRoot);
            GameObject actualItem = spawned.Item2;
            if (actualItem != null)
            {
                SpawnedItemField?.SetValue(machine, actualItem);
                if (vanillaRoot != null) vanillaRoot.gameObject.SetActive(false);
                yield return null;
                NormalizeDisplayItemScale(actualItem, machine);
            }
            else if (vanillaRoot != null)
            {
                vanillaRoot.gameObject.SetActive(true);
            }
        }

        private static void NormalizeDisplayItemScale(GameObject item, VendingMachineLevelObject machine)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default(Bounds);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!found) return;
            float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (size <= 0.001f) return;
            float target = Mathf.Clamp(size, DisplayMinSize, DisplayTargetMaxSize);
            float factor = target / size;
            item.transform.localScale *= factor;
            MelonLogger.Msg("Normalized vending display item scale: " + machine.name + " model=" + item.name + ", maxBounds=" + size.ToString("F2") + "m, factor=" + factor.ToString("F2") + ".");
        }
    }
}
