#if DEBUG
using System;
using EnemyDropLoot.Config;
using EnemyDropLoot.Managers;
using MelonLoader;
using Mimic.Actors;
using MimicAPI.GameAPI;
using ReluProtocol;
using ReluProtocol.Enum;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EnemyDropLoot.Debugging
{
    // Debug-only profiling + stress-test harness for the area FPS-drop investigation.
    // Compiled ONLY into Debug builds (#if DEBUG); Release ships none of this.
    //
    // Live overlay (top-left) shows, every frame:
    //   - FPS
    //   - DungeonRoom.OnUpdate time in ms (last / max / rolling avg) - the actual per-tick room cost,
    //     measured by RoomUpdateProfilerPatch. Watch this climb as mod-spawned loot accumulates.
    //   - how many loot objects THIS MOD has spawned in the current room
    //   - the active DropChance / MaxDropsPerKill
    //
    // Hotkeys: F8 toggle overlay, F9 log a snapshot, F10 stress-spawn +25 loot at the player,
    //          F11 reset the profiling counters (leave/re-enter the room to actually despawn loot).
    internal static class DebugTools
    {
        private static bool _overlay = true;
        private static GUIStyle? _style;

        private static float _fps;
        private static float _fpsTimer;
        private static int _fpsFrames;

        // Fed by RoomUpdateProfilerPatch (stopwatch around DungeonRoom.OnUpdate).
        internal static double LastRoomUpdateMs;
        internal static double MaxRoomUpdateMs;
        private static double _emaRoomUpdateMs;

        // Loot objects this mod has spawned in the current room (real drops + stress spawns).
        internal static int ModSpawnedThisRoom;

        internal static void RecordRoomUpdate(double ms)
        {
            LastRoomUpdateMs = ms;
            if (ms > MaxRoomUpdateMs) MaxRoomUpdateMs = ms;
            _emaRoomUpdateMs = _emaRoomUpdateMs <= 0.0 ? ms : (_emaRoomUpdateMs * 0.95 + ms * 0.05);
        }

        internal static void OnModLootSpawned() => ModSpawnedThisRoom++;

        internal static void OnRoomReset()
        {
            ModSpawnedThisRoom = 0;
            MaxRoomUpdateMs = 0.0;
            _emaRoomUpdateMs = 0.0;
        }

        internal static void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _fpsTimer += dt;
            _fpsFrames++;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / _fpsTimer;
                _fpsTimer = 0f;
                _fpsFrames = 0;
            }

            Keyboard? kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.F8].wasPressedThisFrame) _overlay = !_overlay;
            if (kb[Key.F9].wasPressedThisFrame) LogSnapshot();
            if (kb[Key.F10].wasPressedThisFrame) StressSpawn(25);
            if (kb[Key.F11].wasPressedThisFrame)
            {
                OnRoomReset();
                MelonLogger.Msg("[EDL/Profiler] Stats reset (leave/re-enter the room to actually despawn the loot).");
            }
        }

        internal static void DrawOverlay()
        {
            if (!_overlay) return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                richText = false
            };

            string text =
                "EnemyDropLoot [DEBUG profiler]\n" +
                $"FPS: {_fps:F0}\n" +
                $"DungeonRoom.OnUpdate: {LastRoomUpdateMs:F2} ms  (max {MaxRoomUpdateMs:F2}, avg {_emaRoomUpdateMs:F2})\n" +
                $"Mod loot spawned this room: {ModSpawnedThisRoom}\n" +
                $"DropChance {EnemyDropLootPreferences.DropChance:P0}  |  MaxDropsPerKill {EnemyDropLootPreferences.MaxDropsPerKill}\n" +
                "F8 hide   F9 log   F10 spawn +25 loot   F11 reset stats";

            Rect rect = new Rect(14f, 14f, 640f, 150f);

            // drop shadow
            _style.normal.textColor = Color.black;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), text, _style);
            // foreground
            _style.normal.textColor = new Color(1f, 0.95f, 0.35f);
            GUI.Label(rect, text, _style);
        }

        private static void LogSnapshot()
        {
            MelonLogger.Msg(
                $"[EDL/Profiler] FPS={_fps:F0} | DungeonRoom.OnUpdate last={LastRoomUpdateMs:F2}ms max={MaxRoomUpdateMs:F2}ms avg={_emaRoomUpdateMs:F2}ms | ModLootThisRoom={ModSpawnedThisRoom} | DropChance={EnemyDropLootPreferences.DropChance:P0} MaxDropsPerKill={EnemyDropLootPreferences.MaxDropsPerKill}");
        }

        // Rapidly reproduce loot accumulation without grinding kills: spawn a burst of loot at the player.
        private static void StressSpawn(int count)
        {
            try
            {
                IVroom? room = LootPoolManager.ActiveRoomDebug;
                if (room == null)
                {
                    MelonLogger.Warning("[EDL/Profiler] StressSpawn: no active dungeon room (enter a dungeon first).");
                    return;
                }

                ProtoActor? player = PlayerAPI.GetLocalPlayer();
                if (player == null)
                {
                    MelonLogger.Warning("[EDL/Profiler] StressSpawn: local player not found.");
                    return;
                }

                Vector3 basePos = player.transform.position;
                int spawned = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!LootPoolManager.DebugTryPickItem(out int itemId)) continue;

                    ItemElement? item = room.GetNewItemElement(itemId, false);
                    if (item == null) continue;

                    Vector2 offset = UnityEngine.Random.insideUnitCircle * 3f;
                    Vector3 spawnVec = new Vector3(basePos.x + offset.x, basePos.y, basePos.z + offset.y);
                    PosWithRot pos = new PosWithRot { pos = spawnVec };

                    if (room.SpawnLootingObject(item, pos, false, ReasonOfSpawn.ActorDying) != 0)
                    {
                        spawned++;
                        OnModLootSpawned();
                    }
                }

                MelonLogger.Msg($"[EDL/Profiler] StressSpawn: requested {count}, spawned {spawned}. Mod loot this room = {ModSpawnedThisRoom}.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EDL/Profiler] StressSpawn error: {ex.Message}");
            }
        }
    }
}
#endif
