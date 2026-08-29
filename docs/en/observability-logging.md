# Observability and logging

[Русский](../ru/observability-logging.md) · [Documentation](README.md) · [Operations/TUI](operations-tui.md) · [Logging roadmap](../roadmap/runtime-logging-pipeline.md)

## 1. Current status

TerraRuntime already exposes bounded operations telemetry and a bounded recent-log buffer, but the full runtime-owned asynchronous structured logging pipeline described by the roadmap is **not complete yet**.

Keep the distinction explicit:

```text
bounded operations snapshots      implemented for multiple runtime domains
bounded recent-log read model     implemented
TUI log consumption               implemented foundation
fully structured async log queue  incomplete
background drain + JSONL sinks    incomplete
stable public runtime log API     incomplete
```

The current implementation is useful for local operations, but it must not be documented as the final logging architecture.

## 2. Observability boundary

Observability must not acquire ownership of simulation state.

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

The TUI and future exporters consume detached data. They do not traverse mutable runtime stores directly.

## 3. Current recent-log buffer

`RuntimeLogBuffer` is a bounded operations read model, not the final public logging API.

Current hard limits are:

```text
default entries       512
maximum entries       8192
maximum source length 64 characters
maximum message length 2048 characters
```

When the ring buffer is full, new events overwrite the oldest retained entries and increment an overwrite counter. Logging history therefore remains bounded even if an operator never opens the log view.

## 4. Current log entry shape

The current operations record contains:

```text
Sequence
TimestampUtc
Level
Source
Message
```

Current levels are:

```text
Debug
Information
Warning
Error
```

Snapshots also expose total published entries, overwritten entries, the applied minimum level and capture time.

This shape is intentionally smaller than the future structured event model proposed by the logging roadmap.

## 5. Source/message normalization

`RuntimeLogBuffer.Publish` normalizes retained strings before storing them.

- source is bounded to 64 characters;
- message is bounded to 2048 characters;
- control characters are replaced with spaces;
- an empty source falls back to `Runtime`;
- retained history cannot grow by attaching arbitrary object graphs or packet payloads.

This is a retention safety rule, not a substitute for a structured event schema.

## 6. Snapshot reads and filtering

Operations consumers can request a bounded log snapshot by:

- minimum level;
- optional exact source;
- maximum entry count.

The buffer returns the newest matching entries while preserving chronological order in the returned snapshot.

Consumers can also request a bounded sorted source list for filtering UI.

## 7. Chat is not logging

The reserved read-only source `Chat` projects the separate bounded public-chat telemetry into the operator log view.

That projection is intentional: chat routing itself is not converted into generic logging ownership merely because operators may want to inspect recent chat.

Chat entries are projected as `Information` records for the UI, while their source telemetry remains separate.

## 8. Current host log behavior

`RuntimeHostLog` currently bridges runtime messages to the recent-log buffer and local console output.

Its behavior depends on local UI state:

- every published/written message enters the bounded runtime log buffer;
- when the TUI is active, ordinary console writes are suppressed to avoid corrupting the terminal dashboard;
- after a TUI session has existed and falls back to plain console, published messages can also be written to standard output;
- explicit error writes may use standard error when the TUI is not active.

This host bridge is still synchronous at the call site. The future structured logging pipeline must move sink formatting/I/O off hot runtime paths.

## 9. Why the future pipeline is separate

The logging roadmap requires a runtime-owned producer queue because a slow disk, console, host sink or external exporter must never turn diagnostics into backpressure on gameplay/network hot paths.

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

That target is not yet equivalent to the current `RuntimeHostLog` + `RuntimeLogBuffer` implementation.

## 10. Telemetry is not the same as logs

TerraRuntime uses counters/snapshots for high-frequency operational facts that should not become one log line per occurrence.

Examples include network/runtime counters such as:

- active/registered/accepted/rejected connections;
- queued outbound frames/bytes and rejected outbound frames;
- inbound frames/bytes and rate rejects;
- admission capacity/rate rejection counts;
- connection stop-reason counters;
- malformed/rate/invalid-state/gameplay/backpressure rejection categories;
- relayed/baseline/rejected NPC, projectile and world-item frames.

These belong in bounded telemetry snapshots rather than high-volume textual logging.

## 11. Connection rejection telemetry

`RuntimeNetworkSnapshot` currently keeps rejection classes distinguishable, including counters for:

```text
malformed protocol
rate limited
invalid state
gameplay rejection
backpressure
```

