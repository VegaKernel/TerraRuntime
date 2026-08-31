# Авторитетные операции изменения мира

TerraRuntime отделяет декодированное представление Terraria packet/file от авторитетного изменения мира. Runtime-owned семантическая реализация этого слоя — `VanillaWorldTileMutationService` в `TerraRuntime.World`.

## Область поддержки

Сервис владеет текущими проверенными однотайловыми операциями хранения:

- `PlaceTile` для обычных vanilla-тайлов без frame-important семантики, уже допущенных вызывающим gameplay-слоем;
- `KillTile` только для source-backed simple-removal среза `Dirt`, `Stone` и `Sand`;
- `PlaceWall` и `KillWall` с типизированной проверкой `WallTypeId`;
- `SetShape` для solid-тайлов, которые не являются платформами и frame-important объектами;
- ограниченной канонизацией frame обычных тайлов и dirty propagation для сети и persistence.

Запрос задаётся как `WorldTileMutationRequest`. Этот API не принимает сырые номера packet action. Сетевой ingress сначала обязан декодировать и аутентифицировать пакет, а верхние gameplay-слои по-прежнему отвечают за reach, удерживаемый предмет/мощность инструмента, расход инвентаря, protection policy и резервирование drop.

`VanillaSimpleTileKillCatalog` намеренно является каталогом **возможностей mutation**, а не глобальной таблицей mining hardness. Тайл попадает туда только тогда, когда generic storage transition и production drop path достаточно полно смоделированы и очистка ячейки не стирает молча vanilla-поведение.

## Почему generic `KillTile` работает fail-closed

Pinned source TerrariaServer 1.4.5.8 показывает, что внешне обычные тайлы не подчиняются одному generic mining rule. `Player.PickTile` накапливает hit damage до порога `100`; `Player.GetPickaxeDamage` содержит tile-specific пороги мощности; `WorldGen.CanKillTile` зависит от соседних тайлов, контейнеров и другого состояния мира; `Player.DoesPickTargetTransformOnKill` преобразует семейства вроде Grass в другой тайл вместо простого удаления.

Source contract сейчас закрепляет, среди прочего, порог `65` pick power для Ebonstone/Crimstone и `210` для Lihzahrd content, а Grass явно входит в transform-on-kill family. Поэтому считать любой не-frame-important тайл операцией `Type = 0; Active = false` было бы не упрощением, а повреждением поведения.

Пока для этих семейств не реализованы собственные типизированные семантики, generic mutation service возвращает `UnsupportedState`. В результате production packet-17 path больше не может медной киркой удалить Grass, Snow, Lihzahrd Brick или любой другой не реализованный тайл только потому, что его storage-definition не является frame-important.

## Владение состоянием

Блок тайла, стена, набор проводов и жидкость являются независимыми частями runtime-state. Разрушение поддерживаемого обычного блока очищает принадлежащее блоку состояние (`Active`, тип тайла, shape, краску тайла, actuator/inactive и block visibility/fullbright flags), но сохраняет стену, её краску, провода и жидкость. Удаление стены очищает только принадлежащее стене состояние и не уничтожает сам блок.

Каждая зафиксированная запись ячейки проходит через `WorldTileStore.Set`, поэтому section seqlock versions, сетевые dirty sections и persistence dirty sections изменяются через один single-writer boundary.

## Framing

Для не-frame-important тайлов vanilla `.wld` не сохраняет значимые координаты tile frame. Поэтому TerraRuntime канонизирует затронутую окрестность `3×3` обычных тайлов в frame `(0,0)`, а не пытается воспроизводить выбор клиентского sprite frame внутри авторитетного storage. Изменение стены помечает dirty все секции, затронутые этой ограниченной frame-окрестностью.

Frame-important и известный multi-tile content намеренно отклоняется generic-сервисом. Для его placement/break требуется проверенная геометрия `TileObjectData`, anchors, style/frame mapping и lifecycle метаданных, а не угадывание арифметики frame.

## Mining boundary packet 17

Текущий production proof инструмента для packet 17 намеренно узкий: выбранный предмет должен разрешаться в source-verified Copper Pickaxe (`pick = 35`). Успешная клиентская команда `KillTile` после этого может быть зафиксирована только тогда, когда target также входит в `VanillaSimpleTileKillCatalog`.

Это **не** заявление о полной vanilla mining parity. TerraRuntime пока не воспроизводит полный lifecycle накопления `HitTile`, полный каталог кирок, все world-position/progression gates, transforming tiles и server-owned модель reach. Всё это остаётся явными gameplay-задачами. Ключевой инвариант теперь в другом: отсутствующая семантика закрывается fail-closed, а не превращается в destructive generic behavior.

`tools/ci/probe_tile_mining.py` закрепляет эту boundary непосредственно по официальному бинарнику TerrariaServer 1.4.5.8. Изменения simple-kill каталога, mutation service или packet-level regression tests повторно запускают этот source contract.

## Статус roadmap

**Operation boundary** D5 для placement/break/framing остаётся завершённой в пределах объявленного поддерживаемого среза: typed requests, один authoritative commit owner, ограниченный simple framing и отдельная replication используются production path. Последнее усиление authority сужает generic `KillTile` до доказанного среза Dirt/Stone/Sand и превращает неподдерживаемые mining families в явные capability gaps.

Полная mining parity всё ещё требует source-backed breadth инструментов, hit accumulation, environment-dependent правил `CanKillTile`, transform-on-kill семейств, object destruction и reach/protection policy. Они должны добавляться через типизированные boundaries, а не повторным открытием generic packet-driven очистки ячеек.
