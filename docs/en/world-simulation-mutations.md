# Wiring, liquid and growth mutation boundaries

[Русский](../ru/world-simulation-mutations.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime keeps wiring, liquid material, liquid scheduling and growth commits separate from packet codecs and from ordinary tile placement. The initial D5 boundaries are AOT-safe typed services in `TerraRuntime.World`.

## Wiring

`VanillaWorldWiringMutationService` accepts named `WorldWireChannel` values and semantic place/kill wire, place/kill actuator and actuate/deactuate operations. It preserves tile, wall, paint and liquid state and commits through `WorldTileStore.Set` so network and persistence dirtiness cannot be skipped.

Actuation requires an active tile and an installed actuator. Circuit discovery, `WirePulse` traversal, device behavior, recursion suppression and bounded pulse scheduling remain separate parity work; packet action numbers never enter this service.

## Liquids

`VanillaWorldLiquidMutationService` owns `SetLiquid` and `ClearLiquid`. It validates the named `WorldLiquidKind`, canonicalizes an empty cell to zero Water state, preserves unrelated tile state and schedules the changed cell plus its in-bounds orthogonal neighbors in `WorldLiquidUpdateQueue`.

Material state and scheduler state remain distinct and both are already persisted by runtime world snapshots. `VanillaWorldLiquidSimulator1458` consumes the queue with a fixed per-tick budget, performs the verified ordinary same-kind gravity/horizontal settling slice, and reproduces the source-backed `Liquid.LiquidCheck` material reactions for water/lava/honey/shimmer. Merge products, the 24-unit threshold, the 276-entry final `tileObsidianKill` capability, the `21/467/88` container set and packet-20 `TileChangeType` identities are source-pinned. Supported lower `tileCut`, safe active `tileObsidianKill` replacement and the lower container override cross an authoritative prepare/commit side-effect boundary that owns drops/NPCs/network state; unsupported complex `WorldGen.ReplaceTile` targets fail before participating liquid is cleared. A material merge clears its participating liquids in vanilla order and is represented by one explicit packet-20 tile square rather than redundant packet-48 fanout for those merge clears. The ordinary dedicated-server `kill` lifecycle is also source-backed: one entry advances at most once per TerraRuntime tick, changed amounts reset `kill` and wake the cell above, stable `254` normalizes to `255` on retirement, and the retirement threshold follows TerrariaServer 1.4.5.8 `10 + activePlayersInSlots0To14 / 3`. Water below `Main.UnderworldLayer == maxTilesY - 200` loses two units per liquid update. The loading path now also has a source-backed quick-settle scheduler slice (fixed retirement threshold 8, lava/honey delay bypass and the `>250 -> 255` source refill) plus the bottom-up `Liquid.QuickWater` fast-settle pre-pass with its exact scan bounds, boulder/546 solidity exceptions and Bubble-379 barrier. Loading-time `LiquidCheck(..., false)` clears colliding liquids without creating runtime merge blocks. Remaining gaps are complex replacement/dependency/shape cases outside the safe active-merge subset, style-aware `WorldGen.WaterCheck` object death, full post-load orchestration/cache admission, and panic/forced-settle behavior.

## Growth and spread

`VanillaWorldGrowthMutationService` is the guarded commit boundary after a growth rule has selected an eligible cell. Requests carry typed expected and result tile identities plus the semantic `Grow` or `Spread` reason. The expected identity rejects stale queued work. Invalid, frame-important and multi-tile results fail closed; accepted ordinary transformations preserve wall, wires, liquid and paint while canonicalizing tile frame and shape state.

Random selection, light/biome/time checks, source-specific adjacency/support rules and bounded work queues belong to growth rule/scheduler implementations. The mutation boundary does not claim those vanilla families are complete.

## Roadmap status

This completes the D5 **decomposition** checkpoint: wiring, liquid and growth no longer need to share raw flag/field writes or packet-owned mutation code. The ordinary liquid flow/material-reaction runtime now uses those boundaries, but this does not claim full Terraria simulation parity. New circuit devices, the remaining liquid lifecycle/object-interaction paths and growth families must enter through these boundaries with source-backed rules and per-tick budgets.
