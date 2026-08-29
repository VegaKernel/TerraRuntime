# Security, trust boundaries and failure isolation

[Русский](../ru/security.md) · [Documentation](README.md) · [Networking](networking-protocol.md) · [Roadmap](../roadmap.md)

## 1. Security model

TerraRuntime treats every client-controlled byte, length, packet rate, identity claim and gameplay request as untrusted input.

Security is not a single anti-cheat component. It is a set of boundaries that keep one connection or malformed world/network input from acquiring unbounded CPU, memory, queue growth or direct access to authoritative state.

The core rule is:

```text
untrusted input
   -> bounded parse/accounting
   -> connection/session legality
   -> authoritative gameplay validation
   -> mutation only after acceptance
```

## 2. Security is separate from anti-cheat

Protocol hardening and gameplay authority solve different problems.

Protocol/security rules may reject:

- impossible frame lengths;
- invalid handshake;
- unsupported protocol version;
- configured rate abuse;
- connection-capacity exhaustion;
- illegal connection-state ordering;
- malformed variable-length payloads.

Gameplay authority may reject a syntactically valid action because it is impossible in current world/player state.

Do not guess gameplay rules merely to appear more secure. False-positive anti-cheat that rejects legal vanilla behavior is a correctness bug.

## 3. Connection admission

`TerrariaConnectionAdmissionGate` limits expensive connection admission before allocating a complete connection/player workload.

It enforces two independent controls:

- maximum concurrent active connections;
- admission-attempt rate.

The default admission-rate gate allows **512 attempts per 1 second window**.

Every attempt consumes the rate budget **before** capacity admission. This is deliberate: when the server is already full, an attacker must not be able to create an unbounded accept/reject churn loop that bypasses rate accounting simply because capacity rejects immediately.

The gate records accepted, rejected, capacity-rejected and rate-rejected counters.

## 4. Admission leases

A successful admission returns a lease. Releasing the lease decrements active-connection state exactly once.

The lease object is idempotent from the caller's perspective: repeated `Dispose` does not double-release because ownership is exchanged to null.

Internal negative active-count state is treated as an invariant violation rather than silently ignored.

## 5. Handshake and join deadlines

The default connection policy requires protocol handshake completion within **10 seconds**.

A valid `Hello` ends the pre-handshake deadline, but it does not grant an unlimited player-slot lease. Production sink composition exposes a narrow readiness signal to the network watchdog. After `Hello`, a connection that has not reached the runtime's ready/`Playing` state is bounded by a default **2 minute join-completion deadline**.

The two-minute value is a deliberately conservative hard-abuse ceiling, not a Terraria gameplay cadence or a claim about an official vanilla timeout. It exists to prevent a peer from sending a valid `Hello` and then retaining an admitted player slot forever without completing world entry.

Once the connection reaches `Playing`, the join deadline no longer applies. The current default normal post-join idle timeout remains infinite. Deployments may configure a finite idle timeout independently of the join budget.

The watchdog keeps handshake, join and ordinary idle expiration distinct so telemetry can report `HandshakeTimeout`, `JoinTimeout` and `IdleTimeout` separately.

## 6. Connection-wide rate accounting

Every connection has fixed-window rate accounting for configured frame/byte work.

The current default connection policy uses the `HardAbuse` budget rather than an unlimited/accounting-only mode.

Rate limits exist to establish a hard abuse ceiling, not to encode ordinary gameplay timing rules.

Legitimate Terraria traffic can be bursty during join/sections/chests. Limits must therefore be validated against real workloads before being tightened.

## 7. Per-message and fan-out rate accounting

`TerrariaMessageRateAccountant` maintains optional rate accountants indexed by message ID.

Only explicitly configured message IDs receive a message-specific budget. Other messages still pass through connection-wide accounting.

This lets expensive packet classes receive stricter controls without pretending every packet has the same cost.

Public vanilla `Say` chat has an additional **server-global fan-out ceiling of 256 broadcasts per 1 second window**. The budget is checked before the relay performs its O(players) recipient iteration. This prevents many individually legal senders from multiplying a per-connection allowance into unbounded aggregate outbound-queue work. A broadcast rejected by this global ceiling is dropped and recorded in rate-limit rejection telemetry; it does not grant each player a separate copy of the global budget.

The 256/s value is a deliberately loose hard-abuse ceiling, not a normal chat cadence. Legitimate chat should remain far below it while the server still has a deterministic upper bound on chat fan-out work.

