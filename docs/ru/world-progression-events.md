# World progression и event state

[English](../en/world-progression-events.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

TerraRuntime проецирует validated `.wld` runtime metadata в gameplay-owned views progression и events. Persistence field order и raw invasion values остаются на world-file boundary.

## Permanent progression

`VanillaWorldProgressionId` именует 36 permanent milestones TerrariaServer 1.4.5.8: bosses, уже побеждённые invasions, Hardmode, celestial pillars, tiers Old One's Army и source-backed unlock events вроде разбитого Shadow Orb. `VanillaWorldProgressionState.IsComplete` запрашивает immutable runtime snapshot без раскрытия packed persistence bits.

`WorldFileRuntimeMetadata.Progression` выполняет explicit projection field-to-milestone. Event activity, weather и temporary holidays не смешиваются с этим state.

## Владение runtime mutations

Каждый живой `WorldRuntime` владеет одним journal `RuntimeWorldProgressionMutations` для progression, произведённой после загрузки. Тот же journal явно передаётся в NPC/town gameplay и в capture persistence snapshot, поэтому два runtime не могут случайно сойтись на одном mutable progression через process-static lookup. Baseline facts из `.wld` остаются отделены от новых mutations, поэтому save patching сохраняет только состояние, которое действительно изменил этот runtime.

## Active events и invasions

`VanillaWorldInvasionId` закрепляет официальный диапазон из пяти invasion values: none, Goblin Army, Snow Legion, Pirate Invasion и Martian Madness. Unknown persisted values проецируются в `Unknown` и fail closed вместо превращения в valid gameplay event.

`VanillaWorldEventState` отдельно предоставляет activity Blood Moon, Eclipse, Slime Rain, Party, Lantern Night, Sandstorm, Halloween и Christmas. Persistence variants manual/genuine и today/forever нормализуются в один semantic active state.

## Идентичность времени мира

`VanillaMoonPhase` именует точный восьмизначный цикл фаз луны Terraria 1.4.5.8. `VanillaMoonPhases` валидирует persistence primitives и владеет переходом через конец цикла, поэтому authoritative runtime clock не сравнивает и не сбрасывает необъяснённые raw phase numbers. Типизированное значение снова преобразуется в byte только на границе patch world-файла.

## Capability boundary

Сервер владеет счётчиками, перемещением, предупреждениями и завершением Goblin, Frost Legion, Pirate и Martian. Клиентские `-1`, `-2`, `-3` требуют расхода выбранного предмета из серверного бюджета при отсутствии вторжения. Клиентский `-7` отклоняется: Martian Probe нужен собственный авторитетный производитель. Размер учитывает активные физические слоты с исходным базовым максимумом здоровья от 200, включая мёртвых участников подсчёта. Неизвестное здоровье, неподдержанная metadata или устаревший владелец вызывают атомарный отказ. Расход, `UnifiedRandom` и событие принимаются до observers. Начальная репликация сохраняет `7`, затем исходный индикатор `78`, отличающийся от обычного прогресса. См. [границы оплаченного вызова](paid-event-invocation-1458.md).

Сокетный bootstrap читает один неизменяемый снимок, опубликованный владельцем мира. На `packet 6` отправляются актуальный `7` и при необходимости прогресс вторжения `78`; неактивное обычное вторжение не отправляет прогресс. Последующий ответ на `packet 8` начинается с актуального `7` и статуса секций, сохраняя исходное различие между запросами. Переход между runtime захватывает свежий снимок назначения в его авторитетном потоке и отправляет `7`, затем при необходимости `78`, до секций назначения и `49`. Кодирование кадров использует отделённый снимок скалярных значений, не читая изменяемых владельцев из сокетного потока. Прогресс загруженного события с нулевым начальным размером остаётся знаковым: синхронизация входа использует максимум 1, а зачёт смерти сохраняет исходный максимум 0.

Допущенные канонические гоблины получают контекст дневного исчезновения и исходный выбор; неподдержанный Summoner отклоняется без замены. Выбор пиратов сохраняет обычные варианты и лимиты Captain/ship; связанный Captain/Ghost работает, корабль/пушки не допущены. Естественный выбор Frost/Martian и полные события остаются открытыми; Martian381 поддерживается только как определение. Существующая упорядоченная добыча Goblin/Pirate сохранена. Десять проверенных обычных семейств принимают точное удаление поколения, зачёт, добычу и RNG до observers, затем выпускают вывод в исходном порядке: добыча, `78`, неактивный `23`. Вложенные смерти, исключения callback и замены не повторяют награды и не откатывают новое событие. Возможный или неизвестный производитель Dungeon Spirit после Plantera отклоняется до HP до появления его удерживаемого владельца; доказанное раннее ложное условие исходной ветки разрешает обычный путь.

Смерть капитана принимает пять единиц зачёта вторжения вместе с drops, рождением Ghost и удалением родителя до observers; fade-смерть Ghost даёт одну единицу через ту же связанную транзакцию. Producer работает и вне активного вторжения, включая отсутствие optional invasion owner, без выдуманного зачёта. Размер `5 → 0` завершается на следующем шаге invasion lifecycle при ещё живом Ghost; размер `6 → 1` требует последующей единицы зачёта смерти Ghost, чтобы дойти до нуля. Raw-счётчики/currentness и вложенная публикация проверяются независимо. Настроенный связанный корпус подтверждает принятие скалярного lifecycle и буквальный `98`, а не новое полное воспроизведение `WorldRuntime` `98 → 82 → 7`. Production-интеграция завершена; общая checkpoint-acceptance и Native-проверка ещё ожидаются.

Завершение принимает счётчики и постоянный признак до публикации: `98`, объявление `82`, мир `7`, затем остальные предупреждения. Запрос первой Lantern Night сохраняется; его ночной потребитель остаётся открытым. Отделённые checkpoints сохраняют пять полей вторжения, четыре признака завершения и будущую Lantern Night. Frost Legion имеет живого владельца; неизвестные типы сохраняют raw metadata и отклоняются симуляцией. [Часы событий](event-clock-authority-1458.md) и [лунные смерти](moon-event-death-authority-1458.md) описывают живые сохраняемые флаги, временные волны и точную область поддержки. Полный планировщик `Main.Update` и совместимость целых сражений остаются открытыми.

Эти types завершают identity/state decomposition, а не full event simulation. Условия start/stop, waves, spawn pools, rewards, world transitions, announcements и replication остаются отдельными source-backed implementations. Чтение completed milestone само по себе не предоставляет эти gameplay consequences.
