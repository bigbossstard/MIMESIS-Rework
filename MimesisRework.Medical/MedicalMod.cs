using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using ReluProtocol;
using ReluProtocol.Enum;
using ReluNetwork.ConstEnum;
using Mimic.Actors;
using UnityEngine;

[assembly: MelonInfo(typeof(MimesisRework.Medical.MedicalMod), "MIMESIS Rework - Medical", "0.1.0", "MIMESIS Rework")]
[assembly: MelonGame(null, null)]

namespace MimesisRework.Medical
{
    public sealed class MedicalMod : MelonMod
    {
        private const int HealItem = 3104;
        private const int FirstHpJuiceItem = 3102;
        private const int LastHpJuiceItem = 3107;
        private const float ReviveTargetRange = 4.5f;
        private const float DuplicateUseWindowSeconds = 2f;
        private static readonly FieldInfo SelfField = AccessTools.Field(typeof(InventoryController), "_self");
        private static readonly MethodInfo PlayerRevivePacketHandler =
            AccessTools.Method(typeof(GameMainBase), "OnPacket", new[] { typeof(PlayerReviveSig) });
        private static readonly Dictionary<long, float> ProcessedUseRequests = new Dictionary<long, float>();

        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(MedicalMod).Assembly);
            LoggerInstance.Msg("Field medical effects loaded; depot panel retired.");
        }

        internal static bool TryUseCustomMedicalItem(
            InventoryController inventory, int hashCode, bool sync, out MsgErrorCode result)
        {
            result = MsgErrorCode.CantAction;
            if (Hub.s == null) return false;
            if (inventory == null) return false;
            var pdata = AccessTools.Field(typeof(Hub), "pdata")?.GetValue(Hub.s) as Hub.PersistentData;
            int itemId = inventory.GetCurrentInventorySlotItemMasterID();
            if (!IsHpJuice(itemId)) return false;
            MelonLogger.Msg("Medical item use requested: item=" + itemId + ", hash=" + hashCode + ", sync=" + sync + ".");

            if (pdata == null || pdata.ClientMode != NetworkClientMode.Host)
            {
                MelonLogger.Warning("Medical item request not handled locally: host authority is required.");
                return false;
            }

            VCreature user = SelfField?.GetValue(inventory) as VCreature;
            if (user == null || !(user.VRoom is DungeonRoom))
            {
                MelonLogger.Warning("Medical item request ignored: player or supported dungeon room is unavailable.");
                return false;
            }
            if (user.LifeCycle != VCreatureLifeCycle.Alive)
            {
                MelonLogger.Warning("Medical item request rejected: user actor " + user.ObjectID + " is " + user.LifeCycle + ".");
                return itemId == HealItem;
            }

            long requestKey = ((long)user.ObjectID << 32) | (uint)hashCode;
            float now = Time.realtimeSinceStartup;
            var expiredRequests = new List<long>();
            foreach (KeyValuePair<long, float> entry in ProcessedUseRequests)
                if (now - entry.Value > DuplicateUseWindowSeconds)
                    expiredRequests.Add(entry.Key);
            foreach (long expiredRequest in expiredRequests)
                ProcessedUseRequests.Remove(expiredRequest);
            if (ProcessedUseRequests.ContainsKey(requestKey))
            {
                result = MsgErrorCode.Success;
                MelonLogger.Msg("Ignored duplicate medical-use request: player=" + user.ObjectID +
                    ", item=" + itemId + ", hash=" + hashCode + ".");
                return true;
            }
            ProcessedUseRequests.Add(requestKey, now);

            if (TryReviveAimedTeammate(inventory, user, pdata.main, itemId, out result))
                return true;
            if (itemId != HealItem)
                return false;

            long maxHp = user.StatControlUnit.GetSpecificStatValue(StatType.HP);
            long missing = Math.Max(0L, maxHp - user.StatControlUnit.GetCurrentHP());
            if (missing <= 0)
            {
                MelonLogger.Msg("Medical item not consumed: player " + user.ObjectID + " has full HP.");
                return true;
            }
            long healAmount = Math.Max(1L, maxHp / 4);
            inventory.DecreaseItem(itemId, 1);
            user.StatControlUnit.AdjustHP(healAmount);
            result = MsgErrorCode.Success;
            MelonLogger.Msg("Medical item healed player " + user.ObjectID + ": requested=" + healAmount +
                ", missingBefore=" + missing + ", maxHP=" + maxHp + ", hash=" + hashCode + ".");
            return true;
        }

        private static bool IsHpJuice(int itemId)
        {
            return itemId >= FirstHpJuiceItem && itemId <= LastHpJuiceItem;
        }

        private static bool TryReviveAimedTeammate(
            InventoryController inventory, VCreature user, GameMainBase main, int itemId, out MsgErrorCode result)
        {
            result = MsgErrorCode.CantAction;
            if (main == null)
            {
                MelonLogger.Warning("Medical revive target check skipped: active game scene is unavailable.");
                return false;
            }

            Camera aimCamera = Camera.main;
            if (aimCamera == null)
            {
                MelonLogger.Warning("Medical revive target check skipped: main camera is unavailable; falling back to self-heal.");
                return false;
            }

            Ray aimRay = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int layerMask = Hub.RaySensorLayerMask;
            RaycastHit[] hits = Physics.RaycastAll(
                aimRay, ReviveTargetRange, layerMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            if (hits.Length == 0)
                MelonLogger.Msg("Medical revive target check: no hit on the game's ray-sensor layers within " +
                    ReviveTargetRange + "m (mask=" + layerMask + "); checking player model bounds independently.");

            VPlayer target = null;
            RaycastHit targetHit = default(RaycastHit);
            string firstHit = hits.Length == 0 ? "none" : hits[0].collider.name +
                " (layer=" + hits[0].collider.gameObject.layer + ", distance=" + hits[0].distance.ToString("F2") + "m)";
            var hitSummary = new System.Text.StringBuilder();
            foreach (RaycastHit hit in hits)
            {
                if (hitSummary.Length > 0) hitSummary.Append("; ");
                VPlayer hitPlayer = hit.collider.GetComponentInParent<VPlayer>();
                ProtoActor visualActor = null;
                if (hitPlayer == null)
                {
                    visualActor = hit.collider.GetComponentInParent<ProtoActor>();
                    if (visualActor != null)
                        hitPlayer = user.VRoom.FindPlayerByObjectID(visualActor.ActorID);
                }
                hitSummary.Append(hit.collider.name).Append("@").Append(hit.distance.ToString("F2"))
                    .Append("m/layer").Append(hit.collider.gameObject.layer)
                    .Append("/VPlayer=").Append(hitPlayer != null ? hitPlayer.ObjectID.ToString() : "none")
                    .Append("/ProtoActor=").Append(visualActor != null ? visualActor.ActorID.ToString() : "none");
                if (hitPlayer != null && hitPlayer != user && hitPlayer.VRoom == user.VRoom)
                {
                    target = hitPlayer;
                    targetHit = hit;
                    break;
                }
            }
            float aimedDistance = 0f;
            if (target == null || target.LifeCycle != VCreatureLifeCycle.Dead)
            {
                VPlayer aimedDeadPlayer = FindDeadPlayerAlongAim(main, user, aimRay, out aimedDistance);
                if (aimedDeadPlayer != null)
                {
                    target = aimedDeadPlayer;
                    targetHit = default(RaycastHit);
                }
            }
            if (target == null)
            {
                MelonLogger.Msg("Medical revive target check: " + hits.Length + " ray-sensor hit(s), first=" +
                    firstHit + "; hits=[" + hitSummary + "]; no allied player actor resolved under crosshair.");
                return false;
            }
            if (target.LifeCycle != VCreatureLifeCycle.Dead)
            {
                MelonLogger.Msg("Medical revive target check: aimed player " + target.ObjectID + " is " + target.LifeCycle +
                    ", not Dead; collider=" + targetHit.collider.name + ", distance=" +
                    targetHit.distance.ToString("F2") + "m; falling back to self-heal.");
                return false;
            }

            MelonLogger.Msg("Medical revive started: user=" + user.ObjectID + ", target=" + target.ObjectID +
                ", distance=" + (targetHit.collider != null ? targetHit.distance : aimedDistance).ToString("F2") +
                "m, collider=" + (targetHit.collider != null ? targetHit.collider.name : "aim-cone actor fallback") + ".");
            if (!target.Revive(target.Position))
            {
                MelonLogger.Warning("Medical revive failed: native VPlayer.Revive returned false for actor " + target.ObjectID + ".");
                return true;
            }

            var reviveSignal = new PlayerReviveSig { revivedPlayerActorID = target.ObjectID };
            try
            {
                user.VRoom.SendToAllPlayers(reviveSignal, null);
                if (PlayerRevivePacketHandler == null)
                    MelonLogger.Error("Medical revive local notification failed: GameMainBase.OnPacket(PlayerReviveSig) was not found.");
                else
                    PlayerRevivePacketHandler.Invoke(main, new object[] { reviveSignal });
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Medical revive succeeded locally but network/player notification failed for actor " +
                    target.ObjectID + ": " + ex);
            }

            inventory.DecreaseItem(itemId, 1);
            result = MsgErrorCode.Success;
            MelonLogger.Msg("Medical revive completed: user=" + user.ObjectID + ", target=" + target.ObjectID +
                ", item=" + itemId + ", actorLifecycle=" + target.LifeCycle + ", itemConsumed=1.");
            return true;
        }

        private static VPlayer FindDeadPlayerAlongAim(
            GameMainBase main, VCreature user, Ray aimRay, out float targetDistance)
        {
            targetDistance = 0f;
            VPlayer bestTarget = null;
            float bestDistance = float.MaxValue;
            string candidates = string.Empty;
            foreach (ProtoActor visualActor in main.GetAllPlayers())
            {
                if (visualActor == null) continue;
                VPlayer candidate = user.VRoom.FindPlayerByObjectID(visualActor.ActorID);
                if (candidate == null || candidate == user || candidate.VRoom != user.VRoom ||
                    candidate.LifeCycle != VCreatureLifeCycle.Dead)
                    continue;

                Renderer[] renderers = visualActor.GetComponentsInChildren<Renderer>(true);
                float actorDistance = float.MaxValue;
                bool hasVisibleBounds = false;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                        continue;
                    hasVisibleBounds = true;
                    float boundsDistance;
                    if (renderer.bounds.IntersectRay(aimRay, out boundsDistance) &&
                        boundsDistance >= 0f && boundsDistance <= ReviveTargetRange &&
                        boundsDistance < actorDistance)
                        actorDistance = boundsDistance;
                }

                if (!hasVisibleBounds)
                {
                    Vector3 offset = visualActor.transform.position - aimRay.origin;
                    float distance = Vector3.Dot(offset, aimRay.direction);
                    if (distance > 0f && distance <= ReviveTargetRange &&
                        (offset - aimRay.direction * distance).magnitude <= 0.35f)
                        actorDistance = distance;
                }

                if (candidates.Length > 0) candidates += "; ";
                candidates += "actor=" + visualActor.ActorID + ", lifecycle=" + candidate.LifeCycle +
                    ", root=" + visualActor.transform.position.ToString("F2") +
                    ", renderers=" + renderers.Length +
                    ", rayBoundsDistance=" + (actorDistance == float.MaxValue ? "miss" : actorDistance.ToString("F2") + "m");
                if (actorDistance == float.MaxValue || actorDistance >= bestDistance)
                    continue;
                bestTarget = candidate;
                bestDistance = actorDistance;
            }

            if (bestTarget != null)
            {
                targetDistance = bestDistance;
                MelonLogger.Msg("Medical revive target resolved from player actors: target=" + bestTarget.ObjectID +
                    ", distance=" + bestDistance.ToString("F2") + "m; static geometry does not block body selection. " +
                    "Dead-player candidates: " + candidates + ".");
            }
            else
                MelonLogger.Msg("Medical revive body-bound check found no dead player intersecting the crosshair within " +
                    ReviveTargetRange + "m. Dead-player candidates: " + candidates + ".");
            return bestTarget;
        }

        [HarmonyPatch(typeof(InventoryController), nameof(InventoryController.UseItem))]
        private static class UseMedicalItemPatch
        {
            private static bool Prefix(
                InventoryController __instance, int hashCode, bool sync, ref MsgErrorCode __result)
            {
                int itemId = __instance == null ? -1 : __instance.GetCurrentInventorySlotItemMasterID();
                MelonLogger.Msg("Medical UseItem prefix observed: item=" + itemId + ", hash=" + hashCode +
                    ", sync=" + sync + ".");
                if (!TryUseCustomMedicalItem(__instance, hashCode, sync, out __result)) return true;
                return false;
            }
        }
    }
}