Long-term security work still requires broader subsystem-level budgets where one legal packet can trigger significant world/gameplay work.

## 8. Framing and size bounds

Frame lengths and packet-specific declared lengths are hostile until validated.

Rules:

- impossible or truncated frame envelopes are rejected deterministically;
- message-specific size ceilings apply before large allocations;
- no client-declared count directly selects unbounded memory;
- receive-buffer lifetime is not extended into gameplay state;
- parser failure closes/rejects bounded scope instead of crashing the process.

A safe decoder must remain safe even when every length/count field is chosen adversarially.

## 9. Connection-state legality

A packet may be well-formed but illegal at the current connection stage.

Examples include actions before handshake, before player identity/slot assignment or before world entry.

The runtime validates session state before gameplay mutation. Client-claimed player slot/identity is ignored where connection ownership already determines the authoritative identity.

## 10. Typed stop reasons

Network failure is classified rather than flattened.

Current stop reasons include categories such as:

```text
HandshakeTimeout
JoinTimeout
IdleTimeout
InvalidHandshake
UnsupportedProtocol
ProtocolFailure
InboundIoFailure
OutboundFailure
SlowClient
RateLimited
```

This classification is useful for security telemetry because a stalled join, a rate-limited peer, a malformed client and a broken network adapter are not the same incident.

## 11. Bounded queues

Inbound authoritative work and outbound connection work must remain bounded.

A client cannot be allowed to create:

- an unlimited inbound command backlog;
- an unlimited outbound frame backlog;
- unlimited section/compression work;
- unbounded logging/telemetry objects.

When an outbound client cannot drain its bounded queue, it may be disconnected as `SlowClient` rather than blocking simulation.

## 12. Authoritative work budgets

A syntactically legal request can still be computationally expensive.

The authoritative loop therefore uses global/per-source fairness and operation limits. Work that bypasses the authoritative loop but multiplies across recipients, such as public chat fan-out, must have its own server-global subsystem ceiling before the multiplication step.

The project is continuing toward explicit global subsystem budgets for areas such as tile work, liquids, sections and other expensive operations.

Security budgets are **global when work shares one tick or one shared fan-out resource**. Multiplying a full expensive-work budget by player count simply converts a limit into a larger DoS surface.

## 13. Single-writer containment

Network callbacks, timers, TUI and background workers cannot mutate authoritative gameplay state directly.

This is a security property as well as an architecture property: a malformed input path has fewer places where it can create concurrent state corruption.

Validated commands cross one controlled owner boundary before mutation.

## 14. Entity identity safety

Generation/revision-aware handles protect runtime entities from stale slot reuse.

A stale command referencing an old NPC/projectile/player slot must not mutate a different new entity that later occupies the same numeric slot.

Content type IDs and runtime entity handles are separate domains.

## 15. World coordinate safety

World/tile operations validate coordinates before indexing runtime arrays.

Future area operations must also guard:

- integer overflow;
- negative dimensions;
- attacker-controlled large rectangles;
- repeated expensive framing/liquid/placement work.

Bounds checking is required before accessing world memory, not after a failed index operation.

## 16. Chest and object input

Chest/object traffic is a historically exploit-prone area because it mixes identity, coordinates and inventory state.

Current chest handling includes malformed-input containment and validates relevant identifiers/coordinates before authoritative operations.

Strict conservation/anti-dupe rejection must be based on an actual server-owned inventory model. Rejecting legal client behavior from guessed ownership transitions is not acceptable security.

## 17. Persistence safety

World corruption and data loss are security/reliability failures.

Persistence rules include:

- canonical `.wld` remains recovery source;
- runtime snapshot corruption falls back to `.wld`;
- save writes publish through temporary file + flush + atomic replacement;
- unknown/newer world layouts are not rewritten from guessed offsets;
- background writers use detached snapshots, not mutable live stores;
- truncated sections must not silently erase unrelated valid state.

An attacker-caused malformed network exception must not skip the normal final-save/shutdown path for an otherwise healthy world.

## 18. Runtime snapshot integrity

`.runtime-world` is untrusted persisted input at startup even when TerraRuntime created it previously.

The loader verifies its header/layout and integrity-protected embedded canonical data, tile shards and liquid payload.

Any inconsistency is a cache miss. No partially validated snapshot is published.

