# Wiring, liquid and growth mutation boundaries

[Русский](../ru/world-simulation-mutations.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime keeps wiring, liquid material, liquid scheduling and growth commits separate from packet codecs and from ordinary tile placement. The initial D5 boundaries are AOT-safe typed services in `TerraRuntime.World`.

## Wiring

`VanillaWorldWiringMutationService` accepts named `WorldWireChannel` values and semantic place/kill wire, place/kill actuator and actuate/deactuate operations. It preserves tile, wall, paint and liquid state and commits through `WorldTileStore.Set` so network and persistence dirtiness cannot be skipped.

Actuation requires an active tile and an installed actuator. Circuit discovery, `WirePulse` traversal, device behavior, recursion suppression and bounded pulse scheduling remain separate parity work; packet action numbers never enter this service.

## Liquids

`VanillaWorldLiquidMutationService` owns `SetLiquid` and `ClearLiquid`. It validates the named `WorldLiquidKind`, canonicalizes an empty cell to zero Water state, preserves unrelated tile state and schedules the changed cell plus its in-bounds orthogonal neighbors in `WorldLiquidUpdateQueue`.

Material state and scheduler state remain distinct and both are already persisted by runtime world snapshots. Flow, settling, water/lava/honey/shimmer reactions and the bounded per-tick simulation consumer remain explicit capability gaps.

## Growth and spread

`VanillaWorldGrowthMutationService` is the guarded commit boundary after a growth rule has selected an eligible cell. Requests carry typed expected and result tile identities plus the semantic `Grow` or `Spread` reason. The expected identity rejects stale queued work. Invalid, frame-important and multi-tile results fail closed; accepted ordinary transformations preserve wall, wires, liquid and paint while canonicalizing tile frame and shape state.

Random selection, light/biome/time checks, source-specific adjacency/support rules and bounded work queues belong to growth rule/scheduler implementations. The mutation boundary does not claim those vanilla families are complete.

## Roadmap status

This completes the D5 **decomposition** checkpoint: wiring, liquid and growth no longer need to share raw flag/field writes or packet-owned mutation code. It does not claim full Terraria simulation parity. New circuit devices, liquid reactions and growth families must enter through these boundaries with source-backed rules and per-tick budgets.
