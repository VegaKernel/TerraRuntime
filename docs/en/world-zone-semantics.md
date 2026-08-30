# World zone semantics

[Русский](../ru/world-zone-semantics.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime exposes depth and biome membership through typed gameplay semantics instead of unrelated numeric checks or persistence/network flags.

## Depth classification

`VanillaWorldDepthZoneResolver` reproduces the mutually exclusive vertical classification used by TerrariaServer 1.4.5.8 `SceneMetrics`: sky ends at `worldSurface * 0.35f`, overworld ends at `worldSurface`, dirt layer ends at `rockLayer`, rock layer ends at `maxTilesY - 200`, and lower tiles are underworld. Boundary inclusivity is covered by executable tests. Invalid coordinates and inconsistent layer geometry fail closed.

## Biome membership

`VanillaWorldBiomeFlags` names independent gameplay memberships such as Corruption, Crimson, Hallow, Jungle, Snow, Desert, Dungeon and Shimmer. `VanillaWorldZoneState` validates known bits and keeps these memberships distinct from the single depth zone. Combined memberships are intentional: a location can satisfy more than one scene condition.

The flags are the semantic output boundary for a future source-backed `SceneMetrics` tile-count scan. They are not packet or world-file bits.

## Capability boundary

This slice completes typed biome/zone semantics; it does not claim tile-count threshold scanning or full biome-detection parity. Tile census radius, thresholds, nearby structures, special-seed exceptions and player-scene integration remain separate source-backed work.
