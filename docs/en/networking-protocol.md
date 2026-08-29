# Networking and protocol

[Русский](../ru/networking-protocol.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Roadmap](../roadmap.md)

## 1. Scope

This guide describes the networking and Terraria protocol path that exists in TerraRuntime today. It documents implemented boundaries and current safety rules, not the full target described by the roadmap.

The protocol baseline is Terraria **1.4.5.8**, protocol **326**. Multiplicity 2.7.x is the typed packet implementation baseline behind the TerraRuntime protocol boundary. The official 1.4.5.8 dedicated server and independent real-client captures remain the final behavioral/wire references when an implementation and a round-trip test disagree.

## 2. Layer map

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
   |  Multiplicity adapters / typed wire models
   v
owned semantic input
   |
   v
authoritative game-loop command boundary
   |
   v
gameplay/state validation and mutation
```

The reverse path is:

```text
authoritative state/event
   -> runtime packet projection
   -> protocol encode
   -> bounded per-connection outbound queue
   -> one connection writer
   -> TCP
```

A packet decoder does not own gameplay policy. A socket callback does not own gameplay state.

## 3. Framing

Terraria frames use the runtime's verified envelope:

```text
[u16 total frame length][u8 message id][payload...]
```

The network layer incrementally handles:

- a frame split across multiple socket reads;
- multiple frames coalesced into one read;
- invalid/impossible lengths;
- oversized messages subject to explicit ceilings;
- truncated input at connection termination.

Client-declared sizes are untrusted. They must never directly select an unbounded allocation size.

The framing layer answers only whether a byte sequence can be safely separated into protocol frames. Message-specific decoding and gameplay legality are separate stages.

## 4. Receive-buffer ownership

Socket input is temporary borrowed data. Gameplay must never retain a reference into a receive buffer after the read pipeline advances.

The ownership transition is:

```text
borrowed socket bytes
   -> validated frame
   -> decoded/owned values
   -> typed command or owned frame data
   -> authoritative queue
