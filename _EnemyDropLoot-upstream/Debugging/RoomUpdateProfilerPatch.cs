#if DEBUG
using System.Diagnostics;
using HarmonyLib;

namespace EnemyDropLoot.Debugging
{
    // Debug-only: time the dungeon room's per-tick update (DungeonRoom.OnUpdate -> base IVroom.OnUpdate, which
    // iterates every actor in the room incl. mod-spawned loot). Feeds DebugTools so the overlay can show the
    // room tick cost climbing as loot accumulates - the core profiling signal for the area FPS-drop bug.
    [HarmonyPatch(typeof(DungeonRoom), nameof(DungeonRoom.OnUpdate))]
    internal static class RoomUpdateProfilerPatch
    {
        private static void Prefix(ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(long __state)
        {
            double ms = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            DebugTools.RecordRoomUpdate(ms);
        }
    }
}
#endif
