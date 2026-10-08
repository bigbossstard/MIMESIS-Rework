using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using HarmonyLib;
using Bifrost.Cooked;
using Bifrost.ConstEnum;
using ReluNetwork.ConstEnum;
using ReluProtocol.Enum;
using Mimic.Actors;
using UnityEngine;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(MimesisTestMod.Core), "MIMESIS Rework - Dev Tools", "0.3.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisTestMod
{
    public sealed class Core : MelonMod
    {
        private static Core _instance;
        private bool _showPanel = true;
        private float _loadedAt;
        private string _devStatus = "Host-only dev controls";
        private static readonly System.Random DevRandom = new System.Random();
        internal static bool InfiniteMoneyEnabled { get; private set; }
        internal static bool InfiniteHPEnabled { get; private set; }
        internal static bool InfiniteShotgunChargeEnabled { get; private set; }
        private const int RamblamShotgunItemMasterId = 1002;

        public override void OnInitializeMelon()
        {
            _instance = this;
            _loadedAt = Time.realtimeSinceStartup;
            LoggerInstance.Msg("MIMESIS Test Mod loaded successfully.");
            LoggerInstance.Msg("Press F8 in-game to toggle the test panel.");
        }

        internal static void TogglePanelFromPatch()
        {
            if (_instance == null)
                return;
            _instance._showPanel = !_instance._showPanel;
            _instance.LoggerInstance.Msg("Test panel toggled from ProtoActor.Update: " + (_instance._showPanel ? "shown" : "hidden"));
        }

        public override void OnGUI()
        {
            if (!_showPanel)
                return;

            GUI.Box(new Rect(20f, 20f, 420f, 328f), "MIMESIS REWORK TEST MOD");
            GUI.Label(new Rect(35f, 50f, 390f, 24f), "Test mod is active");
            GUI.Label(new Rect(35f, 72f, 390f, 20f), "F8: toggle panel | controls require host");
            if (GUI.Button(new Rect(35f, 98f, 185f, 30f), "Spawn random monster"))
                RunDevAction(SpawnRandomMonster);
            if (GUI.Button(new Rect(230f, 98f, 185f, 30f), "Spawn random item"))
                RunDevAction(SpawnRandomItem);
            if (GUI.Button(new Rect(35f, 134f, 185f, 30f), "Kill all monsters"))
                RunDevAction(KillAllMonsters);
            if (GUI.Button(new Rect(230f, 134f, 185f, 30f), InfiniteMoneyEnabled ? "Disable infinite money" : "Enable infinite money"))
                RunDevAction(ToggleInfiniteMoney);
            if (GUI.Button(new Rect(35f, 170f, 185f, 30f), "Give shotgun"))
                RunDevAction(GiveShotgun);
            if (GUI.Button(new Rect(230f, 170f, 185f, 30f), InfiniteHPEnabled ? "Disable infinite HP" : "Enable infinite HP"))
                RunDevAction(ToggleInfiniteHP);
            if (GUI.Button(new Rect(35f, 206f, 185f, 30f), "Ramblam shotgun"))
                RunDevAction(GiveRamblamShotgun);
            GUI.Label(new Rect(35f, 242f, 390f, 35f), _devStatus);
            GUI.Label(new Rect(35f, 279f, 390f, 20f), "Feature modules load independently");
            GUI.Label(new Rect(35f, 302f, 390f, 20f), "Loaded: " + (Time.realtimeSinceStartup - _loadedAt).ToString("0.0") + " s ago");
        }

        private void RunDevAction(Action<IVroom, VPlayer> action)
        {
            try
            {
                if (!TryGetHostPlayer(out IVroom room, out VPlayer player, out string reason))
                {
                    _devStatus = reason;
                    return;
                }
                action(room, player);
            }
            catch (Exception ex)
            {
                _devStatus = "Action failed: " + ex.GetType().Name + " (see MelonLoader log)";
                LoggerInstance.Error("Dev action failed: " + ex);
            }
        }

        private bool TryGetHostPlayer(out IVroom room, out VPlayer player, out string reason)
        {
            room = null;
            player = null;
            reason = "Start or join a game as host first.";
            if (Hub.s == null || Hub.Main == null)
                return false;

            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || pdata.ClientMode != NetworkClientMode.Host)
            {
                reason = "Dev controls are host-only.";
                return false;
            }

            ProtoActor avatar = Hub.Main.GetMyAvatar();
            if (avatar == null)
            {
                reason = "Player avatar is not ready yet.";
                return false;
            }

            VWorld world = AccessTools.Property(typeof(Hub), "vworld")?.GetValue(Hub.s) as VWorld;
            VRoomManager manager = world?.VRoomManager;
            var rooms = manager == null ? null : AccessTools.Field(typeof(VRoomManager), "_vrooms")?.GetValue(manager) as IDictionary;
            if (rooms != null)
            {
                foreach (DictionaryEntry entry in rooms)
                {
                    if (!(entry.Value is IVroom candidate))
                        continue;
                    VPlayer found = candidate.FindPlayerByObjectID(avatar.ActorID);
                    if (found == null)
                        continue;
                    room = candidate;
                    player = found;
                    reason = null;
                    return true;
                }
            }

            reason = "Could not find your server-side player in a room.";
            return false;
        }

        private void SpawnRandomMonster(IVroom room, VPlayer player)
        {
            if (!room.DamageAppliable())
            {
                _devStatus = "Combat is disabled in this room. Spawn test monsters inside a dungeon.";
                return;
            }

            var data = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            if (data?.ExcelDataManager?.MonsterInfoDict == null || data.ExcelDataManager.MonsterInfoDict.Count == 0)
            {
                _devStatus = "No monster data found.";
                return;
            }
            List<int> ids = new List<int>();
            foreach (KeyValuePair<int, MonsterInfo> entry in data.ExcelDataManager.MonsterInfoDict)
            {
                MonsterInfo info = entry.Value;
                // The full master list includes test actors and special entities that are not ordinary
                // combat spawns. Use only combat-capable actors for the quick test button.
                if (info != null && info.HP > 0 && info.ThreatValue > 0 && info.AbnormalMasterIDOnSpawn == 0)
                    ids.Add(entry.Key);
            }
            if (ids.Count == 0)
            {
                _devStatus = "No normal combat monsters found in game data.";
                return;
            }
            int id = ids[DevRandom.Next(ids.Count)];
            VMonster monster = room.CreateMonster(id, player.Position.CreateForwardPosWithRot(2f), player.IsIndoor, "", "", ReasonOfSpawn.Admin);
            _devStatus = monster == null ? "Monster spawn failed (ID " + id + ")." : "Spawned combat monster ID " + id + ". Allow its spawn animation to finish.";
        }

        private void SpawnRandomItem(IVroom room, VPlayer player)
        {
            List<ItemMasterInfo> candidates = GetDebugItemCandidates();
            if (candidates.Count == 0)
            {
                _devStatus = "No eligible item data found.";
                return;
            }
            ItemMasterInfo item = candidates[DevRandom.Next(candidates.Count)];
            ItemElement element = room.GetNewItemElement(item.MasterID, false, 1, 0, 0, 0);
            if (element == null || room.SpawnLootingObject(element, player.Position.CreateForwardPosWithRot(2f), player.IsIndoor, ReasonOfSpawn.Admin, 0, 0, 0L, false, false) == 0)
                _devStatus = "Item spawn failed: " + item.Name + ".";
            else
                _devStatus = "Spawned item: " + item.Name + " (" + item.MasterID + ").";
        }

        private static List<ItemMasterInfo> GetDebugItemCandidates()
        {
            var candidates = new List<ItemMasterInfo>();
            DataManager dataManager = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            if (dataManager?.ExcelDataManager == null)
                return candidates;
            foreach (ItemMasterInfo item in dataManager.ExcelDataManager.ItemInfoDict.Values)
            {
                if (item == null || item.IsPromotionItem || item.IsPromotionItemHidden)
                    continue;
                if (item.ItemType == ItemType.Consumable || item.ItemType == ItemType.Equipment || item.ItemType == ItemType.Miscellany)
                    candidates.Add(item);
            }
            return candidates;
        }

        private void KillAllMonsters(IVroom room, VPlayer player)
        {
            room.KillAllMonster();
            _devStatus = "Sent kill-all to current room.";
        }

        private void GiveShotgun(IVroom room, VPlayer player)
        {
            const int shotgunMasterId = 1000;
            DataManager dataManager = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            ItemMasterInfo info = dataManager?.ExcelDataManager?.GetItemInfo(shotgunMasterId);
            if (!(info is ItemEquipmentInfo))
            {
                _devStatus = "Base shotgun item (ID 1000) was not found.";
                return;
            }

            // Item 1000 is the standard shotgun. Its master data starts at zero gauge, so create it full.
            ItemElement item = room.GetNewItemElement(shotgunMasterId, false, 1, 0, 1, 0);
            if (item == null)
            {
                _devStatus = "Game failed to create the shotgun item.";
                return;
            }
            int slot;
            MsgErrorCode result = player.InventoryControlUnit.HandleAddItem(item, out slot, true, false);
            if (result != MsgErrorCode.Success)
            {
                _devStatus = "Could not give shotgun: " + result + ". Make room in your inventory.";
                return;
            }
            player.InventoryControlUnit.HandleChangeActiveInvenSlot(slot, true, 0);
            InfiniteShotgunChargeEnabled = true;
            _devStatus = "Shotgun added and its charge is now infinite.";
        }

        private void GiveRamblamShotgun(IVroom room, VPlayer player)
        {
            DataManager dataManager = AccessTools.Property(typeof(Hub), "dataman")?.GetValue(Hub.s) as DataManager;
            ItemMasterInfo info = dataManager?.ExcelDataManager?.GetItemInfo(RamblamShotgunItemMasterId);
            if (!(info is ItemEquipmentInfo))
            {
                _devStatus = "Ramblam shotgun item (ID 1002) was not found.";
                return;
            }

            ItemElement item = room.GetNewItemElement(RamblamShotgunItemMasterId, false, 1, 0, 5, 0);
            if (item == null)
            {
                _devStatus = "Game failed to create the Ramblam shotgun.";
                return;
            }

            int slot;
            MsgErrorCode result = player.InventoryControlUnit.HandleAddItem(item, out slot, true, false);
            if (result != MsgErrorCode.Success)
            {
                _devStatus = "Could not give Ramblam shotgun: " + result + ". Make room in your inventory.";
                return;
            }

            player.InventoryControlUnit.HandleChangeActiveInvenSlot(slot, true, 0);
            _devStatus = "Ramblam shotgun (Alexa) added with 5 shells.";
        }

        private void ToggleInfiniteHP(IVroom room, VPlayer player)
        {
            InfiniteHPEnabled = !InfiniteHPEnabled;
            if (InfiniteHPEnabled)
            {
                player.StatControlUnit.InstantChargeHP(0L, true);
                _devStatus = "Infinite HP enabled and your HP restored.";
            }
            else
            {
                _devStatus = "Infinite HP disabled.";
            }
        }

        internal static bool ShouldBlockPlayerDamage(VCreature creature)
        {
            if (!InfiniteHPEnabled || !(creature is VPlayer) || Hub.s == null)
                return false;
            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            return pdata != null && pdata.ClientMode == NetworkClientMode.Host;
        }

        internal static bool ShouldKeepShotgunCharged(EquipmentItemElement item)
        {
            return InfiniteShotgunChargeEnabled && item != null && item.ItemMasterID == 1000;
        }

        private void ToggleInfiniteMoney(IVroom room, VPlayer player)
        {
            if (!(room is MaintenanceRoom maintenanceRoom))
            {
                _devStatus = "Infinite money is available in the maintenance room only.";
                return;
            }
            InfiniteMoneyEnabled = !InfiniteMoneyEnabled;
            if (InfiniteMoneyEnabled)
            {
                const int balanceTarget = 1000000;
                if (maintenanceRoom.Currency < balanceTarget)
                    maintenanceRoom.AddCurrency(balanceTarget - maintenanceRoom.Currency);
                _devStatus = "Infinite money enabled: purchases no longer reduce the balance.";
            }
            else
            {
                _devStatus = "Infinite money disabled. Current balance is unchanged.";
            }
        }

        internal static bool ShouldBlockCurrencySpend(int amount)
        {
            if (!InfiniteMoneyEnabled || amount >= 0 || Hub.s == null)
                return false;
            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            return pdata != null && pdata.ClientMode == NetworkClientMode.Host;
        }
    }

    [HarmonyPatch(typeof(MaintenanceRoom), nameof(MaintenanceRoom.AddCurrency))]
    internal static class InfiniteMoneyPatch
    {
        private static void Prefix(ref int amount)
        {
            if (Core.ShouldBlockCurrencySpend(amount))
                amount = 0;
        }
    }

    [HarmonyPatch(typeof(StatController), nameof(StatController.ApplyDamage))]
    internal static class InfiniteHPPatch
    {
        private static bool Prefix(StatController __instance)
        {
            return !Core.ShouldBlockPlayerDamage(__instance.Self);
        }
    }

    [HarmonyPatch(typeof(EquipmentItemElement), nameof(EquipmentItemElement.SetAmount))]
    internal static class InfiniteShotgunChargePatch
    {
        private static void Prefix(EquipmentItemElement __instance, ref int amount)
        {
            if (Core.ShouldKeepShotgunCharged(__instance))
                amount = 1;
        }
    }

    [HarmonyPatch(typeof(ProtoActor), "Update")]
    internal static class ProtoActorInputPatch
    {
        private static bool _lastPressed;
        private static void Postfix()
        {
            Keyboard keyboard = Keyboard.current;
            bool pressed = keyboard != null && keyboard.f8Key.isPressed;
            if (pressed && !_lastPressed)
                Core.TogglePanelFromPatch();
            _lastPressed = pressed;
        }
    }

    [HarmonyPatch(typeof(AIController), nameof(AIController.OnSightOut))]
    internal static class MonsterLoseTargetPatch
    {
        private const float SearchDurationSeconds = 8f;
        private static readonly Dictionary<AIController, LostTarget> PendingTargets = new Dictionary<AIController, LostTarget>();
        private static readonly FieldInfo SelfField = AccessTools.Field(typeof(AIController), "_self");
        private sealed class LostTarget { internal VCreature Target; internal float LostAt; }

        private static bool Prefix(AIController __instance, VActor actor)
        {
            if (!(SelfField.GetValue(__instance) is VMonster) || !(actor is VCreature target))
                return true;
            if (!PendingTargets.TryGetValue(__instance, out LostTarget pending) || pending.Target != target)
            {
                PendingTargets[__instance] = new LostTarget { Target = target, LostAt = Time.time };
                return false;
            }
            if (Time.time - pending.LostAt < SearchDurationSeconds)
                return false;
            PendingTargets.Remove(__instance);
            return true;
        }

        internal static void OnSightIn(AIController controller) => PendingTargets.Remove(controller);
        internal static void TryExpire(AIController controller)
        {
            if (!PendingTargets.TryGetValue(controller, out LostTarget pending) || Time.time - pending.LostAt < SearchDurationSeconds)
                return;
            controller.OnSightOut(pending.Target);
        }
    }

    [HarmonyPatch(typeof(AIController), nameof(AIController.OnSightIn))]
    internal static class MonsterRegainTargetPatch
    {
        private static void Prefix(AIController __instance) => MonsterLoseTargetPatch.OnSightIn(__instance);
    }

    [HarmonyPatch(typeof(AIController), nameof(AIController.Update))]
    internal static class MonsterLoseTargetTimerPatch
    {
        private static void Postfix(AIController __instance) => MonsterLoseTargetPatch.TryExpire(__instance);
    }
}
