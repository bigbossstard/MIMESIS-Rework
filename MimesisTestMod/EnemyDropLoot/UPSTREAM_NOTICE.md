Enemy-drop behavior is adapted from EnemyDropLoot by DooDesch, licensed under the MIT License.
Upstream: https://github.com/DooDesch-Mods/Mimesis-EnemyDropLoot
Upstream revision inspected: 1279bfc9f3f8077eb53ba015be8f19611e40845f

The loot-pool selection, room lifecycle, configurable rolls, death hook, and navmesh placement
follow the upstream implementation. MimicAPI calls were replaced with Harmony reflection so this
project does not need a separate MimicAPI dependency. Default DropChance is set to 1.0 per the
project's requested guaranteed-drop behavior.
