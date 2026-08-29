# Runtime-owned structured logging pipeline roadmap

This document defines the logging architecture that belongs to TerraRuntime itself rather than to Vega or another host.

The runtime must be independently observable without Vega, and logging I/O must never become part of the authoritative simulation critical path. Vega may consume TerraRuntime log events, render them in its TUI/API and combine them with application/plugin logs, but TerraRuntime remains the source of runtime/network/gameplay diagnostics.

The governing rule is:

> **Producing a runtime log event must be bounded and non-blocking on simulation/network hot paths; formatting and I/O happen outside the authoritative loop.**

---


> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

## 1. Migration decision from Vega

Take the useful concepts from Vega's operations logging layer, but do not mechanically move its current implementation.

The currently visible `Vega.Operations.Logging.StructuredOperationsLogger` on `general-devel` constructs an immutable record and invokes every sink synchronously on the caller thread. Its JSON file sink serializes and writes directly and uses `AutoFlush = true`. Those are useful semantics and a good source of invariants, but they are not the target TerraRuntime hot-path architecture.

TerraRuntime should instead own:

- immutable structured runtime log records;
- stable levels, categories and event IDs;
- a bounded producer queue;
- a dedicated background drain worker;
- sink fan-out and sink failure isolation;
- bounded recent-log retention;
- drop/backpressure telemetry;
- shutdown drain/flush semantics;
- optional adapters for Vega and `Microsoft.Extensions.Logging`.

Vega should retain Vega/application/plugin-specific logs and consume TerraRuntime runtime logs through an adapter/sink.

---

## 2. Ownership boundary

### TerraRuntime owns

- runtime lifecycle logs;
- networking, protocol and connection-state logs;
- gameplay/runtime validation logs;
- world load/save/cache/worldgen logs;
- NPC/projectile/item/world simulation diagnostics;
- runtime queueing and prioritization;
- runtime log sequence numbers and timestamps;
- structured runtime context fields;
- the background drain worker;
- standalone console/file/recent-buffer sinks;
- queue and sink health metrics;
- lifecycle-aware final flush.

### Vega owns

- accounts, permissions, moderation and application-policy logs;
- module/plugin lifecycle logs;
- plugin-specific arbitrary events and properties;
- operator policy for application-level retention/export;
- UI/API presentation and filtering;
- integration with external logging infrastructure selected by a Vega deployment.

### Shared integration

Vega may install a TerraRuntime sink/observer that forwards immutable runtime records into Vega's operations layer. This does not transfer ownership of runtime logging back to Vega.

TerraRuntime must never reference Vega assemblies.

---

## 3. Proposed project/boundary shape

Prefer a small dedicated diagnostics layer rather than putting file writers inside `TerraRuntime.Core`.

Conceptual structure:

```text
TerraRuntime.Contracts/Diagnostics
    RuntimeLogLevel
    RuntimeLogEventId
    RuntimeLogRecord
    IRuntimeLogSink / IRuntimeLogObserver

TerraRuntime.Core
    hot-path producers
    runtime context enrichment

TerraRuntime.Diagnostics or host diagnostics implementation
    bounded queue
    drain worker
    console sink
    JSONL file sink
    bounded recent-log store
    optional MEL/Vega adapter
```

Exact project names may change. The dependency rule is normative: core simulation code must not depend on the filesystem, console UI frameworks, Vega or external exporters.

---

## 4. Structured event model

A runtime log record should contain machine-readable context rather than only a preformatted text line.

Candidate fields:

```text
Sequence
TimestampUtc
Level
EventId
Category
Source/Subsystem
MessageTemplate or stable message key
RenderedMessage?
Exception
CorrelationId
WorldId
ConnectionId
PlayerHandle
NpcHandle
ProjectileHandle
ItemHandle
PacketDirection
PacketId/Type
Properties
```

Rules:

- records are immutable after publication;
- context fields are optional and populated only when meaningful;
- no record retains mutable world/entity objects;
- exceptions are captured safely without forcing expensive formatting on every normal event;
- large payloads, packet bodies, tile arrays and arbitrary object graphs are never attached to records;
- endpoint/IP exposure follows an explicit diagnostics/privacy policy rather than being copied to every event;
- do not require `Dictionary<string, object>` allocation for every event;
- hot events should prefer typed fields and compact predefined properties.

Stable event IDs should be grouped by subsystem rather than allocated ad hoc across the repository.

---

## 5. Producer path

The common producer path must be cheap enough to call from the authoritative loop and network runtime.

Target shape:

```text
runtime code
   -> cheap level/category gate
   -> create compact immutable record
   -> TryWrite to bounded queue
   -> return immediately
```

Requirements:

- no disk write;
- no console lock;
- no waiting for a sink;
- no unbounded allocation;
- no synchronous JSON serialization;
- no blocking `ChannelWriter.WriteAsync` from the game loop;
- avoid interpolation/formatting when an event is filtered out;
- prefer source-generated/static logging methods or equivalent precompiled templates for frequent events;
- rare startup/shutdown paths may accept richer allocation when outside the tick hot path.

