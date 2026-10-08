using MelonLoader;
using MimicAPI.GameAPI;
using ReluProtocol;
using ReluProtocol.Enum;
using UnityEngine;

namespace EnemyDropLoot.Services
{
	internal static class LootSpawnService
	{
		internal static bool TrySpawnLoot(VMonster monster, int itemMasterId)
		{
			ItemElement? itemElement = monster.VRoom.GetNewItemElement(itemMasterId, isFake: false);
			if (itemElement == null)
			{
				return false;
			}

			// Place the drop exactly like the game's own monster loot (VMonster.OnDying): snap the monster's
			// position to the nearest navmesh poly with the DEFAULT search radius, and NO horizontal scatter.
			// This keeps the drop on valid navmesh near the monster, inside the room's loot-collecting volume, so
			// it participates in the same drain/cleanup as vanilla loot (the old 2m scatter + tight 2f snap could
			// land loot outside that volume, where the game never cleans it up).
			Vector3 spawnPos = monster.PositionVector;
			Vector3 nearestPos = spawnPos;
			VWorld? vworld = CoreAPI.GetVWorld();
			if (vworld != null)
			{
				Vector3 navPos = vworld.FindNearestPoly(spawnPos);
				if (navPos != NavMeshConstants.INVALID_POSITION)
				{
					nearestPos = navPos;
				}
			}

			PosWithRot dropPos = monster.Position.Clone();
			dropPos.x = nearestPos.x;
			dropPos.y = nearestPos.y;
			dropPos.z = nearestPos.z;

			int spawnResult = monster.VRoom.SpawnLootingObject(itemElement, dropPos, monster.IsIndoor, ReasonOfSpawn.ActorDying);
			if (spawnResult == 0)
			{
				MelonLogger.Warning($"EnemyDropLoot: Failed to spawn loot item {itemMasterId} for monster {monster.MasterID}.");
				return false;
			}

#if DEBUG
			Debugging.DebugTools.OnModLootSpawned();
#endif
			return true;
		}
	}
}

