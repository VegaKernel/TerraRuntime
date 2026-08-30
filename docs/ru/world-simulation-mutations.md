# Границы mutations wiring, liquids и growth

[English](../en/world-simulation-mutations.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime отделяет wiring, liquid material, liquid scheduling и growth commits от packet codecs и обычного tile placement. Начальные D5 boundaries представлены AOT-safe typed services в `TerraRuntime.World`.

## Wiring

`VanillaWorldWiringMutationService` принимает именованные `WorldWireChannel` и semantic operations place/kill wire, place/kill actuator и actuate/deactuate. Он сохраняет tile, wall, paint и liquid state и выполняет commit через `WorldTileStore.Set`, поэтому network и persistence dirtiness нельзя пропустить.

Actuation требует active tile и установленный actuator. Circuit discovery, traversal `WirePulse`, device behavior, recursion suppression и bounded pulse scheduling остаются отдельной parity-работой; packet action numbers в этот сервис не попадают.

## Liquids

`VanillaWorldLiquidMutationService` владеет `SetLiquid` и `ClearLiquid`. Он проверяет именованный `WorldLiquidKind`, canonicalizes empty cell в zero Water state, сохраняет unrelated tile state и планирует изменённую cell вместе с in-bounds orthogonal neighbors в `WorldLiquidUpdateQueue`.

Material state и scheduler state остаются отдельными и уже сохраняются runtime world snapshots. Flow, settling, реакции water/lava/honey/shimmer и bounded per-tick simulation consumer остаются явными capability gaps.

## Growth и spread

`VanillaWorldGrowthMutationService` является guarded commit boundary после того, как growth rule выбрало eligible cell. Requests содержат typed expected/result tile identities и semantic reason `Grow` или `Spread`. Expected identity отклоняет stale queued work. Invalid, frame-important и multi-tile results fail closed; принятые ordinary transformations сохраняют wall, wires, liquid и paint, одновременно canonicalizing tile frame и shape state.

Random selection, light/biome/time checks, source-specific adjacency/support rules и bounded work queues принадлежат реализациям growth rules/schedulers. Mutation boundary не заявляет завершённость этих vanilla families.

## Статус roadmap

Это завершает checkpoint **декомпозиции** D5: wiring, liquids и growth больше не должны делить raw flag/field writes или packet-owned mutation code. Это не заявление о полной Terraria simulation parity. Новые circuit devices, liquid reactions и growth families должны входить через эти boundaries с source-backed rules и per-tick budgets.