Use a BCL primitive such as a bounded `Channel<T>` initially unless measurement demonstrates a better specialized queue.

---

## 6. Queue architecture and backpressure

The queue is bounded because a dead disk or slow sink must not turn diagnostics into an attacker-controlled memory leak.

Recommended baseline:

```text
producers (MPSC)
      |
      v
bounded runtime log queue
      |
      v
dedicated drain worker
      |
      +--> console sink
      +--> JSONL/file sink
      +--> bounded recent-log store
      +--> optional Vega/MEL/export sink
```

### Overflow policy

Not every level needs identical guarantees.

A practical policy should reserve capacity or priority for important events:

- `Trace`/`Debug`: drop first under pressure;
- `Information`: may be sampled, coalesced or dropped when saturated;
- `Warning`/`Error`: preserve preferentially through reserved capacity or a small high-priority lane;
- `Critical`: attempt normal queueing, then use a bounded emergency fallback if the logging worker is unavailable.

The exact implementation may use one queue with reserved accounting or two bounded lanes. Measure before adding a complicated scheduler.

The runtime must expose:

- current queue depth/capacity;
- high-water mark;
- dropped count by level/category;
- oldest queued event age where practical;
- drain rate;
- sink failure counts;
- last sink failure information.

Never block the authoritative game loop waiting for log queue space.

---

## 7. Drain worker

The logging worker is background infrastructure and never owns simulation state.

Requirements:

- one dedicated long-lived drain loop is sufficient initially;
- batch queued events where that reduces file/console overhead;
- fan out to sinks outside the game loop;
- catch sink exceptions independently so one broken sink does not stop the others;
- repeated sink failure may disable/quarantine that sink with health telemetry;
- shutdown is explicit and lifecycle-aware;
- no unbounded `Task.Run` fan-out per record or per sink;
- external network exporters, if later added, require their own bounded buffering and cannot stall the primary drain loop indefinitely.

A sink must have documented blocking behavior. Network exporters should normally be decoupled behind another bounded queue.

---

## 8. File sink

The standalone runtime should support a simple durable structured file format without introducing a database into the server process.

Recommended baseline: newline-delimited JSON (`.jsonl`).

Requirements:

- file writes occur only from the background logging worker;
- configurable directory and filename prefix;
- rotation by size and/or UTC day;
- bounded retained file count/total bytes when retention is enabled;
- safe directory creation;
- append/recovery semantics that tolerate a truncated final line after a crash;
- batched writes where appropriate;
- explicit flush policy rather than `AutoFlush` after every normal record;
- explicit final flush on graceful shutdown;
- file failure never crashes the game loop.

Human-readable plain-text output may remain a console presentation concern. Structured JSONL is the durable baseline.

---

## 9. Recent-log store and TUI/API consumption

The existing Operations/TUI roadmap needs a bounded recent-log feed. It should consume the same immutable runtime records rather than maintain a second logging path.

Requirements:

- bounded ring-buffer retention by record count and optionally approximate bytes;
- lock-free or low-contention reads where practical, but correctness first;
- filtering by level/category/source/event ID and selected entity/connection context;
- sequence-based incremental reads for follow/tail views;
- explicit gap indication when a slow UI consumer has fallen behind retention;
- UI never blocks the drain worker;
- no TUI object is referenced from logging core.

Vega may expose the same model through REST/debug APIs without duplicating logging ownership.

---

## 10. `Microsoft.Extensions.Logging` interoperability

TerraRuntime should be friendly to the .NET ecosystem without forcing `ILogger` internals into every hot path.

Recommended direction:

- provide an adapter from TerraRuntime runtime records to `Microsoft.Extensions.Logging` for hosts that want it;
- optionally provide an `ILoggerProvider`/sink bridge if NativeAOT behavior remains clean;
- do not let arbitrary external `ILogger` providers execute synchronously on the authoritative game-loop thread;
- if MEL is accepted as the public producer abstraction for some non-hot subsystems, route it into the same bounded TerraRuntime pipeline;
- frequent gameplay/network events should still have allocation-aware/source-generated producer paths.

The runtime owns the queue and backpressure semantics even when a host consumes records through MEL.

---

## 11. Correlation and scopes

Useful runtime context should flow without manually rebuilding the same fields in every log call.

Potential correlation scopes:

- connection/session;
- player handle/generation;
- join/bootstrap operation;
- save operation;
- world load/generation operation;
- command/request correlation when the operations layer submits an authoritative command.

Do not implement scopes as a mutable ambient bag that leaks across unrelated async work. Prefer explicit immutable context or carefully bounded `AsyncLocal` usage only outside the authoritative hot path.

The game loop should normally have enough explicit entity/command identity to enrich an event without ambient state.

---

## 12. Logging versus telemetry

Logs and metrics are complementary.

Do not emit one log record for every high-frequency state change just to reconstruct metrics later.

Examples:

