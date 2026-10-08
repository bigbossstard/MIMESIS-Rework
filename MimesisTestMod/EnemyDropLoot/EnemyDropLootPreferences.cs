using System;
using MelonLoader;
using UnityEngine;

namespace MimesisTestMod.EnemyDropLoot
{
    internal static class EnemyDropLootPreferences
    {
        private static MelonPreferences_Category _category;
        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<float> _dropChance;
        private static MelonPreferences_Entry<int> _maxDropsPerKill;

        internal static void Initialize()
        {
            if (_category != null)
                return;
            _category = MelonPreferences.CreateCategory("MimesisRework.EnemyDropLoot", "MIMESIS Rework - Enemy Drops");
            _enabled = _category.CreateEntry("Enabled", true, "Enabled", "Toggle level-appropriate enemy drops.");
            _dropChance = _category.CreateEntry("DropChance", 1f, "Drop chance multiplier", "Multiplier for threat-based drops (0-1). Default: 1.0");
            _maxDropsPerKill = _category.CreateEntry("MaxDropsPerKill", 1, "Max drops per kill", "Maximum loot rolls per enemy.");
        }

        internal static bool Enabled => _enabled.Value;
        internal static float DropChance => Mathf.Clamp01(_dropChance.Value);
        internal static int MaxDropsPerKill => Mathf.Clamp(_maxDropsPerKill.Value, 0, 100);

        internal static int RollDropCount(float chancePerRoll)
        {
            if (!Enabled)
                return 0;
            int rolls = MaxDropsPerKill;
            int successes = 0;
            for (int i = 0; i < rolls; i++)
                if (UnityEngine.Random.value < Mathf.Clamp01(chancePerRoll))
                    successes++;
            return successes;
        }
    }
}
