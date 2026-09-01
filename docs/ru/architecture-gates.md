# Исполняемые архитектурные ограничения

[English](../en/architecture-gates.md) · [Архитектура](architecture.md) · [Roadmap декомпозиции gameplay](../roadmap/gameplay-decomposition-and-catalogs.md)

## Назначение

Архитектурные правила TerraRuntime являются исполняемыми ограничениями, а не только схемами. `RuntimeArchitectureBoundaryTests` запускается в обычном наборе `TerraRuntime.Tests`, который основной CI workflow выполняет после Release-сборки.

Тесты анализируют metadata уже собранных assemblies. Поэтому gate не зависит от раскладки source-файлов и ловит зависимость в тот момент, когда она действительно становится runtime assembly reference.

## Защищаемые границы

### Независимость от внешних server/runtime проектов

Каждая `TerraRuntime.*` assembly, достижимая из production roots, проверяется на прямые references с именами, начинающимися на `Terraria`, `TShock`, `OTAPI` или `Vega`.

Такие зависимости запрещены. TerrariaServer 1.4.5.8, TShock/OTAPI и Vega могут использоваться как reference material или внешний host, но не являются runtime implementation dependencies TerraRuntime.

### Изоляция Multiplicity adapter

`Multiplicity` разрешён только в `TerraRuntime.Protocol.Multiplicity`. Gate проверяет обе стороны контракта:

- adapter по-прежнему ссылается на пакет Multiplicity;
- никакая другая production assembly не ссылается напрямую на assembly семейства `Multiplicity*`.

Так protocol abstraction не превращается постепенно в декорацию, пока удобные packet types растекаются по gameplay или host-коду.

### Направление foundation dependencies

Для нижних слоёв закреплён намеренно небольшой allow-set:

| Assembly | Разрешённые прямые `TerraRuntime*` references |
| --- | --- |
| `TerraRuntime.Contracts` | нет |
| `TerraRuntime.Core` | `TerraRuntime.Contracts` |
| `TerraRuntime.HostContracts` | `TerraRuntime.Contracts` |
| `TerraRuntime.Protocol` | нет |
| `TerraRuntime.World` | `TerraRuntime.Contracts` |
| `TerraRuntime.Network` | `TerraRuntime.Contracts`, `TerraRuntime.Protocol` |
| `TerraRuntime.Protocol.Multiplicity` | `TerraRuntime.Contracts`, `TerraRuntime.Protocol`, `TerraRuntime.World` |

Тест запрещает новые прямые production dependencies за пределами allow-set. При этом он не требует, чтобы каждый разрешённый edge существовал всегда, поэтому уменьшение coupling не требует правки теста.

### Public surface HostContracts

`TerraRuntime.HostContracts` может публиковать собственные типы, общие типы `TerraRuntime.Contracts` и framework/third-party presentation types, которые намеренно входят в host API. Он не может публиковать типы concrete TerraRuntime implementation assemblies, например Core, World, Network, protocol adapters или server executable.

Тест проходит по exported types, base types/interfaces и public signatures constructors, methods, properties, fields и events, рекурсивно учитывая generic и element types.

## Поведение в CI

Основной workflow выполняет:

```text
dotnet build build/TerraRuntime.slnx -c Release --no-restore
dotnet test tests/TerraRuntime.Tests/TerraRuntime.Tests.csproj -c Release --no-build
```

Поэтому нарушение архитектуры ломает тот же обязательный build/test path, что и behavioral regression. Отдельный source-regex job для этих assembly-level правил не нужен.

## Изменение границы

Если новая dependency действительно необходима, архитектурная документация и executable allow-set должны изменяться вместе в одном reviewable commit. Нельзя расширять allow-set только ради зелёного теста. Красный gate здесь и есть полезный сигнал: dependency direction должна меняться осознанно, а не случайно после добавления удобного reference.
