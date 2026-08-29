# Архитектура TerraRuntime

[English](../en/architecture.md) · [Документация](README.md) · [Руководство](project-guide.md) · [Host interfaces](host-interfaces.md)

## 1. Архитектурная цель

TerraRuntime воспроизводит наблюдаемое поведение TerrariaServer 1.4.5.8 без сохранения его внутренней архитектуры. Главные ограничения проектирования:

- mutable simulation state имеет одного authoritative owner;
- network I/O не мутирует gameplay state напрямую;
- client input всегда недоверенный и bounded;
- blocking I/O, compression и тяжёлые фоновые операции не выполняются в hot path game loop;
- gameplay behavior отделяется от packet encoding/decoding;
- `.wld` остаётся каноническим persistent representation;
- runtime core сохраняет NativeAOT-совместимость;
- внешние hosts получают узкие explicit contracts вместо implementation objects.

## 2. Высокоуровневая схема

```text
                        +------------------------------+
                        |        CoreCLR profile       |
                        | trusted host module (Vega)   |
                        +---------------+--------------+
                                        |
                              TerraRuntime.HostContracts
                                        |
                                        v
+-------------+     +-------------------+-------------------+     +-------------+
| TCP clients | --> | Network / Protocol / command ingress  | --> | Game loop   |
+-------------+     +-------------------+-------------------+     | single owner|
                                                                    +------+------+ 
                                                                           |
                     +----------------------+-------------------------------+--------------------+
                     |                      |                               |                    |
                     v                      v                               v                    v
                  Players                  World                            NPCs             Projectiles/Items
                     |                      |                               |                    |
                     +----------------------+-------------------------------+--------------------+
                                                                           |
                                                                           v
                                                              sync/replication planning
                                                                           |
                                                                           v
                                                              bounded outbound queues
                                                                           |
                                                                           v
                                                                    socket writers
```

## 3. Dependency direction

Архитектура не должна превращаться в круговую зависимость между сетью, gameplay и host integration.

Концептуально зависимости идут так:

```text
Contracts
   ^
   |
Core / World / Protocol abstractions
   ^
   |
composition and adapters
   ^
   |
standalone host / extensible host / TUI
```

`TerraRuntime.HostContracts` не должен ссылаться на внутренние concrete runtime classes. Trusted host получает contracts и snapshots, а не `ServerRuntimeState`, mutable stores или socket objects.

## 4. Authoritative ownership

Один dedicated game-loop thread владеет изменяемым состоянием simulation.

К owned state относятся по мере реализации:

- player runtime state;
- NPC slots/handles/state;
- projectile slots/handles/state;
- world item state;
- mutable world/tile/progression state;
- connection-associated gameplay state после преобразования network input в command.

Другие threads могут:

- принимать сеть;
- декодировать bounded input;
- строить immutable work products;
- выполнять disk I/O;
- сериализовать snapshots;
- обновлять UI из immutable telemetry;
- возвращать результаты через explicit completion/command boundaries.

Они не могут самовольно менять authoritative collections.

## 5. Command boundary

Network input проходит ownership transfer до попадания в game loop.

```text
borrowed receive bytes
      |
      v
frame validation
      |
      v
owned decoded data / typed command
      |
      v
bounded authoritative queue
      |
      v
gameplay/state validation
      |
      v
mutation
```

Это разделяет две разные задачи:

1. можно ли безопасно разобрать bytes;
2. разрешено ли это действие в текущем gameplay/session state.

Decoder не должен принимать gameplay-решения, а gameplay не должен работать с временной памятью socket receive buffer.

## 6. Scheduling и fairness

Simulation schedule базово работает на 60 Hz.

Inbound command processing ограничивается бюджетами. Причина проста: один connection не должен получить возможность превратить `while(queue.TryRead(...))` в собственный персональный DoS primitive.

Runtime использует/развивает:

- hard global operation cap;
- per-source fairness quota;
- optional authoritative CPU-time cap;
- deferred-work counters;
- oldest backlog age;
- subsystem phase timing.

Бюджеты подсистем являются глобальными, если работа конкурирует за один simulation tick. Их нельзя механически умножать на player count.

## 7. Network architecture

Для каждого соединения существуют независимые read/write paths.

### Inbound

- socket read;
- incremental framing `[u16 length][u8 message id][payload]`;
- hard frame/message ceilings;
- protocol decode;
- connection-state legality;
- rate/work accounting;
- enqueue typed/owned command.

### Outbound

- authoritative state/event;
- recipient decision;
- packet projection/encode;
- bounded per-client queue;
- slow-client policy;
- socket writer.

Slow client не должен заставлять game loop ждать освобождения socket buffer.

## 8. Protocol boundary

`TerraRuntime.Protocol` задаёт runtime-facing protocol concepts. `TerraRuntime.Protocol.Multiplicity` адаптирует Multiplicity 2.7.x к этой границе.

Gameplay-код не должен зависеть от concrete Multiplicity packet classes там, где может работать с domain command/state.

Protocol-layer responsibility:

- wire framing;
- packet IDs и wire flags;
- bounded decode/encode;
- conversion из wire representation в owned semantic input;
- conversion из runtime projection в wire representation.

Gameplay-layer responsibility:

- legality;
- domain invariants;
- state transitions;
- authoritative outcomes.

## 9. Entity identity

Content type и live runtime identity — разные понятия.

```text
ProjectileTypeId  != projectile slot/handle
NpcTypeId         != NPC slot/handle
ItemTypeId        != inventory/world item identity
```

Runtime использует generation/revision-style identity там, где slot reuse может сделать stale reference опасным. Это защищает от ситуации, когда команда, относящаяся к старой сущности, случайно мутирует новую сущность в переиспользованном slot.

## 10. World architecture

World subsystem разделяет:

