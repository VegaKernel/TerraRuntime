# TerrariaNewRuntime roadmap

TerrariaNewRuntime is a clean-room C# implementation of the Terraria dedicated server runtime. The project preserves observable vanilla gameplay behavior while replacing fragile legacy internals with explicit, testable, secure and high-performance subsystems.

The governing rule is: **behavioral parity where players can observe it; freedom of implementation everywhere else.**

## Platform baseline

- Target **.NET 11** from the start.
- Do not preserve compatibility with older .NET runtimes unless a concrete deployment requirement appears later.
- Use the current .NET 11 runtime, JIT, GC, `System.IO.Pipelines`, spans, source generators and performance APIs instead of carrying legacy compatibility abstractions.
- Enable nullable reference types, warnings as errors, deterministic builds and analyzers.
- Keep NativeAOT compatibility in mind for standalone tooling, but do not constrain the game server architecture around NativeAOT while dynamic/runtime features are still useful.
- Prefer modern BCL functionality over third-party dependencies where it is sufficient.

## Architectural goals

- Preserve vanilla rules, packet semantics, world behavior, NPC/boss AI outcomes, progression, events, drops, housing, wiring, liquids and `.wld` compatibility.
- Do not reproduce the original Terraria server architecture merely because gameplay behavior came from it.
- Treat every client packet as untrusted input.
- Keep mutable simulation state under a single authoritative owner by default.
- Keep sockets, compression, persistence, logging and expensive I/O away from the game loop.
- Prefer deterministic, bounded work over shared mutable concurrency.
- Make protocol and gameplay behavior independently testable.
- Design hot paths to be allocation-light from the beginning, but require measurements before introducing unsafe or exotic optimizations.

## Target architecture

```text
TCP clients
    |
    v
Connection read loops
    |
    v
Frame decoder / protocol validation
    |
    v
Typed inbound commands
    |
    v
Authoritative game loop (single writer)
    |
    +--> World / Players / NPCs / Projectiles / Items
    |       |
    |       +--> deterministic gameplay systems
    |
    +--> outbound state/events
            |
            v
      per-client sync planner
            |
            v
      encoded packet queues
            |
            v
        socket writers
```

Connection code never mutates the world directly. Background work produces immutable results that are applied through explicit game-loop commands.

## Phase 0 - Repository and .NET 11 baseline

- Keep production code under root `src/`.
- Keep tests under root `tests/`.
- Keep locally decompiled official server output under ignored `decompiled/` only.
- Pin the .NET 11 SDK through `global.json`.
- Record the exact Terraria dedicated-server version and binary SHA-256 used as behavioral reference.
- Maintain reproducible local tooling to download and decompile the official server.
- Add .NET 11 CI for restore, build, test and formatting/analyzers.
- Enable nullable, warnings as errors and deterministic builds.
- Remove dependencies and patterns retained only for old .NET/Mono compatibility.

## Phase 1 - Typed protocol core

Build a standalone Terraria protocol layer before gameplay.

### Framing

- Incremental parser for `[u16 length][u8 message id][payload]`.
- Correctly handle fragmented and coalesced TCP reads.
- Reject impossible lengths, oversized frames and truncated payloads deterministically.
- Never allocate directly from an untrusted client-declared length without a hard ceiling.

### Typed packets

- Replace magic offsets in gameplay code with typed packet records/structs.
- Centralize message IDs in one named catalog.
- Use explicit bounded `TryDecode`/`Decode` contracts.
- Represent optional wire sections with named flags and types rather than scattered bit arithmetic.
- Preserve raw trailing bytes only when protocol semantics genuinely require transparent relay.
- Use source generation for codecs only where it simplifies correctness or produces measurable gains.

### .NET 11 hot path

- `ReadOnlySpan<byte>` / `Span<byte>` codecs.
- `SequenceReader<byte>` where segmented input is useful.
- `IBufferWriter<byte>` for encoding.
- `PipeReader` / `PipeWriter` compatible framing.
- Avoid `MemoryStream`, `BinaryReader`, LINQ and temporary arrays on common packet paths.
- Use `ArrayPool<T>` or `MemoryPool<T>` for large temporary buffers when measurement justifies pooling.

### Security

- Separate byte decoding from gameplay/state validation.
- Per-message size ceilings.
- Per-connection rate accounting.
- Protocol fuzz tests and a permanent malformed-packet corpus.
- Parsing failure returns a bounded error and never crashes the server process.

## Phase 2 - Networking runtime

