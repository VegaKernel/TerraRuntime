# Skyblock runtime rules

TerraRuntime does not infer gameplay from the generator that happened to create a world. Generation is a one-time operation; runtime behavior is derived from the persisted Terraria world state and the current tile population.

For Terraria 1.4.5.8 the relevant persisted property is the vanilla `SkyblockWorld` flag. TerraRuntime already decodes and preserves that flag in `WorldFileRuntimeMetadata`. The built-in `terraruntime:skyblock` creation path now publishes worlds with the same vanilla flag set, so a saved world keeps its gameplay semantics after arbitrary restarts without storing a TerraRuntime generator identity.

## `lowTiles`

`VanillaSkyblockRuntimePolicy1458` mirrors the source-backed Skyblock density gate:

- the world must have the vanilla `SkyblockWorld` flag;
- fewer than 10% of tile cells may contain an active tile;
- exactly 10% filled is not `lowTiles`.

The evaluator scans the current authoritative tile store. Consequently, the state can change as players build out the world; it is not frozen at generation time.

When `lowTiles` is active, the policy exposes the Terraria 1.4.5.8 rules required by downstream gameplay systems:

- Snow biome tile threshold: 300 instead of 1500;
- Desert biome tile threshold: 300 instead of 1500;
- Hardmode conversion is skipped.

These values are protected by the `Skyblock Runtime Policy Source Contract` workflow, which decompiles the pinned official TerrariaServer 1.4.5.8 assembly and verifies `SceneMetrics`, `WorldGen.Skyblock.lowTiles`, and `WorldGen.GERunner` before the contract is accepted.

The policy deliberately does not depend on `WorldGeneratorId`. A sparse ordinary world remains ordinary, and a loaded vanilla Skyblock world receives the same semantics even if TerraRuntime did not create it.
