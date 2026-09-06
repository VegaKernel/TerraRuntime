# Границы mutations wiring, liquids и growth

[English](../en/world-simulation-mutations.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime отделяет wiring, liquid material, liquid scheduling и growth commits от packet codecs и обычного tile placement. Начальные D5 boundaries представлены AOT-safe typed services в `TerraRuntime.World`.

## Wiring

`VanillaWorldWiringMutationService` принимает именованные `WorldWireChannel` и semantic operations place/kill wire, place/kill actuator и actuate/deactuate. Он сохраняет tile, wall, paint и liquid state и выполняет commit через `WorldTileStore.Set`, поэтому network и persistence dirtiness нельзя пропустить.

Actuation требует active tile и установленный actuator. Circuit discovery, traversal `WirePulse`, device behavior, recursion suppression и bounded pulse scheduling остаются отдельной parity-работой; packet action numbers в этот сервис не попадают.

## Liquids

`VanillaWorldLiquidMutationService` владеет `SetLiquid` и `ClearLiquid`. Он проверяет именованный `WorldLiquidKind`, canonicalizes empty cell в zero Water state, сохраняет unrelated tile state и планирует изменённую cell вместе с in-bounds orthogonal neighbors в `WorldLiquidUpdateQueue`.

Material state и scheduler state остаются отдельными и уже сохраняются runtime world snapshots. `VanillaWorldLiquidSimulator1458` потребляет очередь с фиксированным per-tick budget, выполняет проверенный ordinary same-kind gravity/horizontal settling slice и воспроизводит source-backed material reactions `Liquid.LiquidCheck` для water/lava/honey/shimmer. Merge products, порог 24 units, финальная capability `tileObsidianKill` из 276 типов, container set `21/467/88` и identities `TileChangeType` packet 20 source-pinned. Поддержанные lower `tileCut`, безопасный active `tileObsidianKill` replacement и lower container override проходят через authoritative prepare/commit side-effect boundary, который владеет drops/NPC/network state; сложные неподдержанные targets `WorldGen.ReplaceTile` fail closed до очистки участвующей жидкости. Material merge очищает liquids в vanilla order и представляется одним explicit packet-20 tile square без лишнего packet-48 fanout для этих merge clears. Обычный dedicated-server `kill` lifecycle также source-backed: одна запись продвигается максимум на один logical liquid update за TerraRuntime tick, изменение amount сбрасывает `kill` и будит клетку сверху, стабильные `254` нормализуются в `255` при retirement, а порог retirement повторяет TerrariaServer 1.4.5.8 `10 + activePlayersInSlots0To14 / 3`. Water ниже `Main.UnderworldLayer == maxTilesY - 200` теряет две units за liquid update. Оставшиеся gaps: сложные replacement/dependency/shape cases вне безопасного active-merge subset, `quickFall`/`quickSettle`, panic/forced-settle behavior и полная parity post-load initialization.

## Growth и spread

`VanillaWorldGrowthMutationService` является guarded commit boundary после того, как growth rule выбрало eligible cell. Requests содержат typed expected/result tile identities и semantic reason `Grow` или `Spread`. Expected identity отклоняет stale queued work. Invalid, frame-important и multi-tile results fail closed; принятые ordinary transformations сохраняют wall, wires, liquid и paint, одновременно canonicalizing tile frame и shape state.

Random selection, light/biome/time checks, source-specific adjacency/support rules и bounded work queues принадлежат реализациям growth rules/schedulers. Mutation boundary не заявляет завершённость этих vanilla families.

## Статус roadmap

Это завершает checkpoint **декомпозиции** D5: wiring, liquids и growth больше не должны делить raw flag/field writes или packet-owned mutation code. Ordinary liquid flow/material-reaction runtime теперь использует эти boundaries, но это не заявление о полной Terraria simulation parity. Новые circuit devices, оставшиеся liquid lifecycle/object-interaction paths и growth families должны входить через эти boundaries с source-backed rules и per-tick budgets.
