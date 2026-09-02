# Town NPC: жильё и магазины

TerraRuntime теперь владеет постоянным household-состоянием городских NPC, а не использует секции NPC и `TownRoomManager` из `.wld` только как данные начальной загрузки.

`TownNpcAuthority` является конкретным world-scoped владельцем композиции и authoritative lifecycle городских NPC. Он владеет housing validation, rescue/progression transforms, move-in scheduling, домашним расписанием, разрешением shop session, shimmer processing и оркестрацией town combat, но вызывается только существующим world writer. Теперь `NpcAuthority` координирует этот town-owner вместе с lifecycle обычных NPC, AI/combat/catch; `ServerRuntimeState` только маршрутизирует entity-команды и фазы тика, сохраняя их порядок.

## Реализованный срез 1.4.5.8

Runtime загружает постоянный список town NPC и соответствие комнат в `RuntimeTownNpcStateStore`, резервирует стабильные runtime-слоты для загруженных NPC и применяет проверенные по исходнику TerrariaServer 1.4.5.8 значения `SetDefaults` для обычных жителей, городских питомцев и town slimes. Old Man, Traveling Merchant и Skeleton Merchant намеренно не входят в этот постоянный household-каталог.

Пакет клиента `60` (`UpdateNpcHome`) декодируется как точный семибайтный payload Terraria 1.4.5.8. Клиент может запросить назначение комнаты (`status = 0`) или выселение (`status = 1`). `status = 2` остаётся состоянием, которое формирует только сервер. Запрос принимается только от соединения в состоянии Playing и применяется в authoritative game thread.

Назначение жилья использует clean-room реализацию проверки комнаты по исходнику: границы flood-fill, минимальный/максимальный размер, непрерывность безопасных стен, наборы `RoomNeeds` для стула/стола/света/двери, ограничение stinkbug, evil-room score, выбор точки проживания и совместимость по housing category из TownRoomManager. Два обычных жителя не могут занять одну комнату; городской питомец или slime может делить комнату с обычным жителем. Назначение Truffle пока fail-closed, потому что runtime ещё не владеет полным mushroom-scene/unlock условием.

Изменения дома реплицируются пакетом `60` всем Playing-клиентам и сохраняются как reconnect baseline. Save snapshot теперь отделяет `WorldNpcPersistence` и `WorldTownRoom[]`, а lossless world rewriter заменяет секции NPC и town rooms вместе с tiles/chests/signs, не оставляя в `.wld` устаревшее household-состояние.

## Срез инвентаря магазинов

Независимые от протокола каталоги магазинов, расчёт счастья, проверка условий появления городских NPC и связанные source-backed предикаты предметов находятся в `TerraRuntime.Gameplay.Npcs`. Изменяемый счётчик периодичности появления остаётся в Core, потому что это authoritative-состояние конкретного мира. Входные данные магазина используют общую типизированную идентичность `VanillaMoonPhase` из Contracts; значения enum вне допустимого диапазона отклоняются до расчёта ассортимента.

`VanillaTownShopCatalog1458` содержит проверенную по `Chest.SetupShop` логику состава товаров для всех обычных веток продавцов `1..18`, от Merchant до Stylist: Merchant, Arms Dealer, Dryad, Demolitionist, Clothier, Goblin Tinkerer, Wizard, Mechanic, Santa Claus, Truffle, Steampunker, Dye Trader, Party Girl, Cyborg, Painter, Witch Doctor, Pirate и Stylist.

Resolver сохраняет порядок исходника и реализованные условия прогресса: Hardmode, боссы/события, Blood Moon/Eclipse/день/ночь, biome/graveyard/sky/beach, secret-seed флаги, выбранную мировую руду, наличие других town NPC, golfer score, life/mana/team/монеты игрока и разблокировки от предметов. Каждый результат ограничен vanilla-ёмкостью магазина в 40 слотов.

