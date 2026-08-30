# Авторитетные операции изменения мира

TerraRuntime отделяет декодированное представление Terraria packet/file от авторитетного изменения мира. Первая runtime-owned семантическая реализация этого слоя — `VanillaWorldTileMutationService` в `TerraRuntime.World`.

## Область поддержки

Сервис владеет текущими проверенными однотайловыми операциями хранения:

- `PlaceTile` для обычных vanilla-тайлов без frame-important семантики;
- `KillTile` для обычных vanilla-тайлов без frame-important семантики;
- `PlaceWall` и `KillWall` с типизированной проверкой `WallTypeId`;
- `SetShape` для solid-тайлов, которые не являются платформами и frame-important объектами;
- ограниченной канонизацией frame обычных тайлов и dirty propagation для сети и persistence.

Запрос задаётся как `WorldTileMutationRequest`. Этот API не принимает сырые номера packet action. Сетевой ingress сначала обязан декодировать и аутентифицировать пакет, а верхние gameplay-слои по-прежнему отвечают за reach, удерживаемый предмет/мощность инструмента, расход инвентаря, protection policy и резервирование drop.

## Владение состоянием

Блок тайла, стена, набор проводов и жидкость являются независимыми частями runtime-state. Разрушение обычного блока очищает принадлежащее блоку состояние (`Active`, тип тайла, shape, краску тайла, actuator/inactive и block visibility/fullbright flags), но сохраняет стену, её краску, провода и жидкость. Удаление стены очищает только принадлежащее стене состояние и не уничтожает сам блок.

Каждая зафиксированная запись ячейки проходит через `WorldTileStore.Set`, поэтому section seqlock versions, сетевые dirty sections и persistence dirty sections изменяются через один single-writer boundary.

## Framing

Для не-frame-important тайлов vanilla `.wld` не сохраняет значимые координаты tile frame. Поэтому TerraRuntime канонизирует затронутую окрестность `3×3` обычных тайлов в frame `(0,0)`, а не пытается воспроизводить выбор клиентского sprite frame внутри авторитетного storage. Изменение стены помечает dirty все секции, затронутые этой ограниченной frame-окрестностью.

Frame-important и известный multi-tile content намеренно отклоняется generic-сервисом. Для его placement/break требуется проверенная геометрия `TileObjectData`, anchors, style/frame mapping и lifecycle метаданных, а не угадывание арифметики frame.

## Существующий packet-17 путь Dirt

`VanillaDirtPlacement` остаётся строгим source-backed preflight/compatibility facade для уже допущенного Dirt-среза packet 17. Его isolation proof не ослабляется. `ServerRuntimeState` владеет одним долгоживущим `VanillaWorldTileMutationService`, и все принятые commits placement/removal Dirt проходят через этот typed operation boundary. Inventory/tool policy, резервирование drop, storage mutation и replication поэтому остаются отдельными стадиями, а packet handlers не записывают tile fields напрямую.

## Статус roadmap

Это завершает **operation boundary** D5 для placement/break/framing в поддерживаемом ordinary single-tile slice: typed requests, один authoritative commit owner, ограниченный simple framing и отдельная replication используются production path. Это не заявление о полной Terraria parity. Multi-tile placement, attachment/support rules, создание/удаление object metadata и полные visual framing families остаются явными capability gaps и должны добавляться через ту же boundary, а не packet-specific записи.
