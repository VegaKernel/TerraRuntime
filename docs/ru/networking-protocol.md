# Сеть и протокол

[English](../en/networking-protocol.md) · [Документация](README.md) · [Архитектура](architecture.md) · [Roadmap](../roadmap.md)

## 1. Область документа

Здесь описан фактически существующий сегодня сетевой и Terraria protocol path TerraRuntime. Документ фиксирует реализованные границы и действующие правила безопасности, а не выдаёт весь целевой roadmap за готовую систему.

Базовая версия протокола: Terraria **1.4.5.8**, protocol **326**. Multiplicity 2.7.x используется как базовая typed packet implementation за внутренней protocol boundary TerraRuntime. Когда реализация и round-trip test расходятся с реальным поведением, окончательным источником истины остаются официальный TerrariaServer 1.4.5.8 и независимые захваты трафика официального клиента.

## 2. Карта слоёв

```text
TCP socket
   |
   v
TerraRuntime.Network
   |  incremental framing, connection policy, bounded queues
   v
TerraRuntime.Protocol
   |  runtime-facing protocol abstractions
   v
TerraRuntime.Protocol.Multiplicity
   |  адаптеры Multiplicity / typed wire models
   v
owned semantic input
   |
   v
authoritative game-loop command boundary
   |
   v
gameplay/state validation и mutation
```

Обратный путь:

```text
authoritative state/event
   -> runtime packet projection
   -> protocol encode
   -> bounded per-connection outbound queue
   -> один writer соединения
   -> TCP
```

Packet decoder не владеет gameplay policy. Socket callback не владеет игровым состоянием.

## 3. Framing

Terraria frame использует проверенный runtime envelope:

```text
[u16 total frame length][u8 message id][payload...]
```

Network layer инкрементально обрабатывает:

- frame, разбитый между несколькими socket reads;
- несколько frames внутри одного read;
- невозможные/некорректные длины;
- oversized messages с явными ceilings;
- оборванный input при завершении соединения.

Размеры, объявленные клиентом, считаются недоверенными. Они не могут напрямую определять неограниченный размер allocation.

Framing отвечает только на вопрос, можно ли безопасно выделить protocol frames из последовательности байтов. Message-specific decode и gameplay legality являются отдельными стадиями.

## 4. Владение receive buffer

Данные от socket являются временными borrowed data. Gameplay не должен хранить ссылку в receive buffer после продвижения read pipeline.

Переход владения выглядит так:

```text
borrowed socket bytes
   -> validated frame
   -> decoded/owned values
   -> typed command или owned frame data
   -> authoritative queue
```

Это особенно важно для `Span<T>`, `ReadOnlySpan<T>`, `ReadOnlySequence<T>` и pooled buffers: низкое количество allocations не должно превращаться в ошибку времени жизни данных.

## 5. Connection policy

`TerrariaConnectionPolicyState` отслеживает timeout и terminal-stop state отдельно от gameplay state.

Текущая default policy задаёт:

- **10 секунд** на завершение protocol handshake;
- **2 минуты** как консервативный hard-abuse ceiling на завершение join после `Hello`, пока runtime не сообщает ready/`Playing`;
- отсутствие обычного post-join idle timeout (`Timeout.InfiniteTimeSpan`);
- connection rate budget `HardAbuse`;
- набор per-message rate limits `HardAbuse`.

Двухминутный join deadline не является vanilla gameplay timing rule. Он не позволяет peer завершить дешёвую стадию protocol `Hello`, а затем бесконечно удерживать admitted player slot, не входя в мир.

Успешный handshake фиксирует timestamp завершения handshake и немедленно будит watchdog. Production sink composition сообщает network layer только узкий `ITerrariaConnectionReadinessSource` signal и не протаскивает gameplay objects в network policy. Пока connection не ready, watchdog применяет join deadline. После перехода readiness в true join deadline перестаёт действовать, а connection переходит на настроенную обычную idle policy.

Timeout state монотонен: после фиксации terminal stop reason последующая активность не может заменить его другой причиной.

## 6. Stop reasons и категории отказов

`TerrariaConnectionStopReason` сейчас различает:

