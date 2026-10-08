using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using ReluProtocol;
using ReluProtocol.Enum;
using ReluNetwork.ConstEnum;
using UnityEngine;

[assembly: MelonInfo(typeof(MimesisRework.Medical.MedicalMod), "MIMESIS Rework - Medical", "0.1.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisRework.Medical
{
    public sealed class MedicalMod : MelonMod
    {
        private const int HealItem = 3104;
        private static readonly FieldInfo SelfField = AccessTools.Field(typeof(InventoryController), "_self");

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(MedicalMod).Assembly);
            LoggerInstance.Msg("Field medical effects loaded; depot panel retired.");
        }

        internal static bool TryUseCustomMedicalItem(InventoryController inventory, out MsgErrorCode result)
        {
            result = MsgErrorCode.CantAction;
            if (Hub.s == null) return false;
            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            if (pdata == null || pdata.ClientMode != NetworkClientMode.Host) return false;
            VCreature user = SelfField?.GetValue(inventory) as VCreature;
            if (user == null || !(user.VRoom is DungeonRoom)) return false;
            int itemId = inventory.GetCurrentInventorySlotItemMasterID();
            if (itemId != HealItem) return false;
            if (user.LifeCycle != VCreatureLifeCycle.Alive) return true;

            if (itemId == HealItem)
            {
                long maxHp = user.StatControlUnit.GetSpecificStatValue(StatType.HP);
                long missing = Math.Max(0L, maxHp - user.StatControlUnit.GetCurrentHP());
                if (missing <= 0) return true;
                inventory.DecreaseItem(itemId, 1);
                user.StatControlUnit.AdjustHP(Math.Max(1L, maxHp / 4));
                result = MsgErrorCode.Success;
                return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(InventoryController), nameof(InventoryController.UseItem))]
        private static class UseMedicalItemPatch
        {
            private static bool Prefix(InventoryController __instance, ref MsgErrorCode __result)
            {
                if (!TryUseCustomMedicalItem(__instance, out __result)) return true;
                return false;
            }
        }
    }
}