`VanillaSpecialTownShopCatalog1458` реализует source-shaped специальные ветки `19..25`: Traveling Merchant, Skeleton Merchant, Tavernkeep, Golfer, Zoologist, Princess и второй декоративный магазин Painter. Модель сохраняет разреженные vanilla-слоты, custom coin prices и валюту Defender Medals, а не превращает эти магазины в обычный плоский список. Travel inventory, moon/time state, progression, bestiary completion, Golfer score и условия пилонов передаются явными входами.

`VanillaTownHappiness1458` реализует числовой путь `ShopHelper`: biome preferences, полный набор отношений допущенных NPC, отдельную семантику Princess, crowding дома/посёлка, штрафы homeless/far-home/evil biome, LoveStruck и vanilla clamp/rounding `0.75..1.5`. Локализованный текст настроения остаётся вне этого числового примитива.

## Что здесь не заявляется

Этот срез **не** заявляет полную parity town AI. Значения AI style `7` допущены для корректного hitbox/life/wire состояния постоянных жителей, но дневные/ночные schedules, teleport-home, взаимодействие с дверями/стульями, атаки, диалоги, локализованный happiness dialogue, rescue/transform lifecycle и специальные shimmer/seed ветки остаются отдельными parity-gates.

## Синхронизация разговора с NPC и магазина

Пакет 40 (`SetNpcTalk`) теперь декодируется на границе соединения, отправляется в authoritative loop и перед репликацией кодируется заново с подтверждённым слотом игрока. Переданный клиентом `player` не используется как источник истины. Значение NPC `-1` закрывает разговор, живые wire-слоты ограничены диапазоном `0..199`.

Таблица обычных продавцов защищена CI-контрактом на закреплённый TerrariaServer 1.4.5.8 `Chest.SetupShop`. Для веток `1..18`, включая диапазонные циклы Santa и Painter, проверяется точная последовательность предметов из исходного кода.


Truffle housing 1.4.5.8: до первого вселения нужна surface-комната (кроме `Main.NoFunctionalSurface`), минимум 100 mushroom tiles `70/71/72/528`; unlock сохраняется в `.wld`.

### Authoritative talk-to-shop mirror

Packet 40 теперь повторяет серверную часть `Player.SetTalkNPC`: после проверки authenticated player slot authoritative game thread разрешает live NPC, снимает packet-5 inventory/vitals/team state, сканирует pinned `169x124` SceneMetrics вокруг игрока, считает source-shaped housing crowding и числовой happiness, затем собирает обычный `Chest.SetupShop` или поддержанный special shop в immutable per-player session. Закрытие разговора очищает session, disconnect не даёт ей протечь в переиспользованный player generation.

Не принадлежащие runtime факты не подменяются выдумками: `LoveStruck`, live wind/weather, Golfer score, полный Bestiary/Fairy Torch state, Artisan Bread и Traveling Merchant `travelShop` отмечаются явными missing-fact flags.

### Rescue и жизненный цикл critter

Talk-rescue из TerrariaServer 1.4.5.8 теперь authoritative для Golfer Rescue, Bound Goblin, Bound Wizard, Bound Mechanic, Webbed Stylist, Sleeping Angler и лежащего без сознания Tavernkeep. Transform сохраняет NPC slot/generation, переносит позицию от старой нижней границы как `NPC.Transform`, превращает слот в persistent homeless resident и журналирует соответствующий `saved*` флаг для lossless-патча `.wld`.

Packet 70 (`CatchNPC`) декодируется как точный signed `Int16` NPC slot и применяется только game-loop owner. Runtime отдельно закрепляет полный `NPCID.Sets.CountsAsCritter` и все проверенные `catchItem` mapping: до despawn резервируется world-item slot, затем у authoritative player center создаётся 12x12 captured-critter item с vanilla spawn velocity и резервируется за authenticated player. Statue-spawned critter удаляется без предмета. Mystic Frog здесь fail-closed, потому что vanilla телепортирует его вместо обычного catch; Demon Tax Collector остаётся отдельным transform-путём от Purification Powder projectile 10.