- canonical persistence (`.wld`);
- runtime tile/world representation;
- derived indexes/section state;
- encoded/compressed network sections;
- disposable runtime cache (`.runtime-world`).

Целевая dependency:

```text
.wld parser/serializer
       |
       v
validated world snapshot/state
       |
       v
runtime representation
       |
       +--> world queries/collision/liquids
       +--> gameplay mutations
       +--> section/sync state
       +--> save snapshot
       +--> derived runtime cache
```

Нельзя использовать derived cache как единственный источник восстановления мира.

## 11. Save architecture

Save pipeline обязан минимизировать stop-the-world работу authoritative thread.

```text
game loop
   |
short bounded snapshot handoff
   |
   v
background serializer/writer
   |
   v
temporary file
   |
flush/validate
   |
atomic replace
   |
canonical .wld
```

Runtime coalesces redundant save requests вместо накопления очереди сериализаций. Shutdown semantics должны гарантировать, что newest authoritative state не проиграет более старому background save.

## 12. Runtime world cache

`.runtime-world` ускоряет startup и может хранить prepared runtime state. Он versioned, disposable и проверяется относительно исходного `.wld` и собственной schema/integrity metadata.

Правила:

- cache miss — нормальное состояние;
- invalid cache → fallback на `.wld`;
- cache rebuild не предшествует успешному canonical save;
- cache corruption не считается world corruption;
- оптимизация принимается только при измеримом выигрыше `WorldReady`/`NetworkReady`.

## 13. Synchronization и interest management

Replication не обязана воспроизводить неэффективный vanilla broadcast algorithm, если observable behavior сохраняется.

Interest management является внутренней TerraRuntime subsystem. External host может только включить или выключить механизм через `IInterestManagementControl`.

Внутри runtime остаются:

- spatial partitioning;
- recipient sets;
- enter/leave transitions;
- hysteresis;
- forced resync deadlines;
- full-state-on-entry;
- entity-specific visibility rules.

До доказательства этих semantics packet suppression должна fail-open.

## 14. Gameplay decomposition

Gameplay не должен становиться одним giant packet switch.

Целевые ownership domains:

```text
Players
Items / Inventory / Use
NPC definitions / lifecycle / AI / combat / spawning
Projectile definitions / lifecycle / behavior / collision / combat
World tiles / objects / chests / signs / tile entities
Wiring / Liquids / Growth
Combat / Buffs / Loot
Events / Progression / Housing
World generation
```

Definition catalogs содержат version-pinned vanilla facts. Runtime stores содержат live state. Packet projection находится на внешней границе.

## 15. World generation architecture

Worldgen pipeline отделяет discovery от выполнения:

```text
generator registry
   -> selected provider
   -> plan builder
   -> validated pass graph/order
   -> isolated workspace
   -> deterministic execution
   -> final validation
   -> accepted runtime world candidate
```

Trusted host регистрирует provider, но TerraRuntime контролирует execution boundary и принятие результата.

Built-in flat generator — infrastructure baseline, не vanilla parity implementation.

## 16. NativeAOT и CoreCLR split

### NativeAOT profile

Проверяет, что core architecture:

- не зависит от JIT-only behavior;
- не требует arbitrary managed DLL loading;
- не опирается на reflection-driven discovery;
- проходит Linux/Windows native smoke.

### CoreCLR extensible profile

Добавляет trusted host-module loading. Это не отменяет AOT constraints core-проектов. Host-specific dynamic behavior должен оставаться за границей extensible host и не протекать обратно в runtime core.

## 17. Host integration boundary

Trusted host module получает две стадии API.

### Bootstrap environment

`ITerraRuntimeHostEnvironment` доступен до live runtime и содержит:

- root/deployment paths;
- dashboard registry;
- world-generator registry.

### Live runtime

`ITerraRuntimeHostRuntime` прикрепляется после старта authoritative runtime и предоставляет:

- runtime info;
- interest-management control;
- player snapshots;
- NPC actor operations;
- controlled server-player operations.

Ни один из этих contracts не является приглашением хранить mutable references на internal stores.

## 18. TUI architecture

TUI — consumer operations/read-model boundary.

```text
authoritative runtime
      |
immutable/bounded projections
      |
      v
TUI thread

TUI action
      |
controlled operation/command
      |
      v
authoritative runtime
```

UI toolkit не должен становиться dependency gameplay core.

## 19. Background workers

Worker получает snapshot или isolated buffer и возвращает результат. Запрещён pattern, где worker получает mutable world object и меняет его конкурентно с game loop.

Parallel gameplay/worldgen разрешается только после доказательства independence и deterministic equivalence там, где vanilla RNG/order имеет значение.

## 20. Failure containment

Trust boundaries должны локализовать ошибки.

- malformed frame не валит process;
- bad packet не пропускает mutation;
- save failure не заменяет good canonical file partial output;
- TUI failure не останавливает runtime readiness;
- stale/corrupt runtime cache не блокирует `.wld` fallback;
- host module не получает прямую mutable authority над internal state.

## 21. Observability

Telemetry должна объяснять, где runtime тратит время и почему отбрасывает работу, но не превращать каждый packet в аллокационный праздник.

Ключевые группы:

- tick CPU/wall и worst phase;
- command backlog/budget exhaustion;
- queue depth/slow-client drops;
- active entity counts;
- spatial membership;
- save/cache state;
- malformed/rejected protocol categories;
- GC/memory по мере доступности.

## 22. Архитектурный Definition of Done

При изменении архитектурной границы одновременно проверяются:

1. owner mutable state не размыт;
2. input/output contracts остаются bounded;
3. NativeAOT constraints не нарушены;
4. failure behavior определён;
5. тесты способны поймать регрессию;
6. RU и EN документация обновлена в том же изменении.
