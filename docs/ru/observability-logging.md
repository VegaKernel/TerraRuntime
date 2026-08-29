# Observability и structured runtime logging

[English](../en/observability-logging.md) · [Документация](README.md) · [Operations/TUI](operations-tui.md) · [Logging roadmap](../roadmap/runtime-logging-pipeline.md)

В TerraRuntime есть **L0-L2 structured logging foundation**, а live-host путь L3 уже в основном внедрён. Startup, загрузка/cache/recovery мира, persistence, lifecycle listener/connection, lifecycle trusted host module, shutdown failures и TUI failures идут в bounded structured pipeline с semantic event IDs. Обычный lifetime `TerrariaServerHost.RunAsync` явно dispose'ит и drain'ит logger на любом return path. TUI operations path теперь использует тот же `RuntimeRecentLogStore`, что и structured logging, а production composition получила bounded runtime configuration для очереди и first-party sinks.

## Архитектура

```mermaid
graph LR
    P[Runtime producer] -->|semantic event + detached context + TryPublish| Q[Bounded MPSC channel]
    Q --> W[Single background drain worker]
    W --> C[Compatibility stdout/stderr delivery]
    W --> J[Rotating JSONL sink]
    W --> O[RuntimeLogBuffer operations facade]
    O --> R[RuntimeRecentLogStore]
    R --> T[TUI Logs view]
```

Semantic identity и локальная console delivery намеренно разделены. `RuntimeLogRecord.EventId` описывает **что произошло**. Host-local delivery hint идёт рядом с record во внутреннем pipeline envelope и сообщает только delivery-aware sink, нужно ли принятое событие оставить buffered, отправить в stdout или stderr. Обычные structured sinks получают только `RuntimeLogRecord`, поэтому console routing не может случайно превратиться в semantic event identity.

Delivery hint фиксируется до enqueue, поэтому последующее изменение состояния TUI не может задним числом перенаправить уже принятое событие.

## Ограничение producer path

Producer path нормализует bounded scalar text/context, назначает sequence/timestamp и вызывает `ChannelWriter.TryWrite`. Disk I/O, console I/O, JSON encoding, flush, rotation, retention и mutation recent-log выполняются вне authoritative producer path.

При capacity очереди \(N_q\) и reserve для warning/error \(N_r\) normal records могут занять не больше

\[
N_{normal}=N_q-N_r.
\]

Defaults остаются \(N_q=2048\) и \(N_r=256\). `Warning`, `Error` и `Critical` могут использовать reserve. При saturation producer не ждёт место, а отклоняет record и увеличивает per-level drop counter.

## Stable record contract

`TerraRuntime.Contracts.Diagnostics.RuntimeLogRecord` содержит sequence, UTC timestamp, severity, stable event ID, category, subsystem, bounded message text, detached correlation context и bounded exception type/message. Context состоит только из scalar identifiers: logging не удерживает mutable runtime entities и raw packet payloads.

### Event ID allocation

| Range | Category |
| ---: | --- |
| `1000-1999` | Lifecycle |
| `2000-2999` | Network |
| `3000-3999` | Protocol |
| `4000-4999` | World |
| `5000-5999` | Persistence |
| `6000-6999` | Plugin |
| `7000-7999` | Gameplay |
| `8000-8999` | Operations |
| `9000-9999` | Security |

Legacy bridge по-прежнему резервирует `8000-8002` для старых callers `RuntimeHostLog.Write`/`Publish`. Новые live-host call sites эти ID больше не используют. Для lifecycle, network, world, persistence, plugin/host integration и operations событий назначены финальные category-specific IDs. `8003` является semantic event для отказа Terminal UI.

Protocol, gameplay и security IDs выделяются тогда, когда реально мигрируют соответствующие semantic call-site families; runtime не выдумывает события только ради заполнения диапазонов.

## Live-host context

`RuntimeHostLog` добавляет run-scoped correlation identifier к semantic events, если caller не передал более узкую correlation. После загрузки мира также добавляется стабильный world ID. Connection lifecycle events используют один connection-scoped correlation ID и `ConnectionId`; если при ошибке disconnect уже известен authoritative player handle, он добавляется как `PlayerHandle`.

Entity и packet context остаются полями того же detached contract, но заполняются только call sites, которые действительно владеют проверенными entity/packet identifiers. Raw packet payload и mutable runtime object в logging не удерживаются.

## RuntimeHostLog adoption

Следующие families в `TerrariaServerHost` structured и больше не вызывают напрямую `Console.WriteLine`, `Console.Error.WriteLine` или `RuntimeLogBuffer.Publish`:

- cleanup abandoned-save и подготовка save template;
- stat/read/load world source, runtime cache hit/miss/rebuild, checkpoint recovery и bootstrap cache preparation;
- startup profile и listener-ready/failure events;
- failures attach/detach trusted host module;
- connection accept/stop/failure и shutdown faults;
- TUI startup/runtime failures;
- дедлайны authoritative command drain и остановки game loop;
- success/failure финального canonical world save и failures invalidation runtime cache.