The derived cache is disposable, so the runtime can reject incompatible versions without implementing risky migrations.

## 19. Host/plugin trust boundary

The CoreCLR extensible host distinguishes trusted host modules from ordinary plugins.

Trusted host modules receive `TerraRuntime.HostContracts`, not arbitrary internal implementation objects. They can register narrow extension providers or issue controlled operations.

Ordinary Vega plugins remain behind Vega's own Plugin SDK and are not automatically granted TerraRuntime trusted-module privileges.

The NativeAOT standalone profile does not arbitrarily load managed plugin DLLs.

## 20. TUI/operations boundary

UI is not trusted as a mutation owner merely because it is local.

Read paths use bounded snapshots. Administrative write paths are marshalled through controlled operations/command surfaces to the authoritative owner.

Terminal/UI failure degrades to plain console rather than corrupting runtime state or terminating the server.

## 21. NativeAOT dependency discipline

Dependency admission is part of the security/reliability model.

Runtime reflection scanning, dynamic code generation or serializers with unclear trimming behavior are not casually admitted to production paths.

New production dependencies must pass the project's Linux/Windows NativeAOT gates and exercised smoke paths where applicable.

This reduces hidden runtime behavior and deployment-only failures in security-sensitive boundary code.

## 22. Failure isolation

The intended failure scope is as small as practical:

```text
bad frame          -> reject/close connection
rate abuse         -> reject/close connection
stalled handshake  -> close that connection
stalled join       -> close that connection
chat fan-out abuse -> drop over-budget broadcast
slow reader        -> close that connection
bad runtime cache  -> fall back to .wld
TUI failure        -> fall back to plain console
save write fail    -> keep previous canonical checkpoint
invalid worldgen   -> discard candidate world
```

The process should not fail-fast for ordinary hostile network input.

Invariant throws remain appropriate for impossible internal programming states, but production trust-boundary failures should use explicit bounded error paths.

## 23. Telemetry

Security-relevant observability should distinguish at least:

- active/accepted/rejected connections;
- capacity admission rejects;
- admission-rate rejects;
- connection/message rate limits;
- server-global fan-out rate rejection;
- connection stop reason mapping, including handshake/join/idle timeouts;
- malformed/protocol failures;
- slow-client disconnects;
- queue depth/backlog age;
- invalid gameplay requests;
- cache validation failure reason;
- save failure counters.

Telemetry itself must be bounded so an attacker cannot turn error reporting into the next memory/CPU attack.

## 24. Fuzzing

Permanent deterministic malformed/fuzz coverage already exists for the framing layer and typed packet decoders, including hostile declared lengths and segmented input. This is a regression floor, not a claim that protocol fuzzing is complete.

The broader roadmap still requires fuzz/malformed corpora for areas such as:

- section/tile data;
- `.wld` parsing;
- command/text parsing;
- future complex variable-length gameplay formats.

A useful fuzz scenario should prove not only that malformed input was rejected, but that the server remains healthy enough to accept a valid connection afterward.

## 25. Security evidence

Depending on the change, evidence includes:

- admission-gate churn/capacity tests;
- handshake/join/idle deadline tests;
- connection policy/rate tests;
- global fan-out budget tests;
- malformed frame/packet tests;
- slow-reader real-socket tests;
- runtime queue/fairness tests;
- world parser/cache corruption tests;
- persistence interruption/atomic-replacement tests;
- live official-client compatibility probes.

For a bug fix, the regression test should fail when the fix is removed.

## 26. Current limitations

The following remain active security work:

- broader protocol/world fuzz corpora beyond the current framing and typed-decoder regression floor;
- complete global/per-subsystem expensive-work budgets beyond the currently bounded command loop and chat fan-out;
- complete rate limits for all expensive gameplay classes;
- richer malformed/rejected packet telemetry;
- full authoritative movement/inventory/combat validation;
- production-like 24-player and 255-connection stress coverage;
- long-running soak/adversarial scenarios.

Security posture should therefore be described by concrete implemented bounds, not by a blanket "secure" label.

## 27. Change checklist

A trust-boundary/security change is incomplete unless, where relevant, attacker-controlled sizes are bounded before allocation, expensive work is rate/budget controlled, failure scope is contained, connection/gameplay rejection categories remain distinguishable, authoritative state is not mutated off-thread, persistence recovery remains safe, regression evidence fails on the old behavior, and this page plus `docs/ru/security.md` are updated together.