```

This rule is especially important for `Span<T>`, `ReadOnlySpan<T>`, `ReadOnlySequence<T>` and pooled buffers: low allocation cost must not become a lifetime bug.

## 5. Connection policy

`TerrariaConnectionPolicyState` tracks timeout and terminal-stop state independently from gameplay state.

The current default policy uses:

- **10 seconds** for protocol handshake completion;
- **2 minutes** as a conservative hard-abuse ceiling for completing join after `Hello` until the runtime reports ready/`Playing`;
- no normal post-join idle timeout (`Timeout.InfiniteTimeSpan`);
- the `HardAbuse` connection rate budget;
- the `HardAbuse` per-message rate-limit set.

The two-minute join deadline is not a vanilla gameplay timing rule. It prevents a peer from completing the cheap protocol `Hello` step and then retaining an admitted player slot indefinitely without entering the world.

A successful handshake records the handshake-completion timestamp and wakes the watchdog immediately. Production sink composition exposes only a narrow `ITerrariaConnectionReadinessSource` signal to the network layer; it does not leak gameplay objects into network policy. While the connection is not ready the watchdog applies the join deadline. Once readiness becomes true it stops applying the join deadline and follows the configured ordinary idle policy instead.

Timeout state is monotonic: once a terminal stop reason is recorded, later activity cannot replace it with a different reason.

## 6. Stop reasons and rejection categories

`TerrariaConnectionStopReason` currently distinguishes:

| Reason | Meaning |
|---|---|
| `PeerClosed` | remote endpoint closed normally |
| `ApplicationStopped` | runtime shutdown requested |
| `Cancelled` | connection execution was cancelled |
| `HandshakeTimeout` | required protocol handshake was not completed before the deadline |
| `JoinTimeout` | `Hello` completed but the connection did not reach ready/`Playing` before the join deadline |
| `IdleTimeout` | configured post-join inactivity deadline expired |
| `InvalidHandshake` | handshake bytes/state were structurally invalid |
| `UnsupportedProtocol` | client protocol/version is not accepted |
| `ProtocolFailure` | protocol processing failed after framing |
| `InboundIoFailure` | socket/read-side I/O failed |
| `OutboundFailure` | encode/write-side processing failed |
| `SlowClient` | bounded outbound policy closed a client that could not drain data |
| `RateLimited` | configured connection/message budget rejected traffic |

These categories are intentionally different. Operators and tests should not flatten them into one generic "network error" because malformed input, abusive rate, a stalled join, unsupported client version and an I/O failure require different diagnosis.

Frame rejection telemetry separately normalizes malformed protocol, rate-limited, invalid-state, gameplay-rejected and backpressure failures so a sink-chain rejection does not need to be inferred from arbitrary text logs.

## 7. Handshake and connection-state legality

The runtime validates connection state separately from byte decoding. A packet that is syntactically valid can still be illegal before handshake, before slot assignment or before spawn.

Current design rules:

1. the connection establishes the server-owned identity/slot;
2. client-claimed player identity is not trusted where the connection already determines ownership;
3. protocol/state transitions are checked before gameplay mutation;
4. invalid pre-handshake or pre-spawn operations are rejected rather than reaching runtime stores.

The live `Vanilla World Load` workflow is the primary end-to-end guard for the official-client-compatible join/bootstrap path. Unit tests cannot prove the full join sequence because both sides of an in-process test can accidentally share the same wrong assumption.

## 8. Multiplicity boundary

Multiplicity is a protocol dependency, not a gameplay dependency.

`TerraRuntime.Protocol.Multiplicity` is responsible for translating between Multiplicity wire models and TerraRuntime-owned protocol/domain representations. Gameplay systems should not accept Multiplicity packet classes merely because they are convenient.

This keeps three things separable:

- a fix that belongs in the shared Multiplicity packet model;
- a TerraRuntime-specific connection/state rule;
- a gameplay rule that should not know how the packet was encoded.

Critical layouts require independent evidence such as golden bytes, official traffic or differential probes. A successful Multiplicity encode/decode round trip only proves that the encoder and decoder agree with each other.

## 9. Inbound and fan-out rate accounting

Rate accounting occurs before expensive gameplay work. The policy has both connection-wide and message-class controls.

The objective is not to punish legitimate bursty Terraria traffic. The objective is to establish a hard upper boundary beyond which one client cannot convert packet rate into unbounded CPU, memory or queue growth.

Some legal input does not enter the authoritative command loop before doing shared work. Public `Say` chat is the current important example: one accepted chat frame can fan out to every playing connection. `RuntimeChatRelay` therefore applies a **server-global 256 broadcasts per 1 second** hard-abuse ceiling before iterating recipients. Over-budget broadcasts are dropped and counted as rate-limited rejection rather than multiplying per-connection allowances across the whole server.

The roadmap still contains broader work-budget tasks, including complete subsystem-level budgets for expensive operations. Therefore current connection/message/fan-out limiting must not be described as complete DoS protection for every gameplay subsystem.

## 10. Authoritative command queue

Decoded network input crosses into simulation through a bounded command path. The game loop applies a global operation ceiling, a per-source processing quota and a per-source pending/reservation ceiling so one connection cannot monopolize a tick or occupy the entire shared mailbox simply by submitting faster than the loop drains.

Important invariants:

- packet order is preserved where Terraria semantics require it;
- inbound work is bounded by runtime budgets;
- one source cannot reserve the complete shared command capacity;
- the authoritative thread decides whether the action is legal;
- deferred work is observable rather than silently executed without limit;
- networking does not hold the game loop waiting for socket I/O.

## 11. Outbound queues and slow clients

Every connection has a bounded outbound path. The game loop produces state/events and queues encoded work; it does not synchronously wait for the peer's TCP receive window.

A slow reader therefore becomes a local connection problem instead of a server-wide stall. When the configured bounded policy is exceeded, the connection can terminate with `SlowClient`.

Queue sizing is still an active measurement task. A queue being bounded is an invariant; the ideal bound is workload-dependent and must be justified by real join/section/chest traffic rather than folklore.

## 12. Join and bootstrap traffic

Join is a special high-burst phase. A newly connected player may need world metadata, player state, sections and object data before normal movement synchronization begins.

The current implementation has live probes for join/movement and selected chest/bootstrap behavior against worlds generated by the official TerrariaServer 1.4.5.8. These workflows protect ordering and compatibility assumptions that are difficult to validate from unit tests alone.

The network policy separately bounds incomplete joins: after a valid `Hello`, production readiness must reach `Playing` within the conservative default two-minute abuse ceiling or the connection stops with `JoinTimeout`. Reaching `Playing` disables this join deadline; normal idle policy is independent.

The section-heavy bootstrap path is state-gated. The first valid section request can enqueue the bootstrap section sequence; after the session has advanced to `AwaitingSpawn`/`Playing`, repeated section requests do not regenerate and re-enqueue the full section transfer.

Long-term join work remains staged in the roadmap: section generation/compression and initial-state transfer must stay under a **global** per-tick budget rather than granting a complete expensive-work budget to every joining player.

## 13. Interest management

Interest management belongs to the synchronization layer, not the packet parser. The network layer can route only after authoritative visibility policy has decided which clients should observe an update.

External hosts receive only the narrow `IInterestManagementControl` on/off control. Spatial layout, enter/leave rules, hysteresis and forced resync are internal TerraRuntime policy.

Until visibility transitions are fully proven, suppression must fail open: disabling or an uncertain state should restore vanilla-like broad recipient selection rather than accidentally hide state forever.

## 14. Threading rules

Network read/write tasks are independent of the authoritative simulation owner.

Allowed off-thread work includes:

- socket reads and writes;
- framing;
- bounded protocol decode/encode;
- immutable packet/frame construction;
- connection-local accounting;
- bounded transport-only fan-out that has an explicit server-global work ceiling.

Not allowed off-thread:

- mutating player/world/NPC/projectile/item stores directly;
- treating a TUI or timer callback as a gameplay owner;
- keeping transient receive-buffer references in authoritative state.

## 15. Failure isolation

Malformed or abusive client traffic should close or reject that connection without crashing the server process or skipping world-save shutdown behavior. Shared non-authoritative work such as chat fan-out may instead drop only the over-budget operation so one attacker does not force unrelated peers to disconnect.

Network failure handling should preserve the distinction between:

```text
malformed bytes
rate/work limit
stalled handshake/join
illegal connection state
legal protocol but rejected gameplay action
I/O failure
slow client
runtime shutdown
```

This distinction is part of the observability contract and should remain stable as structured telemetry expands.

## 16. Tests and executable evidence

Relevant evidence lives across:

- framing and socket connection tests;
- handshake/join/idle watchdog tests;
- connection policy and rate-accounting tests;
- global chat fan-out budget tests;
- Multiplicity decoder/mapper tests;
- permanent deterministic malformed framing and typed-decoder fuzz tests;
- real-process/slow-client tests;
- `Vanilla World Load` live join/movement probes;
- official-server reference workflows for packet/world behavior.

When changing a non-trivial network rule, add a test that fails when the fix is removed. Green tests that also pass on the broken implementation are not evidence.

## 17. Current limitations

The following areas are intentionally not presented as finished:

- broader protocol/world fuzz corpora beyond the current framing and typed-decoder regression floor;
- complete global/per-subsystem expensive-work budgets beyond the command loop and chat fan-out already bounded;
- measurement-derived final queue sizing;
- complete packet-count/byte telemetry by message ID;
- full section-aware suppression/resync semantics;
- broad real-client replay corpus;
- full vanilla gameplay coverage behind every valid packet type.

See the main roadmap and performance/tick-stability roadmap before treating an unchecked target as implemented behavior.

## 18. Change checklist

A networking/protocol change is not complete until, where relevant:

- framing/decoder tests cover malformed and valid input;
- connection-state legality is tested;
- rate/queue/fan-out behavior is bounded;
- NativeAOT paths remain compatible;
- independent official-client/server evidence exists for wire-sensitive changes;
- this page and `docs/ru/networking-protocol.md` are updated in the same change.
