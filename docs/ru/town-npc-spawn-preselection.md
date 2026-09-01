# Preselection Town NPC внутри SpawnTownNPC

TerraRuntime повторяет source-visible pre-materialization control flow `WorldGen.SpawnTownNPC` из TerrariaServer 1.4.5.8 до уже реализованного поиска physical spawn point.

## Сначала scoring комнаты для prioritized type

Vanilla не валидирует candidate room сразу для NPC, который в итоге будет выбран. `SpawnTownNPC` начинает с `prioritizedTownNPCType`, выполняет `StartRoomCheck`, `RoomNeeds`, stinkbug gate и `ScoreRoom(-1, prioritizedTownNPCType)` и только после положительного `hiScore` вызывает `IsThereASpawnablePrioritizedTownNPC(bestX, bestY)`.

`RuntimeTownNpcMoveInCoordinator1458` теперь сохраняет именно этот порядок. Каждый candidate сначала revalidate-ится для глобального `VanillaTownSpawnEligibility1458.PrioritizedType`. Затем room-aware selector использует **пересчитанный** home tile из этой проверки для semantics `TownRoomManager.AddOccupantsToList`, а не cached home tile, сохранённый индексом при первоначальном обнаружении комнаты.

## Guarded recursion в assigned room

Если у выбранного type есть `TownManager` room, vanilla рекурсивно вызывает `SpawnTownNPC(room.X, room.Y - 2)` при установленном `currentlyTryingToUseAlternateHousingSpot`. Рекурсивный вызов заново делает room scoring и `IsThereASpawnablePrioritizedTownNPC`; это не forced spawn того type, который запустил recursion. Поэтому assigned room вполне может выбрать другого eligible occupant этой комнаты.

Runtime теперь моделирует это как одноуровневый guarded recursive candidate resolution. Только успешный recursive selection заменяет внешний candidate. Если alternate room блокируется, управление возвращается к исходной комнате, как в source. Рекурсивная комната проверяется из source seed `room.Y - 2`; совпадение с устаревшим cached canonical home не навязывается.

## Финальный exact-home occupancy gate

Непосредственно перед physical materialization vanilla вызывает `IsRoomConsideredAlreadyOccupied(bestX, bestY, prioritizedTownNPCType)`. Проверка compatibility намеренно использует глобальный prioritized type, даже если фактически выбран другой spawn type. Учитываются только active housed Town NPC с точным совпадением `homeTileX/homeTileY`; блокировка происходит, когда `TownRoomManager.CanNPCsLiveWithEachOther` видит одинаковую housing category.

TerraRuntime сохраняет эту странность явно, а не «исправляет» её до проверки по selected type.

## Проверка и оставшаяся граница

Focused regressions покрывают recursive reselection внутри assigned room и финальный occupancy gate по housing category глобального prioritized type. Отдельный CI source contract фиксирует порядок `SpawnTownNPC`, `IsThereASpawnablePrioritizedTownNPC`, `IsRoomConsideredAlreadyOccupied` и `TownRoomManager.CanNPCsLiveWithEachOther` по официальной сборке сервера 1.4.5.8.

Этот slice **не** заявляет точное получение исходных координат house probe. TerraRuntime пока использует bounded house-candidate index вместо полного воспроизведения random stream `UpdateWorld`/`TrySpawningTownNPC`, 300-point sampling из `CheckForHousesNearAPlayer` и random fallback `SpawnHomelessNPC`/`LastFoundHouse`. Это остаётся следующей границей WorldGen/Town integration.