- packet count/bytes -> counters, not per-packet information logs;
- tick duration -> metric/histogram, with a warning log only for threshold events;
- queue depth -> gauge, with a warning when sustained pressure crosses policy thresholds;
- malformed packet -> counter plus sampled/limited diagnostic log;
- extension failure -> counter plus error log with provider identity.

Rate-limit repetitive diagnostics so a malicious client cannot fill the log queue or disk with one malformed request class.

---

## 13. Security and sensitive data

Logging is part of the trust boundary.

Requirements:

- sanitize control characters in human-readable output;
- never log passwords, authentication secrets, tokens or raw credential payloads;
- do not log full arbitrary packet payloads by default;
- bound string/property lengths from untrusted input;
- distinguish client-provided text from trusted runtime fields;
- endpoint/IP logging is configurable where privacy policy requires it;
- exception rendering must not recursively serialize arbitrary state graphs.

Security events should use stable event IDs and categories so Vega/operations tooling can consume them without parsing message text.

---

## 14. Shutdown and crash behavior

Graceful shutdown sequence should be explicit:

```text
stop accepting new runtime work
    -> stop normal log producers at lifecycle boundary
    -> complete logging queue
    -> drain queued records
    -> flush sinks
    -> dispose sinks
```

Use a bounded shutdown policy. A permanently blocked sink must not hang process shutdown forever.

For process-fatal failures where the normal worker cannot drain, a minimal emergency path may write one bounded critical message to `Console.Error`/OS stderr. It must not attempt complex serialization or acquire game-loop locks.

---

## 15. Testing requirements

### Functional

- events preserve sequence/order within the published queue semantics;
- filtered-out events do not reach sinks;
- structured fields survive sink fan-out;
- recent-log retention reports sequence gaps correctly;
- rotation/retention works;
- shutdown drains expected queued records;
- sink exception does not stop other sinks;
- Vega adapter receives immutable records without owning runtime state.

### Backpressure

- bounded queue never grows beyond capacity;
- low-priority events are dropped according to policy;
- warning/error reserve behavior is deterministic;
- drop counters are correct;
- stalled file/external sink cannot block the authoritative game loop.

### Security

- oversized untrusted fields are bounded;
- credentials are never rendered by dedicated security paths;
- control characters cannot corrupt plain console framing unexpectedly.

### NativeAOT

- Linux and Windows native publish remains warning-free;
- exercised native smoke path writes console/file events and shuts down cleanly;
- JSON serialization uses an explicit AOT-safe source-generated context or another verified AOT-safe path.

### Performance

Benchmark:

- disabled/filtered log call;
- enabled hot-path log enqueue;
- queue saturation/drop path;
- batch drain throughput;
- JSONL serialization throughput;
- recent-buffer read while producers are active.

The producer benchmark matters most. A slow file sink is survivable if isolated; a slow producer path taxes every tick.

---

## 16. Delivery order

### L0 - Contracts and event taxonomy

- [ ] runtime log level/event ID/category types;
- [ ] compact immutable record;
- [ ] subsystem event-ID allocation policy;
- [ ] sensitive-data rules.

### L1 - Bounded queue and worker

- [ ] non-blocking producer path;
- [ ] bounded channel;
- [ ] background drain loop;
- [ ] drop/backpressure metrics;
- [ ] lifecycle/shutdown integration.

### L2 - Core sinks

- [ ] console sink;
- [ ] JSONL rotating file sink;
- [ ] bounded recent-log store;
- [ ] sink health/failure isolation.

### L3 - Runtime adoption

Replace ad hoc console/log output subsystem by subsystem:

- [ ] startup/lifecycle;
- [ ] networking/protocol;
- [ ] world load/save/cache;
- [ ] player/session validation;
- [ ] NPC/projectile/gameplay;
- [ ] worldgen and extension diagnostics.

Do not convert hot paths into chatty per-tick logs while migrating.

### L4 - Vega integration

- [ ] Vega runtime-log sink/adapter;
- [ ] TUI recent-log consumption;
- [ ] REST/debug projection where appropriate;
- [ ] remove duplicate runtime-origin logging from Vega once TerraRuntime is the authoritative source.

### L5 - Enforcement and performance gate

- [ ] architecture test preventing core dependency on concrete sinks/Vega;
- [ ] saturation stress test;
- [ ] NativeAOT smoke;
- [ ] producer-path benchmark and documented budget.

---

## Definition of done

This roadmap slice is complete when:

- [ ] TerraRuntime is independently observable without Vega;
- [ ] no normal runtime log sink performs file/console/network I/O on the authoritative game-loop thread;
- [ ] the producer path is bounded and non-blocking;
- [ ] queue overflow has explicit deterministic policy and telemetry;
- [ ] file/console/recent sinks are isolated from one another;
- [ ] graceful shutdown drains/flushed logs under a bounded policy;
- [ ] Vega consumes runtime logs instead of owning TerraRuntime diagnostics;
- [ ] frequent runtime events use stable structured IDs/categories rather than text parsing;
- [ ] Linux and Windows NativeAOT smoke paths exercise the logging pipeline successfully.
