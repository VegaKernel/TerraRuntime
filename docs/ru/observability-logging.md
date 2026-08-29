# Observability и logging

[English](../en/observability-logging.md) · [Документация](README.md) · [Operations/TUI](operations-tui.md) · [Logging roadmap](../roadmap/runtime-logging-pipeline.md)

## 1. Текущий статус

TerraRuntime уже предоставляет bounded operations telemetry и bounded recent-log buffer, но полный runtime-owned asynchronous structured logging pipeline из roadmap **ещё не завершён**.

Различие нужно сохранять явно:

```text
bounded operations snapshots      реализованы для нескольких runtime domains
bounded recent-log read model     реализован
TUI log consumption               реализованный foundation
fully structured async log queue  incomplete
background drain + JSONL sinks    incomplete
stable public runtime log API     incomplete
```

Текущая реализация полезна для local operations, но её нельзя описывать как финальную logging architecture.

## 2. Observability boundary

Observability не получает ownership simulation state.

```text
authoritative runtime
       |
       +--> bounded counters/snapshots
       +--> bounded log/read-model events
       |
       v
operations layer
       |
       +--> TUI
       +--> plain/local diagnostics
       +--> future host/API/export adapters
```

TUI и future exporters потребляют detached data. Они не обходят mutable runtime stores напрямую.

## 3. Текущий recent-log buffer

`RuntimeLogBuffer` — bounded operations read model, а не финальный public logging API.

Текущие hard limits:

```text
default entries       512
maximum entries       8192
maximum source length 64 characters
maximum message length 2048 characters
```

Когда ring buffer заполнен, новые events перезаписывают самые старые retained entries и увеличивают overwrite counter. Поэтому log history остаётся bounded, даже если operator никогда не открывает log view.

## 4. Текущая форма log entry

Current operations record содержит:

```text
Sequence
TimestampUtc
Level
Source
Message
```

Current levels:

```text
Debug
Information
Warning
Error
```

Snapshot также содержит total published entries, overwritten entries, applied minimum level и capture time.

Эта форма намеренно меньше future structured event model из logging roadmap.

## 5. Нормализация source/message

`RuntimeLogBuffer.Publish` нормализует retained strings до сохранения.

- source bounded до 64 characters;
- message bounded до 2048 characters;
- control characters заменяются пробелами;
- empty source fallback'ится в `Runtime`;
- retained history не растёт от arbitrary object graphs или packet payloads.

Это retention safety rule, а не замена structured event schema.

## 6. Snapshot reads и filtering

Operations consumers могут запросить bounded log snapshot по:

- minimum level;
- optional exact source;
- maximum entry count.

Buffer возвращает newest matching entries, сохраняя chronological order в snapshot.

Также доступен bounded sorted source list для UI filtering.

## 7. Chat не является logging

Reserved read-only source `Chat` проецирует отдельную bounded public-chat telemetry в operator log view.

Это намеренно: chat routing не превращается в generic logging ownership только потому, что operators хотят видеть recent chat.

Chat entries проецируются как `Information` records для UI, а их исходная telemetry остаётся отдельной.

## 8. Текущее поведение host log

`RuntimeHostLog` сейчас связывает runtime messages с recent-log buffer и local console output.

Поведение зависит от local UI state:

- каждое published/written message попадает в bounded runtime log buffer;
- при active TUI обычный console output подавляется, чтобы не ломать dashboard;
- после существовавшей TUI session и перехода в plain console published messages могут также идти в standard output;
- explicit error writes могут использовать standard error, если TUI не active.

Этот host bridge пока synchronous на call site. Future structured logging pipeline должен вынести sink formatting/I/O с hot runtime paths.

## 9. Почему future pipeline отдельный

Logging roadmap требует runtime-owned producer queue, потому что slow disk, console, host sink или external exporter не должны превращать diagnostics в backpressure gameplay/network hot paths.

Target direction:

```text
runtime producer
   -> cheap level/category gate
   -> compact immutable record
   -> non-blocking bounded enqueue
   -> return

background drain worker
   -> console/file/recent-buffer/host sinks
```

Этот target пока не эквивалентен current `RuntimeHostLog` + `RuntimeLogBuffer` implementation.

## 10. Telemetry не равно logs

TerraRuntime использует counters/snapshots для high-frequency operational facts, которые не должны становиться одной log line на каждое occurrence.

Примеры network/runtime counters:

- active/registered/accepted/rejected connections;
- queued outbound frames/bytes и rejected outbound frames;
- inbound frames/bytes и rate rejects;
- admission capacity/rate rejection counts;
- connection stop-reason counters;
- malformed/rate/invalid-state/gameplay/backpressure rejection categories;
- relayed/baseline/rejected NPC, projectile и world-item frames.

Это должно жить в bounded telemetry snapshots, а не в high-volume textual logging.

## 11. Connection rejection telemetry

`RuntimeNetworkSnapshot` сейчас сохраняет rejection classes раздельно, включая counters для:

```text
malformed protocol
rate limited
invalid state
gameplay rejection
backpressure
```

Также отслеживаются selected terminal connection-stop categories: protocol failure, invalid handshake, unsupported protocol, slow client, handshake/idle timeout и application stop.

Это различие надо сохранить по мере развития structured logging. Свести всё в generic `connection failed` означало бы выбросить полезную diagnostic information.

## 12. Runtime-domain telemetry

Operations уже имеет domain-specific telemetry/read models для нескольких runtime areas, включая players, NPCs, projectiles, world items, networking, world state, world clock и save/persistence state.

Правило: high-frequency state агрегируется рядом с owner и отдаётся bounded snapshots. UI не должен вычислять expensive runtime statistics сканированием live entity collections.

## 13. TUI consumption

Terminal UI обновляет operations snapshots на собственном UI thread примерно каждые 500 ms.

Log view читает `ILogOperations`/`RuntimeLogBuffer` snapshots. Он не владеет producer path и не должен блокировать runtime publishers.

Future follow/tail behavior должен оставаться sequence-based и bounded, чтобы slow UI consumer мог сообщить gap вместо требования unbounded retention.

## 14. Planned structured event model

Logging roadmap предлагает immutable machine-readable runtime record с полями вроде:

```text
Sequence
TimestampUtc
Level
EventId
Category
Subsystem
Message template/key
Exception
CorrelationId
World/connection/player/entity context
Packet direction/id
bounded properties
```

Это **target architecture**, а не current public record shape. Документация и APIs должны различать их, пока implementation + tests не докажут новый pipeline.

## 15. Queue/backpressure target

Future logging queue должна быть bounded и non-blocking для authoritative/network hot paths.

Expected policy direction:

- Debug/Trace первыми drop'ятся under pressure;
- Information может sample/coalesce/drop при saturation;
- Warning/Error получает preferential retention/capacity;
- Critical требует bounded emergency fallback, а не unbounded synchronous path.

Exact queue sizes/policies остаются implementation work и требуют measurement.

## 16. Sink failure isolation target

Future sink failure не должен останавливать simulation или остальные diagnostics sinks.

Roadmap требует:

- один long-lived drain worker на старте;
- batching там, где полезно;
- independent sink exception isolation;
- bounded health/failure telemetry;
- graceful shutdown drain/flush;
- separate bounded buffering для future network exporters.

Эти future guarantees нельзя выводить только из существующего recent-log ring buffer.

## 17. File logging status

Durable target roadmap — newline-delimited structured JSON (`.jsonl`) из background logging worker с rotation/retention и explicit flush policy.

Полный durable structured file-sink pipeline пока не считается implemented.

Нельзя закрывать этот gap synchronous JSON/file writes из gameplay/network hot paths.

## 18. Host/Vega boundary

TerraRuntime владеет runtime/network/gameplay/world diagnostics. Vega владеет Vega/application/plugin policy logs.

Future Vega integration может потреблять immutable TerraRuntime log records через sink/adapter, но TerraRuntime не должен ссылаться на Vega assemblies или отдавать Vega mutable runtime objects.

Также arbitrary external `ILogger` providers не должны выполняться synchronously на authoritative game-loop thread.

## 19. Correlation и context

Полезная future correlation включает connection/session, player/entity handle, join/bootstrap, save и world-load/worldgen operations.

Context должен быть explicit и bounded. Нельзя прикладывать mutable runtime objects или arbitrary large dictionaries только ради enrichment log record.

## 20. Performance rule

High-frequency runtime observability должна предпочитать counters, compact typed fields и aggregated snapshots вместо formatted strings.

Logging/telemetry change с заявлением о low overhead требует before/after measurement на том же workload, если затрагивает hot path.

Ни один log sink, file flush, terminal rendering или exporter не должен быть required progress authoritative simulation tick.

## 21. Текущий evidence

Existing tests покрывают recent log buffer, host-log behavior, chat telemetry projection и operations snapshots. Network/security telemetry имеет focused mapping tests.

Future async structured pipeline потребует дополнительного evidence для queue saturation, drop policy, sink failure isolation, shutdown drain и NativeAOT behavior до закрытия roadmap items.

## 22. Текущие ограничения

Current observability/logging limitations:

- recent log model textual и небольшой, а не fully structured;
- stable event IDs/categories ещё не являются universal runtime logging contract;
- completed bounded async producer/drain pipeline отсутствует;
- completed structured JSONL rotation/retention sink отсутствует;
- Vega/MEL adapter contract не завершён;
- high-frequency telemetry coverage ещё расширяется по subsystems.

## 23. Checklist изменения observability/logging

Observability/logging change не завершён, пока по необходимости:

- hot-path work bounded и non-blocking;
- counters предпочитаются per-event text для high-frequency facts;
- retained strings/payloads bounded;
- UI/exporters читают snapshots/immutable records, не mutable stores;
- sink failure не становится gameplay failure;
- implemented и target logging architecture описаны без смешения;
- эта страница и `docs/en/observability-logging.md` обновлены вместе.
