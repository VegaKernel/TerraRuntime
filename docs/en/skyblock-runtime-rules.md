# Skyblock runtime rules

TerraRuntime does not infer gameplay from the generator that happened to create a world. Generation is a one-time operation; runtime behavior is derived from the persisted Terraria world state and the current tile population.

For Terraria 1.4.5.8 the relevant persisted property is the vanilla `SkyblockWorld` flag. TerraRuntime already decodes and preserves that flag in `WorldFileRuntimeMetadata`. The built-in `terraruntime:skyblock` creation path now publishes worlds with the same vanilla flag set, so a saved world keeps its gameplay semantics after arbitrary restarts without storing a TerraRuntime generator identity.

## `lowTiles`

`VanillaSkyblockRuntimePolicy1458` mirrors the source-backed Skyblock density gate:

- the world must have the vanilla `SkyblockWorld` flag;
- fewer than 10% of tile cells may contain an active tile;
- exactly 10% filled is not `lowTiles`.

The standalone evaluator describes a supplied tile image. The live `WorldTileAuthority` owns the mutable density census used by NPC death and the published world-info snapshot. As in `WorldGen.Skyblock.ScanTiles`, loading counts active tiles inside the forty-cell border. The periodic `CountTiles` path scans one complete X column every $30\,\text{updates}$, using the two source surface/depth Y intervals; it refreshes the flag only after the final column. Actuated active tiles count. Changes during a scan retain the already counted columns until the next cycle. This is a sampled census, not an instantaneous density query.

The quotient uses source `float` rounding and comparison with `0.1f`. A changed live flag requests the existing `packet 7` path and refreshes the immutable join snapshot. The owner requires persisted Skyblock identity and retained world surface; unknown facts are not inferred from a generator name or stale initial low-tile flag. Other resource-absence flags and gameplay consumers retaining bootstrap facts are outside this live census qualification.

When `lowTiles` is active, the policy exposes the Terraria 1.4.5.8 rules required by downstream gameplay systems:

- Snow biome tile threshold: 300 instead of 1500;
- Desert biome tile threshold: 300 instead of 1500;
- Hardmode conversion is skipped.

These values are protected by the `Skyblock Runtime Policy Source Contract` workflow, which decompiles the pinned official TerrariaServer 1.4.5.8 assembly and verifies `SceneMetrics`, `WorldGen.Skyblock.lowTiles`, and `WorldGen.GERunner` before the contract is accepted.

The policy deliberately does not depend on `WorldGeneratorId`. A sparse ordinary world remains ordinary, and a loaded vanilla Skyblock world receives the same semantics even if TerraRuntime did not create it.

[Managed checkpoint, 2026-11-16](../verification/net11-stable-item-flow-darts-managed-20261116.json).
