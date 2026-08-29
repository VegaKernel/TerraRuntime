# Документация TerraRuntime

[English](../en/README.md) · [README репозитория](../../README.md) · [Roadmap](../roadmap.md)

Этот каталог содержит русскую документацию TerraRuntime. Английская версия ведётся параллельно в `docs/en/` и должна описывать ту же фактическую версию кода.

## С чего начать

- [Руководство по проекту](project-guide.md) — назначение TerraRuntime, структура репозитория, сборка, запуск, жизненный цикл, сетевой и игровой поток, миры, сохранение и эксплуатация.
- [Архитектура](architecture.md) — границы подсистем, владение состоянием, потоки данных, threading model, NativeAOT/CoreCLR profiles, persistence и extension boundaries.
- [Интерфейсы host-интеграции](host-interfaces.md) — публичные контракты `TerraRuntime.HostContracts`, порядок жизни trusted host module и правила безопасного взаимодействия с runtime.

## Руководства по подсистемам и инженерным правилам

- [Сеть и протокол](networking-protocol.md) — framing, connection policy, граница Multiplicity, queues, rate accounting, stop reasons и join traffic.
- [Загрузка мира и persistence](world-persistence.md) — `.wld`, `.runtime-world`, live save shadow, atomic replacement, liquid state и recovery.
- [Gameplay и vanilla parity](gameplay.md) — players, items, tiles, chests, NPCs, projectiles, combat и явные parity gaps.
- [Synchronization и interest management](synchronization.md) — bootstrap, sections, replication registries, spatial tracking, hysteresis и текущая passthrough policy.
- [Performance и tick scheduling](performance-runtime.md) — $60\,\mathrm{Hz}$ authoritative loop, bounded mailbox/ingress/apply budgets, per-source fairness, missed-tick policy и правила performance evidence.
- [Operations и Terminal UI](operations-tui.md) — startup без аргументов, CLI defaults, lifecycle TUI, fallback console, telemetry и правила dashboard extensions.
- [Развёртывание и конфигурация](deployment-configuration.md) — NativeAOT/CoreCLR packaging, runtime directories, CLI configuration, trusted host-module loading и текущие deployment limitations.
- [Observability и logging](observability-logging.md) — bounded runtime telemetry/log buffers, текущее host-log behavior и незавершённый async structured logging target.
- [World generation](world-generation.md) — provider/pass/workspace/RNG contracts, trusted-host registration и текущий non-vanilla flat baseline.
- [Security и trust boundaries](security.md) — admission limits, rate/size bounds, failure isolation, persistence safety и незавершённая hardening work.
- [Testing, verification и evidence](testing-evidence.md) — политика roadmap checkbox, independent evidence, official-source/live-world probes, NativeAOT/CoreCLR gates и правила доказательства performance claims.

## Нормативные документы проекта

- [Основная дорожная карта](../roadmap.md) — текущее состояние реализации и обязательные следующие этапы.
- [Roadmap документации](../roadmap/documentation.md) — постоянное правило ведения RU/EN документации и coverage plan.
- [NativeAOT baseline](../native-aot-baseline.md) — требования к NativeAOT-совместимости и shipping gate.
- [Reference policy](../reference-policy.md) — иерархия источников при восстановлении vanilla-поведения.

## Правило актуальности документации

Документация является частью реализации, а не отдельной финальной фазой проекта.

Если изменение кода затрагивает наблюдаемое поведение, архитектуру, публичный интерфейс, CLI, формат конфигурации, deployment layout, persistence, lifecycle, threading/ownership rule или extension surface, соответствующая документация в `docs/ru/` и `docs/en/` обновляется в том же изменении.

Изменение не считается завершённым, если код и документация описывают разные версии TerraRuntime.

Для новой подсистемы сначала выбирается её каноническое место в документации. Не нужно создавать новый Markdown-файл на каждый класс: документация группируется по пользовательским и архитектурным концепциям, а не повторяет структуру исходников.

## Что документируем

Для каждой значимой подсистемы должны быть зафиксированы:

1. назначение и границы ответственности;
2. источник истины и владелец mutable state;
3. входы, выходы и основные типы данных;
4. порядок выполнения и lifecycle;
5. публичные/host-facing интерфейсы и примеры использования;
6. ограничения, гарантии безопасности и failure behavior;
7. persistence/networking implications, если применимо;
8. observability и диагностика;
9. известные несовпадения с vanilla и ещё не реализованные возможности;
10. ссылки на релевантные tests/roadmap/decision documents, когда решение требует отдельного обоснования.

## Языковая политика

Русская и английская версии равноправны. Имена типов, пакетов, packet IDs, CLI keys и другие машинные идентификаторы не переводятся. Смысл, ограничения и примеры должны оставаться эквивалентными в обеих версиях.
