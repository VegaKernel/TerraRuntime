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

Эти types завершают identity/state decomposition, а не full event simulation. Условия start/stop, waves, spawn pools, rewards, world transitions, announcements и replication остаются отдельными source-backed implementations. Чтение completed milestone само по себе не предоставляет эти gameplay consequences.
