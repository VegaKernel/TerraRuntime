# Авторитетные операции изменения мира

TerraRuntime отделяет декодированное представление Terraria packet/file от авторитетного изменения мира. Runtime-owned семантическая реализация этого слоя — `VanillaWorldTileMutationService` в `TerraRuntime.World`.

## Область поддержки

Сервис владеет текущими проверенными однотайловыми операциями хранения:

- `PlaceTile` для обычных vanilla-тайлов без frame-important семантики, уже допущенных вызывающим gameplay-слоем;
- `KillTile` для обычных single-cell vanilla-тайлов, допущенных definition-моделью, плюс явно закреплённый поднабор `FrameImportantSingleCell`; остальной frame-important/multi-tile/object content идёт отдельными путями;
- `PlaceWall` и `KillWall` с типизированной проверкой `WallTypeId`;
- `SetShape` для solid-тайлов, которые не являются платформами и frame-important объектами;
- ограниченной канонизацией frame обычных тайлов и dirty propagation для сети и persistence.

Запрос задаётся как `WorldTileMutationRequest`. Этот API не принимает сырые номера packet action. Сетевой ingress сначала обязан декодировать и аутентифицировать пакет, а верхние gameplay-слои по-прежнему отвечают за reach, удерживаемый предмет/мощность инструмента, расход инвентаря, protection policy и резервирование drop.

Обычный mining теперь управляется definition-моделью. `VanillaTileDefinitionCatalog` — flyweight-таблица для каждого vanilla tile 1.4.5.8: она хранит mutation path, mining profile, drop rule и failed-pick transform. Положительного списка «разрешённых» обычных тайлов больше нет: простой single-cell removal является базовым поведением, а frame-important объекты, contextual drops, transforms и progression gates выбирают специализированный путь явно. Обычный Dirt идёт тем же simple-cell путём: наличие соседнего активного грунта не является причиной отклонять завершённый break из packet 17; commit сохраняет соседние клетки и через тот же reservation boundary создаёт drop Dirt Block.

## Почему generic `KillTile` работает fail-closed

Pinned source TerrariaServer 1.4.5.8 показывает, что внешне обычные тайлы не подчиняются одному generic mining rule. `Player.PickTile` накапливает hit damage до порога `100`; `Player.GetPickaxeDamage` содержит tile-specific пороги мощности; `WorldGen.CanKillTile` зависит от соседних тайлов, контейнеров и другого состояния мира; `Player.DoesPickTargetTransformOnKill` преобразует семейства вроде Grass в другой тайл вместо простого удаления.

Source contract сейчас закрепляет, среди прочего, порог `65` pick power для Ebonstone/Crimstone и `210` для Lihzahrd content, а Grass явно входит в transform-on-kill family. Поэтому считать любой не-frame-important тайл операцией `Type = 0; Active = false` было бы не упрощением, а повреждением поведения.

Storage mutation service принимает обычные definitions с `BreakPath = SimpleCell` и намеренно узкое семейство `FrameImportantSingleCell`, для которого независимо закреплены одноклеточный footprint и фиксированный drop 1.4.5.8. Gameplay-authority до commit разрешает мощность кирки, failed-pick transforms, contextual drop semantics и progression-sensitive requirements. Сейчас в этот framed single-cell срез входят Water Candle, Switch и шесть цветных Team Platforms. Обычные Platforms и Torches остаются fail-closed, потому что их drop/style semantics зависят от frame state. Multi-tile content использует отдельную object transaction. Production bridge packet 17 также допускает точную base Chest identity: согласованный 2x2 footprint, удаление runtime metadata и authoritative Chest item drop коммитятся как одна bounded operation; остальные frame-important/object families остаются fail-closed.

## Владение состоянием

Блок тайла, стена, набор проводов и жидкость являются независимыми частями runtime-state. Разрушение поддерживаемого обычного блока очищает принадлежащее блоку состояние (`Active`, тип тайла, shape, краску тайла, actuator/inactive и block visibility/fullbright flags), но сохраняет стену, её краску, провода и жидкость. Удаление стены очищает только принадлежащее стене состояние и не уничтожает сам блок.

Каждая зафиксированная запись ячейки проходит через `WorldTileStore.Set`, поэтому section seqlock versions, сетевые dirty sections и persistence dirty sections изменяются через один single-writer boundary.

## Framing

Для не-frame-important тайлов vanilla `.wld` не сохраняет значимые координаты tile frame. Поэтому TerraRuntime канонизирует затронутую окрестность `3×3` обычных тайлов в frame `(0,0)`, а не пытается воспроизводить выбор клиентского sprite frame внутри авторитетного storage. Изменение стены помечает dirty все секции, затронутые этой ограниченной frame-окрестностью.

Generic frame-important и известный multi-tile content по-прежнему отклоняются storage-сервисом. Единственное исключение — явный каталог `FrameImportantSingleCell`: его removal очищает ровно одну закреплённую ячейку и намеренно не переписывает frame соседей. Более широкий placement/break всё ещё требует проверенной геометрии `TileObjectData`, anchors, style/frame mapping и lifecycle метаданных, а не угадывания арифметики frame.

## Mining boundary packet 17

Packet 17 разрешает выбранный предмет через source-backed pick-tool catalog, после чего проверяет `VanillaTileDefinition` целевого тайла. Обычные single-cell тайлы идут по базовому mutation path; мощность кирки, contextual drops, failed-pick transforms и object/frame-important пути задаются типизированными правилами вместо списков сырых TileID.

Это **не** заявление о полной vanilla mining parity. TerraRuntime всё ещё не воспроизводит полный lifecycle накопления `HitTile`, все frame-important/object destruction rules, все world-position/progression gates и server-owned модель reach. Failed-pick transform families и полный drop-table simple-cell тайлов 1.4.5.8 теперь definition-driven; отсутствующая object/frame семантика остаётся явной fail-closed boundary.

`tools/ci/probe_tile_mining.py` закрепляет mining identities и requirements непосредственно по официальному бинарнику TerrariaServer 1.4.5.8. Drop-rule data отдельно pinned к `WorldGen.KillTile_GetItemDrops`; gameplay использует typed definitions, а не положительный allow-list сырых TileID.

## Статус roadmap

**Operation boundary** D5 для placement/break/framing остаётся завершённой в пределах объявленного поддерживаемого среза: typed requests, один authoritative commit owner, ограниченный simple framing и отдельная replication используются production path. Обычный single-cell mining больше не зависит от вручную поддерживаемого положительного TileID allow-list; immutable definitions типа выбирают mining, transform и drop behavior.

Полная mining parity всё ещё требует hit accumulation, оставшихся environment-dependent правил `CanKillTile`, frame-important/object destruction и reach/protection policy. Они должны добавляться через типизированные boundaries, а не повторным открытием generic packet-driven очистки ячеек.
