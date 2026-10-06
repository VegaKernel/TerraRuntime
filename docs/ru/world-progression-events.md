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

Сервер владеет счётчиками, перемещением, периодичностью предупреждений и признаками завершения вторжений гоблинов, пиратов и марсиан. Доверенные запросы `packet 61` с кодами `-1`, `-3` и `-7` выполняются в авторитетной фазе команд. Размер события учитывает активные физические слоты игроков с исходным базовым максимумом здоровья от 200, включая мёртвых игроков. Перед принятием проверяются `UnifiedRandom` и все выбранные владельцы состояния; неизвестное импортированное здоровье, неподдерживаемое загруженное вторжение или устаревший владелец вызывают атомарный отказ. Обычные запросы запускают событие только при отсутствии текущего вторжения; `-7` всегда сбрасывает задержку и вызывает ветку запуска марсиан. Начальная репликация отправляет `packet 7`, затем исходный индикатор `packet 78`, который отличается от обычной синхронизации прогресса.

Сокетный bootstrap читает один неизменяемый снимок, опубликованный владельцем мира. На `packet 6` отправляются актуальный `7` и при необходимости прогресс вторжения `78`; неактивное обычное вторжение не отправляет прогресс. Последующий ответ на `packet 8` начинается с актуального `7` и статуса секций, сохраняя исходное различие между запросами. Переход между runtime захватывает свежий снимок назначения в его авторитетном потоке и отправляет `7`, затем при необходимости `78`, до секций назначения и `49`. Кодирование кадров использует отделённый снимок скалярных значений, не читая изменяемых владельцев из сокетного потока. Прогресс загруженного события с нулевым начальным размером остаётся знаковым: синхронизация входа использует максимум 1, а зачёт смерти сохраняет исходный максимум 0.

Поддерживаемые канонические гоблины получают контекст вторжения для дневного исчезновения и исходный порядок выбора при появлении. Выбор идентификатора отделён от поддержки поведения: выбранный неподдерживаемый Summoner отклоняется без замены другим NPC. Ограниченный выбор пиратов сохраняет обычные варианты и лимиты капитана и корабля; появление выбранного капитана или корабля отклоняется, пока не поддержаны жизненные циклы Captain/Ghost и связанных пушек. Естественный выбор марсиан и полные события вторжений остаются открытыми; Martian381 поддерживается только как определение. Независимо проверенные упорядоченные таблицы добычи теперь включают обычных пиратов и капитана, а специальные таблицы Parrot/Ghost проверены как пустые; данные добычи сами по себе не предоставляют поддержку AI или использования предметов. Смерть поддерживаемого гоблина или пирата принимает уменьшение счётчика после добычи и перед `78`, затем неактивным `23`; заменённое или ожившее поколение не получает устаревший зачёт.

Завершение принимает счётчики и постоянный признак победы до публикации. Исходный порядок: событие прогресса `98`, объявление `82`, мир `7`, затем оставшиеся предупреждения перемещения. Запрос Lantern Night при первой победе сохраняется как флаг; последующая ночная фаза RNG пока не заявляется. Контрольные сохранения сохраняют пять исходных полей вторжения, три признака завершения и принадлежащий серверу флаг будущей Lantern Night через отделённый снимок. Snow Legion и неизвестные загруженные состояния сохраняют исходное представление в файле, а живая симуляция отклоняется. Эти границы не означают полную совместимость планировщика `Main.Update` или всех вторжений.

Эти types завершают identity/state decomposition, а не full event simulation. Условия start/stop, waves, spawn pools, rewards, world transitions, announcements и replication остаются отдельными source-backed implementations. Чтение completed milestone само по себе не предоставляет эти gameplay consequences.