- Use `System.IO.Pipelines` or an equivalent measured low-allocation .NET 11 socket pipeline.
- One read loop and one write path per connection.
- Bounded outbound queues with an explicit slow-client policy.
- Batch already queued small frames into fewer socket writes without intentionally delaying latency-sensitive traffic.
- Use `TCP_NODELAY` unless measurement demonstrates a better policy.
- Handshake deadline plus normal idle timeout.
- Gate maximum concurrent connections before allocating expensive player state.
- Cancellation must release all connection resources on every exit path.

### DoS hardening

- Global and per-connection budgets for expensive work.
- Bound password/KDF work, compression and section requests.
- Independent rate limits for tile edits, liquids, chat, item operations and other expensive packet classes.
- Distinguish malformed protocol, rate limit, invalid state and gameplay rejection in structured telemetry.

## Phase 3 - Authoritative game loop

Use a single-writer simulation model initially, similar in spirit to the strongest part of terrustia's design.

- One game-loop thread/task owns mutable world, players, NPCs, projectiles, items and progression state.
- Connections submit typed commands/events through bounded channels.
- No locks on the main simulation hot path.
- Preserve packet order per connection.
- Bound inbound work processed per tick so one connection cannot monopolize a frame.
- Fixed 60 Hz simulation schedule with an explicit missed-tick policy.
- Measure both wall time and CPU time.
- Record timing per simulation phase.

Suggested phases:

```text
Inbound commands
Clock/events
Liquids/growth/spread
Tile entities/wiring
Items
NPC AI
Projectiles
Combat/damage
Spawning
Housing
World progression
Visibility/sync planning
Outbound snapshots
```

Do not parallelize gameplay systems until profiling proves it is necessary. Ownership and deterministic ordering are more valuable than theoretical core utilization.

## Phase 4 - World representation

- Preserve vanilla `.wld` compatibility as a hard requirement unless explicitly versioned otherwise.
- Separate persistence representation from in-memory simulation representation.
- Partition the world into sections/chunks for locality, visibility and dirty tracking.
- Track dirty regions so networking and saving do not scan the entire world.
- Avoid allocating objects per tile.
- Benchmark AoS versus SoA/packed layouts before choosing a permanent tile representation.
- Cache derived section metadata only with explicit invalidation rules.

### Save pipeline

- Snapshot required mutable state on the game loop.
- Serialize/compress/write outside the game loop.
- Atomically replace the save only after successful completion.
- Never block simulation on disk I/O except for a short bounded snapshot handoff.
- Regression-test vanilla world round trips and interrupted-save recovery.

## Phase 5 - Gameplay parity

Implement observable behavior subsystem by subsystem using the official server plus independent implementations such as terrustia as behavioral references, never as blindly copied architecture.

Priority:

1. handshake and spawn flow;
2. player state and movement;
3. inventory/items;
4. tile manipulation and sections;
5. chests/signs/tile entities;
6. projectiles and combat;
7. NPC lifecycle and spawning;
8. NPC AI;
9. bosses;
10. drops;
11. housing/town NPC behavior;
12. invasions/events;
13. wiring/liquids;
14. world progression;
15. world generation.

For every subsystem:

- document observable vanilla behavior;
- add deterministic unit tests;
- add integration tests with a real Terraria client/bot where practical;
- maintain explicit known divergences;
- do not mark work complete merely because it compiles or resembles decompiled code.

## Phase 6 - Server-authoritative validation

Close exploit classes inherited from client-trusting designs without turning the server into a false-positive anti-cheat machine.

### Identity/session

- Ignore client-claimed player slot where server ownership is already known.
- Validate packet legality against the connection state machine.
- Reject impossible pre-handshake and pre-spawn operations.

### Movement

- Keep server-known position/velocity history.
- Validate exceptional movement through explicit server-known states such as teleport, mount or respawn.
- Keep tolerances compatible with real network jitter and vanilla behavior.

### Inventory/items

- Server owns world item identity and authoritative item slots.
- Validate pickup distance, ownership, stack bounds and legal transitions.
- Never accept arbitrary client item metadata without validation.

### Tiles/world edits

- Bounds-check coordinates before indexing.
- Validate action type and required world state.
- Use per-player edit budgets based on vanilla-compatible ceilings.
- Guard against overflow and oversized area operations.

### NPC/projectile/combat

- Server owns entity identity and lifecycle.
- Validate targets against live entities.
- Internally use generation/revision handles so stale slot reuse cannot mutate a different entity.

## Phase 7 - Synchronization and scalability

