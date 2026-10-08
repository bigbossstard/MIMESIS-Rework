# MIMESIS - EnemyDropLoot

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/enemydroploot](https://support.doodesch.de/enemydroploot).


> Dungeon enemies in MIMESIS now drop loot - every kill rolls against the current map's own loot pool and can drop a map-appropriate item next to the corpse, so combat actually rewards you.

![Version](https://img.shields.io/badge/version-1.0.3-blue)
![Game](https://img.shields.io/badge/game-MIMESIS-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-working-brightgreen)

## Features

- Rolls for loot whenever an enemy dies in an active dungeon room and spawns the reward at the corpse.
- Builds its loot pool from the live dungeon's own spawn data, so drops only ever contain items that could naturally appear on that level.
- Picks each drop through the source's own weighted roll, with a fallback so a kill rarely comes up empty.
- Spawns loot with the game's own helpers: scatters within a 2 m radius and snaps to the navmesh, for reliable, valid positions.
- Only activates inside real dungeon rooms; shop, tram and arena scenes are never touched.
- Configurable drop chance and number of rolls per kill via MelonPreferences.

## Requirements

| Component | Version |
|-----------|---------|
| MIMESIS | 0.3.0 (current Steam build) |
| MelonLoader | 0.7.3+ |
| MimicAPI | Required - [NeoMimicry/MimicAPI](https://github.com/NeoMimicry/MimicAPI) |

EnemyDropLoot uses MimicAPI to read private game internals, so MimicAPI must be installed for the mod to work.

## Installation

- Recommended: install via a Thunderstore mod manager (r2modman / Gale). It pulls in MelonLoader and MimicAPI for you.
- Manual:
  1. Make sure MelonLoader 0.7.3+ is installed in your game folder.
  2. Download `EnemyDropLoot.dll` from the [Releases page](https://github.com/DooDesch/Mimesis-EnemyDropLoot/releases).
  3. Copy `EnemyDropLoot.dll` **and** `MimicAPI.dll` into `MIMESIS/Mods/`.
  4. Launch the game once so the config section is generated.

## Configuration

Stored in `UserData/MelonPreferences.cfg` under the `EnemyDropLoot` category.

| Option | Description | Default | Values/Range |
|--------|-------------|---------|--------------|
| `Enabled` | Master toggle for the mod. | `true` | `true` / `false` |
| `DropChance` | Chance per roll that a slain enemy drops loot. Clamped to 0-1. | `0.1` | `0.0` - `1.0` |
| `MaxDropsPerKill` | Maximum number of loot rolls performed per enemy. Clamped to 0-100; at least 1 roll is performed. | `1` | `0` - `100` |

Raise `DropChance` toward `1.0` for more frequent drops, and raise `MaxDropsPerKill` for the chance of multiple drops per enemy.

## Usage

No keybinds and no menu - the mod is fully automatic. Enter any real dungeon mission map and the mod silently builds that room's loot pool; killed enemies may then drop map-appropriate items at their death position. The MelonLoader console confirms the pool loaded (for example, `Loaded N loot spawn bundles with M unique items`).

## AI disclosure

The mod icon is AI-generated. Code and configuration are hand-written.
