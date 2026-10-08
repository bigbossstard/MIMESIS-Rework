using System.Reflection;
using HarmonyLib;
using Bifrost.Cooked;
using ReluProtocol;
using ReluProtocol.Enum;
using UnityEngine;

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

    [HarmonyPatch(typeof(ExcelDataManager), nameof(ExcelDataManager.GetItemInfo), new[] { typeof(int) })]
    internal static class RamblamShotgunItemPatch
    {
        private static void Postfix(ExcelDataManager __instance, ItemMasterInfo __result) =>
            EnemyDropLootManager.ConfigureRamblamShotgun(__result, __instance);
    }

    [HarmonyPatch(typeof(CooltimeController), nameof(CooltimeController.AddCooltime), new[] { typeof(CooltimeType), typeof(int), typeof(long), typeof(long), typeof(bool) })]
    internal static class RamblamShotgunFireRatePatch
    {
        private const int RamblamShotgunItemMasterId = 1002;
        private const int ShotgunFireSkillMasterId = 10900;
        private static readonly FieldInfo SelfField = AccessTools.Field(typeof(CooltimeController), "_self");
        private static bool _loggedAdjustedCooldown;

        private static void Prefix(CooltimeController __instance, CooltimeType type, int masterID, ref long idCooltime)
        {
            if (type != CooltimeType.Skill || masterID != ShotgunFireSkillMasterId || idCooltime <= 0)
                return;

            VActor actor = SelfField.GetValue(__instance) as VActor;
            if (actor?.InventoryControlUnit?.GetCurrentInventorySlotItemMasterID() == RamblamShotgunItemMasterId)
            {
                long vanillaCooltime = idCooltime;
                idCooltime /= 4L;
                if (!_loggedAdjustedCooldown)
                {
                    _loggedAdjustedCooldown = true;
                    MelonLoader.MelonLogger.Msg(
                        "MIMESIS Rework: Alexa fire cooldown applied for actor " + actor.ObjectID +
                        ": " + vanillaCooltime + " -> " + idCooltime + " ms.");
                }
            }
        }
    }

    [HarmonyPatch(typeof(LootingLevelObject), nameof(LootingLevelObject.OnSpawn), new[] { typeof(LootingObjectInfo), typeof(bool) })]
    internal static class AlexaShotgunFlickerPatch
    {
        private const int RamblamShotgunItemMasterId = 1002;

        private static void Postfix(LootingLevelObject __instance)
        {
            if (__instance.itemMasterID == RamblamShotgunItemMasterId &&
                __instance.GetComponent<AlexaShotgunFlicker>() == null)
                __instance.gameObject.AddComponent<AlexaShotgunFlicker>();
        }
    }

    internal sealed class AlexaShotgunFlicker : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color RedTint = new Color(0.5f, 0.015f, 0.025f, 1f);
        private static readonly Color RedEmission = new Color(0.28f, 0.004f, 0.008f, 1f);
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private Color[] _legacyColors;
        private bool[] _usesBaseColor;
        private bool[] _usesLegacyColor;
        private bool[] _usesEmission;
        private MaterialPropertyBlock _propertyBlock;
        private float _phase;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            _legacyColors = new Color[_renderers.Length];
            _usesBaseColor = new bool[_renderers.Length];
            _usesLegacyColor = new bool[_renderers.Length];
            _usesEmission = new bool[_renderers.Length];
            _propertyBlock = new MaterialPropertyBlock();
            _phase = Random.value * 10f;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i] != null ? _renderers[i].sharedMaterial : null;
                if (material == null)
                    continue;

                _usesBaseColor[i] = material.HasProperty(BaseColorId);
                _usesLegacyColor[i] = material.HasProperty(ColorId);
                _usesEmission[i] = material.HasProperty(EmissionColorId);
                if (_usesBaseColor[i])
                    _baseColors[i] = material.GetColor(BaseColorId);
                if (_usesLegacyColor[i])
                    _legacyColors[i] = material.GetColor(ColorId);
            }
        }

        private void Update()
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((Time.time + _phase) * 4.2f);
            float tintAmount = Mathf.Lerp(0.1f, 0.3f, pulse);
            Color emission = RedEmission * Mathf.Lerp(0.25f, 1.1f, pulse);

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock);
                if (_usesBaseColor[i])
                    _propertyBlock.SetColor(BaseColorId, Color.Lerp(_baseColors[i], RedTint, tintAmount));
                if (_usesLegacyColor[i])
                    _propertyBlock.SetColor(ColorId, Color.Lerp(_legacyColors[i], RedTint, tintAmount));
                if (_usesEmission[i])
                    _propertyBlock.SetColor(EmissionColorId, emission);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }

    [HarmonyPatch(typeof(Hub), "GetL10NText", new[] { typeof(string), typeof(object[]) })]
    internal static class AlexaShotgunNamePatch
    {
        private static bool Prefix(string key, ref string __result)
        {
            if (key != "Alexa")
                return true;

            __result = "Alexa";
            return false;
        }
    }

    [HarmonyPatch(typeof(VPlayer), nameof(VPlayer.HandleGrapLootingObject), new[] { typeof(int), typeof(int) })]
    internal static class RamblamShotgunPickupDiagnosticPatch
    {
        private const int RamblamShotgunItemMasterId = 1002;

        private static void Prefix(VPlayer __instance, int lootingObjectID, out int __state)
        {
            __state = 0;
            VActor actor = __instance.VRoom?.FindActorByObjectID(lootingObjectID);
            if (!(actor is VLootingObject lootingObject) ||
                lootingObject.GetItemElement()?.ItemMasterID != RamblamShotgunItemMasterId)
                return;

            __state = RamblamShotgunItemMasterId;
            MelonLoader.MelonLogger.Msg(
                "MIMESIS Rework: Alexa shotgun pickup attempted; fake=" + lootingObject.IsFake() + ".");
        }

        private static void Postfix(int __state, MsgErrorCode __result)
        {
            if (__state == RamblamShotgunItemMasterId)
                MelonLoader.MelonLogger.Msg("MIMESIS Rework: Alexa shotgun pickup result=" + __result + ".");
        }
    }
}