| Reason | Значение |
|---|---|
| `PeerClosed` | удалённая сторона нормально закрыла соединение |
| `ApplicationStopped` | runtime завершает работу |
| `Cancelled` | execution соединения отменён |
| `HandshakeTimeout` | protocol handshake не завершён до deadline |
| `JoinTimeout` | `Hello` завершён, но connection не достиг ready/`Playing` до join deadline |
| `IdleTimeout` | истёк настроенный post-join inactivity deadline |
| `InvalidHandshake` | handshake bytes/state структурно некорректны |
| `UnsupportedProtocol` | версия/protocol клиента не поддерживается |
| `ProtocolFailure` | ошибка protocol processing после framing |
| `InboundIoFailure` | ошибка socket/read-side I/O |
| `OutboundFailure` | ошибка encode/write-side path |
| `SlowClient` | bounded outbound policy отключила клиента, который не успевал принимать данные |
| `RateLimited` | configured connection/message budget отклонил traffic |

Эти причины специально не объединяются в один «network error». Malformed input, abusive rate, stalled join, неподдерживаемая версия клиента и I/O failure требуют разной диагностики.

Frame rejection telemetry отдельно нормализует malformed protocol, rate-limited, invalid-state, gameplay-rejected и backpressure failures, поэтому sink-chain rejection не приходится восстанавливать из произвольного текста логов.

## 7. Handshake и legality состояния соединения

Runtime проверяет состояние соединения отдельно от byte decode. Синтаксически правильный packet может быть нелегален до handshake, до назначения slot или до spawn.

Текущие правила:

1. server-owned identity/slot определяется соединением;
2. client-claimed player identity не считается доверенной там, где ownership уже известен из connection;
3. protocol/state transitions проверяются до gameplay mutation;
4. невозможные pre-handshake и pre-spawn операции отклоняются, не доходя до runtime stores.

Live workflow `Vanilla World Load` является основной end-to-end проверкой official-client-compatible join/bootstrap path. Одни unit tests не доказывают полную последовательность join, потому что обе стороны in-process теста могут разделять одну и ту же ошибочную гипотезу.

## 8. Граница Multiplicity

Multiplicity является protocol dependency, а не gameplay dependency.

`TerraRuntime.Protocol.Multiplicity` переводит Multiplicity wire models в собственные protocol/domain representations TerraRuntime и обратно. Gameplay systems не должны принимать конкретные packet classes Multiplicity только потому, что это удобно.

Так остаются разделены три вида изменений:

- исправление общего packet model, которое принадлежит Multiplicity;
- TerraRuntime-specific connection/state rule;
- gameplay rule, которому не важно, как именно packet был закодирован.

Критические layouts требуют независимого подтверждения: golden bytes, официальный трафик или differential probes. Успешный Multiplicity encode/decode round trip доказывает только то, что encoder и decoder согласны между собой.

## 9. Inbound и fan-out rate accounting

Rate accounting выполняется до дорогой gameplay работы. Policy содержит как connection-wide, так и message-class controls.

Цель не в том, чтобы ломать легитимные burst-сценарии Terraria. Цель в наличии жёсткой верхней границы, после которой один клиент не может превратить packet rate в неограниченный CPU, память или рост очередей.

Некоторые legal input не попадают в authoritative command loop до выполнения shared work. Главный текущий пример — public `Say` chat: один accepted chat frame может fan-out'иться на каждый playing connection. Поэтому `RuntimeChatRelay` применяет **server-global ceiling 256 broadcasts за 1 second** до обхода recipients. Over-budget broadcasts отбрасываются и учитываются как rate-limited rejection, а per-connection allowances не перемножаются на весь сервер.

В roadmap всё ещё остаются более широкие work-budget задачи, включая полные subsystem-level budgets для дорогих операций. Поэтому текущие connection/message/fan-out limits нельзя описывать как завершённую DoS-защиту всех gameplay подсистем.

## 10. Authoritative command queue

Decoded network input попадает в simulation через bounded command path. Game loop применяет global operation ceiling, per-source processing quota и per-source pending/reservation ceiling, чтобы одно соединение не могло монополизировать tick или занять весь общий mailbox, просто submit'я быстрее, чем loop успевает drain.

Инварианты:

- packet order сохраняется там, где это требуется Terraria semantics;
- inbound work ограничен runtime budgets;
- один source не может зарезервировать всю shared command capacity;
- authoritative thread решает, легально ли действие;
- deferred work наблюдаем, а не выполняется без ограничений;
- networking не удерживает game loop в ожидании socket I/O.

## 11. Outbound queues и slow clients

Каждое соединение имеет bounded outbound path. Game loop производит state/events и ставит encoded work в очередь, но не ждёт синхронно, пока peer освободит TCP receive window.

Slow reader поэтому становится локальной проблемой одного connection, а не блокировкой всего сервера. При превышении bounded policy соединение может завершиться причиной `SlowClient`.

Queue sizing ещё требует измерений. Ограниченность очереди является инвариантом; оптимальный bound должен подтверждаться реальным join/section/chest traffic, а не выдумываться на глаз.

