# Заселение Town NPC и домашнее расписание

TerraRuntime владеет source-backed проекцией TerrariaServer 1.4.5.8 для eligibility Town NPC и проводит подходящего жителя через authoritative путь выбора комнаты и materialization.

Evaluator покрывает vanilla-флаги `Main.UpdateTime_SpawnTownNPCs` и cadence `7200 / WorldGen.GetWorldUpdateRate()`. Он получает authoritative состояние игроков, а не транспортные пакеты: суммарную стоимость монет, максимальное здоровье и source-pinned inventory predicates для Arms Dealer, Demolitionist и Dye Trader. Persisted rescue/unlock state поддерживаемого набора кандидатов сохраняется при чтении `.wld` и в disposable prepared-world cache.

## Eligibility и точный source priority

Terraria не выбирает жителя простым правилом «первый true в `townNPCCanSpawn`». Тот же update-pass отдельно вычисляет `WorldGen.prioritizedTownNPCType`; теперь этот порядок явно представлен в `VanillaTownSpawnEligibility1458.PrioritizedType`. Приоритетный тип ставится первым в runtime-проекции кандидатов до выбора комнаты, а `CanSpawn` по-прежнему отражает полный набор eligible NPC.

Цепочка приоритета 1.4.5.8 закреплена по исходнику: seed-specific Dryad/Zoologist overrides, Guide/Merchant/Nurse progression, rescued residents, boss/progression residents, Princess, ненумерический порядок town slimes и хвост Bunny/Cat/Dog. Eligibility и priority остаются раздельными там, где они раздельны в vanilla: например, Tenth Anniversary seed может сделать Steampunker eligible, но в `prioritizedTownNPCType` она попадает только после победы над механическим боссом.

## Поиск комнаты, переселение и заселение

`RuntimeTownHouseCandidateIndex1458` сканирует мир инкрементально с фиксированным tile budget. Полный housing validator запускается только для tile identity, входящих в pinned наборы `RoomNeeds`. Комнаты дедуплицируются по canonical home tile и перед использованием всегда заново валидируются для конкретного NPC и текущих жильцов. Если игрок сломал найденный дом, устаревший кандидат fail-closed исключается из выбора.

Уже существующий homeless resident обрабатывается раньше materialization нового NPC, как в `WorldGen.UpdatePrioritizedTownNPC`. После kick-out запускается pinned 3600-tick look-for-home timeout. Когда переселение снова разрешено, валидная заранее назначенная `TownManager` комната получает первую попытку, а discovered-room index используется как fallback.

`RuntimeTownNpcStateStore` теперь сохраняет gameplay-visible порядок пар `TownRoomManager`, а не сортирует комнаты по NPC type. Загруженные пары остаются в порядке `.wld`, semantics `SetRoom` удаляет старую пару и добавляет новую в конец, а kick-out удаляет пару. Этот же ordered view используется для source-поведения `AddOccupantsToList`.

Для каждой проверяемой комнаты выбор нового жителя следует `IsThereASpawnablePrioritizedTownNPC`: сначала идут eligible occupants, уже назначенные именно в эту комнату, в порядке TownRoomManager; затем numeric scan по NPC ID предпочитает eligible type с любой назначенной комнатой, затем town pet и только потом оставляет `prioritizedTownNPCType` как fallback. Если у выбранного type есть собственная назначенная комната, она получает source recursive first attempt до текущего кандидата.

По source cadence `RuntimeTownNpcMoveInCoordinator1458` оценивает живое authoritative состояние игроков/inventory, применяет этот room-aware source selection, при наличии валидной комнаты выделяет generation-safe NPC slot, записывает жителя в `RuntimeTownNpcStateStore` и публикует packet 23 и packet 60 через существующего владельца NPC replication. Жители, созданные runtime, входят в следующие `.wld` snapshots NPC/TownRoom.

Mutable roster больше не предполагает, что все Town NPC навсегда занимают слоты `0..N-1`. Новый житель получает первый свободный vanilla NPC slot, поэтому существующий hostile NPC не может быть затёрт заселением.

## Возврат домой, отдых и кресла

`RuntimeTownNpcSchedule1458` реализует проверенный server-side shelter/resting slice AI_007. Ночь, дождь, eclipse, Slime Rain или переданный storm-above-surface condition требуют возвращения домой. Сохранён pinned допуск в семь tiles ночью при `ai[0] == 5`, а водочувствительные town entities 361/445/687 не проходят обычную проверку resting position, пока находятся в воде.

Поиск домашнего пола следует `SolidOrSlopedTileOrPlatform`: опорой может быть обычный solid tile, который не является solid-top, либо tile из pinned vanilla platform set. Ночью runtime ищет подходящее для NPC кресло в source-радиусе семь tiles по горизонтали, шесть вверх и два вниз с шагом два tiles по вертикали. Для chair types 15 и 497 применяется та же нормализация frame, что на официальном сервере; кресло, уже занятое другим сидящим Town NPC, повторно не выбирается.

Когда житель достигает выбранной resting tile, горизонтальная скорость сводится к нулю source-шагами по `0.1f`. Валидное свободное кресло затем фиксирует vanilla forced-sitting transition: `ai[0] = 5`, `ai[1] = 900 + rand(10800)`, направление из frame кресла, нулевую скорость, pinned bottom anchor и `localAI[3] = 0`. Сохранён запрещённый диапазон frame `1080..1098` для chair type 15. Town Dog, Town Bunny и все восемь town-slime types исключены из этого пути посадки.

Если житель находится вне resting area, runtime требует, чтобы и текущий, и домашний screen-sized safety rectangle были свободны от активных игроков. Destination проверяется в порядке `homeX`, `homeX - 1`, `homeX + 1`, сохранено исключение Old Man для obstruction check, Y-позиция фиксируется по pinned якорю `homeFloorY * 16 - height - 0.1f`, после чего сразу выполняется source post-teleport попытка посадки.

## Намеренно оставшиеся gaps

Move-in path пока не заявляет все детали `WorldGen.SpawnTownNPC`: полный physical off-screen/fallback поиск spawn point, exact более широкий WorldGen house-candidate/random fallback, локализованный transport `Announcement.HasArrived` и генерация given name/variation NPC остаются отдельной работой. В AI_007 отдельно остаются pet idle animations, social/emote/combat branches и live ownership изменений weather/eclipse/invasion. Bestiary-driven live updates Zoologist и случайный Party Girl roll также остаются fail-closed до появления mutable runtime-источников этих фактов.