It also tracks selected terminal connection-stop categories such as protocol failure, invalid handshake, unsupported protocol, slow client, handshake/idle timeout and application stop.

This distinction must be preserved as structured logging evolves. Flattening everything into one generic "connection failed" line would throw away useful diagnostic information.

## 12. Runtime-domain telemetry

Operations already has domain-specific telemetry/read models for multiple runtime areas, including players, NPCs, projectiles, world items, networking, world state, world clock and save/persistence state.

The rule is to aggregate high-frequency state near its owner and expose bounded snapshots. The UI should not calculate expensive runtime statistics by scanning live entity collections.

## 13. TUI consumption

The Terminal UI refreshes operations snapshots on its own UI thread, currently at roughly 500 ms intervals.

The log view consumes `ILogOperations`/`RuntimeLogBuffer` snapshots. It does not own the producer path and must not block runtime publishers.

Future follow/tail behavior should remain sequence-based and bounded so a slow UI consumer can report a gap rather than force unbounded retention.

## 14. Planned structured event model

The logging roadmap proposes an immutable machine-readable runtime record with fields such as:

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

This is **target architecture**, not the current public record shape. Documentation and APIs must distinguish the two until implementation plus tests prove the new pipeline.

## 15. Queue/backpressure target

The future logging queue must be bounded and non-blocking for authoritative/network hot paths.

Expected policy direction:

- Debug/Trace are first candidates for dropping under pressure;
- Information may be sampled/coalesced/dropped when saturated;
- Warning/Error should receive preferential retention/capacity;
- Critical events need a bounded emergency fallback, not an unbounded synchronous path.

Exact queue sizes/policies remain implementation work and require measurement.

## 16. Sink failure isolation target

A future sink failure must not stop simulation or disable every other diagnostics sink.

The roadmap calls for:

- one long-lived drain worker initially;
- batching where useful;
- independent sink exception isolation;
- bounded health/failure telemetry;
- graceful shutdown drain/flush;
- separate bounded buffering for future network exporters.

None of those future guarantees should be inferred solely from the existing recent-log ring buffer.

## 17. File logging status

The roadmap's durable target is newline-delimited structured JSON (`.jsonl`) written from the background logging worker with rotation/retention and explicit flush behavior.

That complete durable structured file-sink pipeline is not yet documented as implemented.

Do not write synchronous JSON/file output from gameplay or network hot paths just to fill this gap quickly.

## 18. Host/Vega boundary

TerraRuntime owns runtime/network/gameplay/world diagnostics. Vega owns Vega/application/plugin policy logs.

A future Vega integration may consume immutable TerraRuntime log records through a sink/adapter, but TerraRuntime must not reference Vega assemblies or hand Vega mutable runtime objects.

Likewise, arbitrary external `ILogger` providers must not execute synchronously on the authoritative game-loop thread.

## 19. Correlation and context

Useful future correlation includes connection/session, player/entity handle, join/bootstrap, save and world-load/worldgen operations.

Context should be explicit and bounded. Do not attach mutable runtime objects or arbitrary large dictionaries merely to enrich a log record.

## 20. Performance rule

High-frequency runtime observability should prefer counters, compact typed fields and aggregated snapshots over formatted strings.

A logging/telemetry change that claims to be cheap must be measured under the same workload before and after if it affects a hot path.

No log sink, file flush, terminal rendering or exporter may become required progress for the authoritative simulation tick.

## 21. Current evidence

Existing tests include coverage for the recent log buffer, host-log behavior, chat telemetry projection and operations snapshots. Network/security telemetry also has focused mapping tests.

The future async structured pipeline will require additional evidence for queue saturation, drop policy, sink failure isolation, shutdown drain and NativeAOT behavior before roadmap items can be marked complete.

## 22. Current limitations

Current observability/logging limitations include:

- the recent log model is textual and small rather than fully structured;
- stable event IDs/categories are not yet the universal runtime logging contract;
- no completed bounded async logging producer/drain pipeline;
- no completed structured JSONL rotation/retention sink;
- no completed Vega/MEL adapter contract;
- high-frequency telemetry coverage is still expanding by subsystem.

## 23. Change checklist

An observability/logging change is incomplete unless, where relevant:

- hot-path work remains bounded and non-blocking;
- counters are preferred over per-event text for high-frequency facts;
- retained strings/payloads are bounded;
- UI/exporters consume snapshots/immutable records, not mutable stores;
- sink failure cannot become gameplay failure;
- implemented versus target logging architecture is stated accurately;
- this page and `docs/ru/observability-logging.md` are updated together.
