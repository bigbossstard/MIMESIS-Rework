using System;
using MelonLoader;
using UnityEngine;

namespace EnemyDropLoot.Config
{
	internal static class EnemyDropLootPreferences
	{
		private const string CategoryId = "EnemyDropLoot";

		private static MelonPreferences_Category _category = null!;
		private static MelonPreferences_Entry<bool> _enabled = null!;
		private static MelonPreferences_Entry<float> _dropChance = null!;
		private static MelonPreferences_Entry<int> _maxDropsPerKill = null!;

		internal static void Initialize()
		{
			if (_category != null)
			{
				return;
			}

			_category = MelonPreferences.CreateCategory(CategoryId, "Enemy Drop Loot");
			_enabled = CreateEntry("Enabled", true, "Enabled", "Toggle the Enemy Drop Loot mod. Default: true");
			_dropChance = CreateEntry("DropChance", 0.1f, "Drop chance", "Chance (0-1) per roll that a slain enemy drops loot. Default: 0.1");
			_maxDropsPerKill = CreateEntry("MaxDropsPerKill", 1, "Max drops per kill", "Maximum number of loot rolls performed per enemy. Default: 1");
		}

		private static MelonPreferences_Entry<T> CreateEntry<T>(string identifier, T defaultValue, string displayName, string? description = null)
		{
			if (_category == null)
			{
				throw new InvalidOperationException("Preference category not initialized.");
			}

			return _category.CreateEntry(identifier, defaultValue, displayName, description);
		}

		internal static bool Enabled => _enabled.Value;
		internal static float DropChance => Mathf.Clamp01(_dropChance.Value);
		internal static int MaxDropsPerKill => Mathf.Clamp(_maxDropsPerKill.Value, 0, 100);
		
		internal static int RollDropCount()
		{
			if (!Enabled)
			{
				return 0;
			}

			int rolls = Math.Max(1, MaxDropsPerKill);
			int successes = 0;
			for (int i = 0; i < rolls; i++)
			{
				if (UnityEngine.Random.value <= DropChance)
				{
					successes++;
				}
			}

			return successes;
		}
	}
}

