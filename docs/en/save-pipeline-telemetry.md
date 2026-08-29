# Save pipeline timing telemetry

[World persistence](world-persistence.md) · [Operations/TUI](operations-tui.md) · [Roadmap](../roadmap.md)

TerraRuntime exposes persistence timing through immutable world-operations snapshots, and the built-in World TUI screen renders the latest three timings in milliseconds. The timings are measured at the ownership boundary rather than by sampling mutable save internals from the TUI or another background observer.

## Measurements

The save coordinator records the most recent duration and cumulative duration for three boundaries:

- **snapshot capture**: time spent synchronously inside the authoritative `captureSnapshot()` callback before the detached save image is handed to the background scheduler;
- **serialization**: time spent inside the serializer callback on the background save worker;
- **write**: elapsed time for the complete `AtomicSaveFileWriter.WriteAsync` transaction.

The write measurement is intentionally an outer transaction measurement. It includes temporary-file setup/cleanup, the serializer callback, durable flush, optional candidate validation, previous-generation backup publication/validation, atomic canonical publication and the supported directory durability barrier.

Because the serializer currently writes directly into the destination temporary stream, serialization contains both format encoding and the stream writes issued by that serializer. It is therefore not a pure CPU-only serialization metric.

Consequently:

\[
T_{write} \ge T_{serialization}
\]

and the two values must **not** be added together when calculating total save latency.

## Ownership and cost

Snapshot timing runs on the authoritative owner because snapshot capture itself runs there. Serialization and write timing run on the existing background save worker. Timing uses the monotonic `Stopwatch` clock and publishes only bounded scalar values through the existing immutable operations snapshot.

No per-tile timer, allocation-heavy tracing or mutable persistence object is exposed to the operations/TUI layer.

## Interpretation

A high snapshot duration points at authoritative handoff/capture work. A high serialization duration points at canonical rewrite/encoding plus serializer-issued stream I/O. A large gap between write and serialization points at durability/validation/backup/publication overhead.

The metrics are diagnostic boundaries, not independent additive phases. If future persistence work separates encoding into an isolated buffer from physical file output, the contract may gain a truly independent file-I/O phase, but that distinction must be implemented rather than inferred from nested timings.