Preserve observable results, not inefficient vanilla broadcast mechanics.

- Section-aware player visibility.
- Dirty-state-driven NPC/projectile/item synchronization.
- Per-client interest sets.
- Avoid O(players²) broadcast when recipients cannot observe an update.
- Encode one immutable frame once and share it among recipient queues when the bytes are identical.
- Explicit delta/full-sync policy with resync deadlines.
- Join pipeline streams sections/entities without unbounded queue growth.
- Benchmark 1, 8, 24, 64, 128 and 255 connections.

Any optimization that changes player-visible behavior requires an explicit compatibility decision.

## Phase 8 - CPU, memory and GC optimization

After correctness baselines exist:

- Profile allocations per tick and packet type.
- Keep common movement/control packet processing allocation-free where practical.
- Replace hot heap objects with structs only when lifetime and copying costs justify it.
- Pool compression and large temporary buffers.
- Cache immutable encoded packets only with explicit invalidation.
- Avoid unnecessary async state machines inside the core tick.
- Use source-generated logging/serialization on hot paths where beneficial.
- Measure GC allocation rate, pause time and heap size on .NET 11.
- Tune GC mode only from production-like benchmarks rather than folklore.
- Consider `GC.TryStartNoGCRegion` only for focused experiments, not as a baseline design.
- `unsafe` requires a benchmark demonstrating material benefit plus focused correctness tests.

## Phase 9 - Observability

- Structured logging from the start.
- Stable event IDs/categories.
- Tick CPU/wall duration and worst phase.
- Queue depth and slow-client drops.
- Packet counts/bytes by message ID and direction.
- Invalid/malformed/rejected packet counters.
- Active players/NPCs/projectiles/items.
- Save snapshot duration and write duration separately.
- GC allocation rate, collections, pause time and heap size.

Telemetry must not add heavy formatting/allocation to every hot-path operation.

## Phase 10 - Test strategy

### Unit tests

- packet codecs;
- flags/bit layouts;
- world math;
- AI state transitions;
- drops and probability rule structure;
- validation/rate limits;
- save/load components.

### Golden-byte tests

Pin critical packet layouts to known-good bytes instead of relying only on encode/decode round trips.

### Differential tests

Drive equivalent scenarios against the official dedicated server and TerrariaNewRuntime and compare observable state/output.

### Real-client integration

Maintain bots/scripts for:

- handshake/join;
- movement/inventory/tile edits;
- chest/sign interaction;
- boss progression;
- events;
- save/restart;
- long-running soak tests.

### Fuzzing

- frame parsing;
- all variable-length packet decoders;
- section/tile decompression;
- `.wld` parsing;
- command/text parsing.

Malformed input must fail safely without process crashes or attacker-controlled large allocations.

## Phase 11 - World generation

Worldgen comes last because it is huge, RNG-order-sensitive and unnecessary for proving the runtime architecture.

- First load existing vanilla worlds correctly.
- Port worldgen pass by pass.
- Treat RNG stream compatibility as explicit behavior.
- Keep statistical generated-world tests plus selected deterministic seeds.
- Do not let worldgen block protocol, runtime or gameplay replacement.

## Performance acceptance direction

Concrete targets will be replaced by measurements on defined hardware.

- Common movement/control packets should avoid avoidable heap allocations.
- Typical ticks must stay comfortably below the 16.67 ms budget with room for spikes.
- Idle CPU should approach sleeping cost rather than burn an entire core.
- Ordinary sync must not scan the entire world.
- No unbounded queue/allocation may be controlled by a client.
- Joining a player must not stall simulation while sections are compressed or written.
- 24-player realistic workload is the first meaningful optimization baseline; 255 connections are a stress/scalability target.

## Non-goals

- Source or binary compatibility with original Terraria server internals.
- Reproducing private class layouts or global-state architecture.
- Keeping Mono or obsolete .NET compatibility baggage.
- Premature ECS conversion because it is fashionable.
- Parallelizing every subsystem.
- Copying decompiled method bodies into the clean implementation.
- Trading vanilla-visible behavior for benchmark numbers without documenting the divergence.

## First milestone

```text
Official Terraria client
        |
        v
TerrariaNewRuntime (.NET 11)
        |
        +-- typed handshake
        +-- player slot assignment
        +-- world metadata
        +-- section request/response
        +-- spawn
        +-- movement relay
        +-- clean disconnect
```

Completion requires a real client to join an existing vanilla world, move, receive nearby world state and disconnect cleanly. The same build must survive malformed frame tests without crashing or allocating unbounded memory.