## 12. Join и bootstrap traffic

Join является особой burst-фазой. Новому игроку могут потребоваться world metadata, player state, sections и object data до начала обычной movement synchronization.

Текущая реализация содержит live probes join/movement и отдельных chest/bootstrap сценариев на мирах, созданных официальным TerrariaServer 1.4.5.8. Такие workflow защищают ordering/compatibility assumptions, которые сложно доказать unit tests.

Network policy отдельно ограничивает незавершённый join: после valid `Hello` production readiness должна достичь `Playing` в пределах консервативного default двухминутного abuse ceiling, иначе connection завершается с `JoinTimeout`. После достижения `Playing` этот join deadline отключается; обычная idle policy независима от него.

Section-heavy bootstrap path state-gated. Первый valid section request может enqueue bootstrap section sequence; после перехода session в `AwaitingSpawn`/`Playing` повторные section requests не генерируют и не enqueue'ят полный section transfer заново.

Дальнейшая работа по join остаётся staged в roadmap: section generation/compression и initial-state transfer должны выполняться под **глобальным** per-tick budget, а не получать полный expensive-work budget на каждого одновременно подключающегося игрока.

## 13. Interest management

Interest management относится к synchronization layer, а не к packet parser. Network layer может выбирать recipients только после того, как authoritative visibility policy определила, какие клиенты должны наблюдать update.

External hosts получают только узкий `IInterestManagementControl` для включения/отключения механизма. Spatial layout, enter/leave rules, hysteresis и forced resync остаются внутренней policy TerraRuntime.

Пока visibility transitions не доказаны полностью, suppression обязан fail-open: отключение или неопределённое состояние должны возвращать vanilla-like broad recipient selection, а не оставлять объект навсегда невидимым клиенту.

## 14. Threading rules

Network read/write tasks независимы от authoritative simulation owner.

Допустимо вне game thread:

- socket reads/writes;
- framing;
- bounded protocol decode/encode;
- построение immutable packet/frame data;
- connection-local accounting;
- bounded transport-only fan-out с явным server-global work ceiling.

Недопустимо вне game thread:

- напрямую мутировать player/world/NPC/projectile/item stores;
- считать TUI или timer callback владельцем gameplay state;
- сохранять transient receive-buffer references в authoritative state.

## 15. Failure isolation

Malformed или abusive client traffic должен закрывать/отклонять конкретное соединение, а не падать всем server process и не ломать shutdown save path. Shared non-authoritative work, например chat fan-out, вместо этого может drop'нуть только over-budget operation, чтобы один attacker не вынуждал отключать unrelated peers.

Network failure handling должен сохранять различие между:

```text
malformed bytes
rate/work limit
stalled handshake/join
illegal connection state
legal protocol, но rejected gameplay action
I/O failure
slow client
runtime shutdown
```

Это часть observability contract и должно оставаться стабильным по мере расширения structured telemetry.

## 16. Tests и executable evidence

Релевантные доказательства распределены между:

- framing/socket connection tests;
- handshake/join/idle watchdog tests;
- connection policy/rate-accounting tests;
- global chat fan-out budget tests;
- Multiplicity decoder/mapper tests;
- permanent deterministic malformed framing и typed-decoder fuzz tests;
- real-process/slow-client tests;
- `Vanilla World Load` live join/movement probes;
- official-server reference workflows для packet/world behavior.

При изменении нетривиального network rule нужен тест, который падает при удалении исправления. Green test, проходящий и на сломанной реализации, доказательством не является.

## 17. Текущие ограничения

Пока не считаются завершёнными:

- broader protocol/world fuzz corpora за пределами текущего framing и typed-decoder regression floor;
- полные global/per-subsystem expensive-work budgets за пределами уже ограниченных command loop и chat fan-out;
- окончательные queue sizes, полученные измерениями;
- полная packet-count/byte telemetry по message ID;
- полные section-aware suppression/resync semantics;
- широкий corpus replay трафика официального клиента;
- полная vanilla gameplay coverage за каждым допустимым packet type.

Перед тем как считать unchecked target реализованным, сверяйтесь с основной roadmap и performance/tick-stability roadmap.

## 18. Checklist изменения сети

Networking/protocol change не считается завершённым, пока по необходимости не выполнено следующее:

- framing/decoder tests покрывают malformed и valid input;
- connection-state legality протестирована;
- rate/queue/fan-out behavior ограничен;
- NativeAOT paths остаются совместимыми;
- для wire-sensitive изменений есть независимое official client/server evidence;
- эта страница и `docs/en/networking-protocol.md` обновлены в том же изменении.