`Console.CancelKeyPress` остаётся control/lifecycle signal, а не logging call, поэтому намеренно не меняется. `TerminalUiHost` продолжает владеть интерактивным console rendering/input в plain-console mode; пользовательский интерфейс не является runtime log sink.

`TerrariaServerHost.RunAsync` владеет `RuntimeHostLog` через `await using`. Поэтому early startup failure, listener failure, normal shutdown и успешный return проходят один и тот же bounded drain/disposal pipeline. Process-exit handler остаётся только fallback для аварийной потери ownership.

## Одно хранилище recent logs

`RuntimeLogBuffer` остаётся существующим `ILogOperations` facade для TUI, но теперь одновременно является `IRuntimeLogSink`, работающим поверх `RuntimeRecentLogStore`. Второй независимой реализации ring больше нет. Structured records и legacy direct operations publications используют одно bounded retained store и один overwrite counter вместо двух параллельных authoritative recent-log состояний.

Default retained capacity остаётся \(512\) records с hard maximum \(8192\). Facade назначает локальные monotonic sequence для read model, отображает structured levels в существующие operations levels и сохраняет exact-source filtering и bounded source enumeration. Underlying structured records сохраняют event/category/context даже при том, что текущий `ILogOperations` projection намеренно остаётся компактным.

## Runtime logging configuration

Production composition `RuntimeHostLog` читает bounded process-level настройки из environment variables. Invalid и out-of-range values откатываются к safe defaults; priority reserve нормализуется так, чтобы

\[
1 \le N_r < N_q.
\]

| Переменная | Default | Назначение |
| --- | --- | --- |
| `TERRARUNTIME_LOG_LEVEL` | `Debug` | minimum accepted structured level |
| `TERRARUNTIME_LOG_QUEUE_CAPACITY` | `2048` | total bounded queue capacity |
| `TERRARUNTIME_LOG_PRIORITY_RESERVE` | `256` | capacity protected from normal-level traffic |
| `TERRARUNTIME_LOG_CONSOLE` | `true` | compatibility stdout/stderr sink |
| `TERRARUNTIME_LOG_JSONL` | `true` | rotating JSONL sink |
| `TERRARUNTIME_LOG_DIRECTORY` | `<app>/logs` | JSONL output directory |
| `TERRARUNTIME_LOG_MAX_FILE_BYTES` | `16777216` | rotation threshold, \(16\,\mathrm{MiB}\) |
| `TERRARUNTIME_LOG_RETAINED_FILES` | `8` | maximum retained JSONL files |
| `TERRARUNTIME_LOG_FLUSH_RECORDS` | `64` | periodic JSONL flush interval |
| `TERRARUNTIME_LOG_SINK_TIMEOUT_MS` | `2000` | per-sink asynchronous deadline |
| `TERRARUNTIME_LOG_SHUTDOWN_TIMEOUT_MS` | `5000` | bounded pipeline drain window |

Internal test composition отключает JSONL и сохраняет injected pipeline queue/timeout values, поэтому unit tests не создают operator files.

## Backpressure и sink health

`RuntimeLogPipeline` публикует accepted/filtered counts, per-severity drops, drained count, sink failures, queue depth и high-water mark. Sink failures изолированы; repeatedly failing sink quarantine'ится, а healthy sinks продолжают работу.

Blocked compatibility console writer не может блокировать producer. Delivery-aware console routing всё равно выполняется только drain worker'ом.

## Built-in sinks

`RuntimeConsoleLogSink` даёт structured human-readable console output. Production host composition использует delivery-aware compatibility console sink вместе с `RuntimeJsonLinesLogSink` и operations sink `RuntimeLogBuffer`/`RuntimeRecentLogStore`. `RuntimeJsonLinesLogSink` пишет NativeAOT-safe JSONL через explicit `Utf8JsonWriter`, с size/day rotation и bounded retention.

Default JSONL policy:

- maximum file size \(16\,\mathrm{MiB}\);
- rotation по UTC day boundary;
- retention \(8\) files;
- periodic flush каждые \(64\) records;
- immediate flush для `Error` и `Critical`.

## Sensitive-data rules

Нельзя писать passwords, authentication tokens, secrets, raw packet bodies, private keys или arbitrary object dumps в message/context. Предпочтительны opaque handles, а не personal/mutable runtime data. Free-form fields bounded и control characters normalized, но sanitization не делает secret material безопасным для logging.

## NativeAOT constraints

Pipeline использует BCL channels, explicit contracts и manual JSON writing. Host-local delivery envelope и environment configuration не добавляют reflection-driven runtime type discovery, dynamic serializer generation или runtime code generation.

## Оставшаяся L3 adoption

Оставшаяся работа L3 теперь ограничена конкретным legacy call-site cleanup: выделять semantic event IDs и detached entity/packet context при фактической миграции protocol/gameplay/security families, закончить оставшиеся `RuntimeHostLog.Write`/`Publish` callers вне уже мигрированных families `TerrariaServerHost` и убрать bridge IDs `8000-8002`, когда от них больше никто не зависит.

Подробный milestone state находится в [`../roadmap/runtime-logging-pipeline.md`](../roadmap/runtime-logging-pipeline.md).
