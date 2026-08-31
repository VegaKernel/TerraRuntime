# Заселение Town NPC и домашнее расписание

TerraRuntime владеет source-backed проекцией TerrariaServer 1.4.5.8 для eligibility Town NPC и теперь проводит подходящего жителя через authoritative путь выбора комнаты и materialization.

Evaluator покрывает vanilla-флаги `Main.UpdateTime_SpawnTownNPCs` и cadence `7200 / WorldGen.GetWorldUpdateRate()`. Он получает authoritative состояние игроков, а не транспортные пакеты: суммарную стоимость монет, максимальное здоровье и source-pinned inventory predicates для Arms Dealer, Demolitionist и Dye Trader. Persisted rescue/unlock state поддерживаемого набора кандидатов сохраняется при чтении `.wld` и в disposable prepared-world cache.

## Поиск комнаты и заселение

`RuntimeTownHouseCandidateIndex1458` сканирует мир инкрементально с фиксированным tile budget. Полный housing validator запускается только для tile identity, входящих в pinned наборы `RoomNeeds`. Комнаты дедуплицируются по canonical home tile и перед использованием всегда заново валидируются для конкретного NPC и текущих жильцов. Если игрок сломал найденный дом, устаревший кандидат fail-closed исключается из выбора.

По source cadence `RuntimeTownNpcMoveInCoordinator1458` оценивает живое authoritative состояние игроков/inventory, выбирает первый eligible type, для которого есть валидная свободная комната, выделяет generation-safe NPC slot, записывает жителя в `RuntimeTownNpcStateStore` и публикует packet 23 и packet 60 через существующего владельца NPC replication. Жители, созданные runtime, входят в следующие `.wld` snapshots NPC/TownRoom.

Mutable roster больше не предполагает, что все Town NPC навсегда занимают слоты `0..N-1`. Новый житель получает первый свободный vanilla NPC slot, поэтому существующий hostile NPC не может быть затёрт заселением.

## Возврат домой

`RuntimeTownNpcSchedule1458` реализует проверенный server-side shelter slice AI_007. Ночь, дождь, eclipse, Slime Rain или переданный storm-above-surface condition требуют возвращения домой. Сохранён pinned допуск в семь tiles ночью при `ai[0] == 5`. Если житель находится вне resting area, runtime соблюдает broad safety policy официального сервера: и текущий, и домашний screen-sized safety rectangle должны быть свободны от активных игроков. Destination проверяется в порядке `homeX`, `homeX - 1`, `homeX + 1`, а позиция фиксируется по pinned якорю `homeFloorY * 16 - height - 0.1f`.

## Намеренно оставшиеся gaps

Это не заявка на полный AI_007. Exact randomized priority/fallback `WorldGen`, локализованный transport `Announcement.HasArrived`, генерация given name NPC, выбор стула/sitting animation, social/emote/combat branches и live ownership изменений weather/eclipse/invasion остаются отдельной работой. Сейчас host может проецировать persisted rain/eclipse/invasion в schedule, а будущие authoritative event systems должны заменить эти начальные факты, когда события станут mutable во время игры. Bestiary-driven первый Zoologist и случайный первый Party Girl также остаются fail-closed до появления live источников этих фактов.
