# Changelog

All notable changes to EnemyDropLoot are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [1.0.8] - 2026-09-04

### Fixed
- The mod reports its version. In 1.0.7 the release build got no version, thus MelonLoader and every mod manager showed `0.0.0-dev`.

## [1.0.7] - 2026-09-04

### Changed
- The mod is built against MIMESIS 0.3.1 and tested in a running game on that build. Loot behaviour does not change.

## [1.0.6] - 2026-06-22

### Fixed
- Slain enemies dropped roughly twice as much loot as configured. The mod's Harmony patches were applied twice (MelonLoader auto-applies them, and the mod also called PatchAll() itself), so the death handler ran twice per kill. Patches now apply exactly once, so a kill drops the configured amount.
- Loot is now dropped on the same event the game uses for its own monster drops, so it spawns exactly once per death and no longer double-drops on revives or force-kills.
- Dropped loot is now placed like the game's own loot (on the nearest navmesh point, no scatter), so it stays within the area the game cleans up on room transitions instead of lingering. This reduces the loot-object build-up that could lower frame rates in loot-heavy areas. (Addresses the "FPS drop" report.)



### Changed
- Started maintaining a full changelog that is now published on GitHub, Thunderstore
  and Nexus. No gameplay changes compared to 1.0.4.

## [1.0.4] - 2026-06-16

### Changed
- Updated the MimicAPI dependency to 0.3.0 for full compatibility with the Mimesis
  0.3.0 game build.
- Rewrote the README to the current standard with accurate configuration, dependency
  and badge information.

## [1.0.3] - 2026-06-15

### Fixed
- Compatibility with the Mimesis 0.3.0 game update and MelonLoader 0.7.3. Enemies drop
  loot again on the new game build.

### Changed
- Reorganised the README with a table of contents, clearer feature descriptions and
  step-by-step installation and configuration guidance, including `DropChance` and
  `MaxDropsPerKill` examples.

## [1.0.2] - 2025-11-16

### Added
- Declared MimicAPI as a dependency so the mod resolves its runtime requirement
  automatically.

## [1.0.1] - 2025-11-16

### Changed
- Refreshed the README with a clearer mod description, installation steps and
  configuration options.

## [1.0.0] - 2025-11-16

### Added
- Initial release. Dungeon enemies now drop loot that matches the active map's spawn
  pool when killed.
- `DropChance` and `MaxDropsPerKill` preferences to tune how often and how much loot
  drops.
