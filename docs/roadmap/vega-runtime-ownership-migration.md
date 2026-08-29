# Vega -> TerraRuntime ownership migration

This document defines which low-level Terraria/runtime responsibilities currently represented in `VegaKernel/Vega` should become native TerraRuntime capabilities, which responsibilities remain in Vega, and which boundaries must be split into a runtime primitive plus a Vega policy layer.

The governing rule is:

> **TerraRuntime owns everything required for a correct, safe, observable Terraria server to exist without Vega. Vega owns application policy, administration, modules/plugins and operator-facing composition.**

The goal is not to copy Vega code into TerraRuntime. Vega is a source of tested ideas, invariants, security lessons and useful contract shapes. TerraRuntime must reimplement those ideas around its own authoritative loop, NativeAOT constraints, world ownership model and Multiplicity-backed protocol boundary.

This migration must preserve the dependency direction:

```text
Vega
    |
    | semantic control / policy / administration
    v
TerraRuntime public contracts
    |
    v
TerraRuntime authoritative runtime
    |
    v
Multiplicity packet models / views / codecs
```

Vega must never become a second owner of Terraria simulation, replication, spatial state or packet-wire semantics.

---


> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

## 1. Ownership matrix

| Capability currently represented in Vega | Target owner | Migration decision |
| --- | --- | --- |
| Packet/player ownership enforcement | `TerraRuntime.Core/Networking` | **Move the invariant.** A client must never authoritatively mutate another connection's slot. |
| Player/world sanity checks | `TerraRuntime.Core/Players` + `Core/Worlds` | **Move.** Finite coordinates, world bounds, legal ranges and runtime-safe state are server invariants. |
| Projectile ownership/sanity | `TerraRuntime.Core/Projectiles` | **Move.** Projectile owner, identity, type/range and finite-state validation belong to the authoritative entity runtime. |
| Item validation | `TerraRuntime.Core/Items` | **Move.** Item identity, stack/type/prefix bounds and legal transitions are runtime invariants. |
| Chest validation | `TerraRuntime.Core/Worlds/Chests` | **Move.** Chest IDs, slot bounds and world-coordinate safety belong below Vega policy. |
| Tile/wall/liquid/tile-entity validation | corresponding world runtime systems | **Move.** Validate before indexing or mutation. |
| Packet flood/rate limits | `TerraRuntime.Core/Networking` | **Move the mechanism, remeasure the numbers.** Do not inherit Vega thresholds as protocol truth. |
| Game-thread dispatcher | `TerraRuntime.Core/Threading` / authoritative loop | **Take the concept, replace the implementation.** TerraRuntime owns the loop and does not need hook-based dispatch. |
| Player/world/NPC/chest/inventory snapshots | `TerraRuntime.Contracts` | **Move/adapt the model.** Keep snapshots immutable and demand-driven. |
| Revision-guarded mutations | `TerraRuntime.Core` entity/world systems | **Required.** Optimistic mutation guards become a runtime-wide primitive. |
| Runtime/network/security telemetry | `TerraRuntime.Contracts` + diagnostics implementation | **Move metric ownership.** Vega may consume/project it but must not be the source. |
| Derived world image/cache | save/startup subsystem | **Take ideas, not the Vega format.** `.wld` remains canonical. |
| Scene visibility mechanics | replication/visibility API | **Split.** Generic hard visibility belongs near replication; Vega scene policy remains an external consumer. |
| World clock | world simulation | **Reimplement only after vanilla verification.** Never copy a corrective clock over vanilla semantics by assumption. |
| Connection/player slot ownership | `TerraRuntime.Core/Networking` + `Players` | **Make fundamental.** Connection source, slot, session generation and lifecycle must be runtime-owned. |
| Connection state machine | `TerraRuntime.Core/Networking` | **Move.** Handshake/join/bootstrap/playing/disconnect legality is runtime protocol semantics. |
| Typed packet processing context | `TerraRuntime.Protocol` / `Core/Networking` | **Adopt the shape, not a second codec.** Multiplicity remains the wire owner. |
| Entity lifecycle IDs/generations | `TerraRuntime.Core/Entities` | **Add.** Required for safe slot reuse and stale-command rejection. |
| Replication/recipient routing | `TerraRuntime.Core/Replication` | **Add.** Move fan-out decisions out of transport registries. |
| AOI / Interest Management | `TerraRuntime.Core/Replication/Interest` | **Runtime-owned.** Vega may only enable/disable or select a supported mode. |
| Backpressure / bounded outbound queues | `TerraRuntime.Core/Networking` | **Required runtime primitive.** A slow client must never backpressure simulation globally. |
| World identity/context | `TerraRuntime.Core/Worlds` | **Add/normalize.** No implicit process-global world assumptions in public contracts. |
| Dirty/revision tracking | `TerraRuntime.Core/Worlds` | **Required.** Foundation for section cache, snapshots, saves and replication. |
| Tick/work budgets | `TerraRuntime.Core` | **Runtime-owned.** Vega may observe/configure documented control surfaces, not implement scheduling. |
| Runtime readiness | host/runtime lifecycle | **Split.** TerraRuntime owns world/network readiness; Vega owns module/application readiness. |

---

## 2. What must remain in Vega

The following are not TerraRuntime responsibilities merely because they consume Terraria state:

- accounts and authentication policy;
- groups, roles and permissions;
- bans, mutes and moderation policy;
- regions and build authorization policy;
- commands and command authorization;
- localization;
- chat moderation/transformation policy;
- module/plugin lifecycle and hot reload;
- database/application persistence;
- REST/API surfaces;
- Terminal UI and other operator UIs;
- update checking;
- GeoIP policy;
- administration workflows;
- cluster messaging and cross-server application coordination;
- community/gameplay rules that are not vanilla runtime invariants.

TerraRuntime supplies safe primitive operations and immutable observations. Vega decides whether an operator, module or player is allowed to request those operations.

Example:

```text
client tile request
      |
      v
TerraRuntime protocol + bounds validation
      |
      v
BeforeTileMutation policy point
      |
      +---- Vega RegionPolicy -> reject
      |
      v
TerraRuntime authoritative mutation
      |
      v
revision / dirty section / replication
```

TerraRuntime must not know about permissions such as `world.edit.region.foo`.

---

## 3. Do not copy the old dependency shape

Vega's current implementations are behavioral/design references, not the target dependency graph.

Migration rules:

1. Identify the invariant or capability.
2. Verify whether it is vanilla/runtime behavior, security hardening or Vega policy.
3. Verify protocol semantics against Multiplicity and official Terraria 1.4.5.8 behavior.
4. Define the smallest TerraRuntime-owned contract needed by a real consumer.
5. Implement it through the authoritative runtime boundary.
6. Add executable regressions.
7. Only then replace/remove the equivalent Vega-side implementation.

Do not mechanically move namespaces or classes.

Do not introduce a second general packet codec into TerraRuntime. Multiplicity remains the packet model/view/encode/decode source of truth; TerraRuntime owns framing, runtime metadata, semantic validation, authoritative state and recipient selection.

---

## 4. Connection and player identity

Connection identity must be stronger than a Terraria slot number.

A slot can be reused. Therefore runtime state should distinguish:

```text
ConnectionId
PlayerSlot
PlayerSessionGeneration
```

A stale operation from the previous occupant of slot `12` must never mutate the new occupant of slot `12`.

Target concepts:

```text
PlayerHandle
    Slot
    Generation

ConnectionHandle
    SourceId
    Slot
    Generation
```

Requirements:

- authoritative connection source is established by the runtime;
- client-claimed player IDs are treated as untrusted protocol fields;
- packet handlers rewrite/validate identity against the owning connection;
- disconnect invalidates the current generation;
- reconnect/slot reuse creates a new generation;
- queued work captures a generation where stale application would be dangerous;
- telemetry identifies both connection source and authoritative player slot.

Current foundation: `PlayerSlotPool` assigns a non-zero, monotonically advancing per-slot session
generation and exposes `PlayerHandle` (`slot + generation`) through each lease and join session.
Player appearance, equipment, spawn, movement and disconnect commands capture a `ConnectionHandle`
(`source + player handle`); the authoritative state rejects stale generations even if both source and
slot are reused. Deferred movement-resync operations also capture both player generations before
enqueue. Extending the same identity to later player-owned entity commands remains required.

---

## 5. Typed packet processing context

TerraRuntime should adopt the useful idea of a typed packet-processing context, but keep Multiplicity as the only packet-wire authority.

Conceptually:

```text
PacketProcessingContext<T>
    Direction
    ConnectionSourceId
    AuthoritativePlayerSlot?
    ConnectionState
    ReceivedAt
    TypedPacket/View
```

The context exists so validation/security/runtime stages do not repeatedly rediscover transport metadata or parse raw bytes.

It must not become a generic plugin packet interception API by default.

Recommended processing boundary:

```text
frame
  -> Multiplicity view/model
  -> protocol structural validation
  -> connection/state legality
  -> authoritative identity normalization
  -> runtime semantic validation
  -> typed command
  -> authoritative apply
  -> replication planning
```

---

## 6. Runtime validation layers

Validation should be split by ownership rather than collected into one giant packet-policy switch.

### Protocol structural validation

Owned by Multiplicity + TerraRuntime protocol boundary:

- complete payload consumption;
- legal encoded ranges where defined by wire format;
- bounded strings/arrays;
- valid optional-field structure;
- frame size limits.

### Connection/state validation

Owned by networking runtime:

- packet legal in current connection state;
- handshake/spawn ordering;
- source/slot ownership;
- repeated or impossible lifecycle transitions;
- rate/flood budgets.

### Gameplay/runtime sanity

Owned by the subsystem that owns the state:

- finite positions/velocities;
- world bounds;
- entity identity/generation;
- item/slot/stack ranges;
- chest bounds;
- projectile ownership;
- tile/liquid/object coordinates;
- legal mutation preconditions.

Current inventory boundary: player item slot IDs are validated before authoritative enqueue and
again before snapshot/relay. TerrariaServer 1.4.5.8 packet 5 accepts slots `0..989`, but only
relays `0..98` and `700..989` (389 slots); private bank/trash ranges `99..699` never enter the
replication cache or peer queues. These exact `PlayerItemSlotID.CanRelay` boundaries have focused
ingress and exhaustive relay tests.

Packet-5 item state is also normalized before the authoritative queue: legacy net IDs `-1..-48`
map through the vanilla 1.4.5.8 `Item.netDefaults` table, IDs outside `ItemID.Count` become air,
non-positive stacks produce canonical air, and relay flags retain only the persistent favorite bit.
The registry repeats normalization defensively for callers that bypass the network ingress.

Packet 4 follows the same rule: player names are trimmed and constrained to the vanilla 20-character
limit before enqueue, while skin, voice, pitch, hair, visibility and progression bit fields are
normalized to the exact 1.4.5.8 ranges. Invalid empty/oversized names cannot enter the authoritative
queue, and defensive registry normalization prevents non-network callers from bypassing the rule.

Packet 13 now has a shared semantic guard at ingress, authoritative apply and replication: positions
and every flag-present optional vector must be finite, selected inventory indices stay in `0..58`,
mount IDs stay below the 1.4.5.8 `MountID.Count` of 66, and optional-field presence is derived from
the wire flags. Absent optional values are canonicalized to zero before storage or encoding.

Packet 12 is validated before spawn submission and again on authoritative apply: spawn coordinates
permit only the vanilla `-1` sentinel or non-negative tiles, timers/death counts cannot be negative,
teams stay in `PlayerTeamID` `0..5`, and spawn contexts stay in the four-value 1.4.5.8 enum. Invalid
data closes bootstrap with a distinct reason and cannot advance the join session to playing.

### Vega policy

Runs only after runtime-safe meaning exists:

- permissions;
- regions;
- moderation;
- custom gameplay restrictions;
- plugin policy.

A Vega rejection is not the same category as malformed protocol or invalid runtime state. Telemetry must keep them separate.

---

## 7. Generation and revision as separate concepts

TerraRuntime should make this distinction explicit everywhere reusable slots/entities exist.

```text
Generation = is this still the same logical object?
Revision   = is this still the same version of that object?
```

Example:

```text
NPC slot 12, generation 5, revision 103
```

If the NPC changes:

```text
slot 12, generation 5, revision 104
```

If it despawns and a different NPC later reuses slot 12:

```text
slot 12, generation 6, revision 1
```

A stale handle from generation 5 is invalid even if its revision happens to match numerically.

Apply this where useful to:

- players/sessions;
- NPCs;
- projectiles;
- world items;
- tile entities;
- chests where replacement semantics require it;
- snapshots and asynchronous mutation requests.

---

## 8. Revision-guarded mutation model

The useful Vega snapshot/mutation pattern should become a TerraRuntime primitive.

Conceptually:

```text
Capture
   -> immutable snapshot revision=105

caller computes request

Apply(expectedRevision=105)
   -> current revision == 105 : apply
   -> current revision != 105 : RevisionConflict
```

Generic result vocabulary should stay small and subsystem-appropriate, for example:

```text
Applied
NotFound
GenerationConflict
RevisionConflict
Invalid
Rejected
Cancelled
Failed
```

Do not force every subsystem into one universal mega-result type if it harms clarity.

All successful authoritative mutations must update the owning revision and any relevant dirty state atomically from the perspective of the game loop.

---

## 9. Immutable runtime snapshots

Snapshot contracts should be runtime-owned and immutable.

Do not copy Vega's aggregate shapes blindly. Separate state by ownership so consumers do not receive unrelated sensitive or expensive data automatically.

Preferred split:

```text
PlayerStateSnapshot
    slot/generation
    position/velocity
    life/mana
    team/pvp
    spawn/lifecycle state

PlayerConnectionSnapshot
    connection source
    endpoint where exposure is explicitly allowed
    connection state
    latency/queue/network counters

PlayerInventorySnapshot
    revision
    bounded item entries

NpcSnapshot
ItemSnapshot
ProjectileSnapshot
ChestSnapshot
WorldSnapshot
```

Guidelines:

- snapshots are demand-driven, not mirrors of every internal field;
- public snapshots never expose mutable runtime objects;
- large collections are bounded/paged or represented by explicit area/query operations;
- snapshots may be created on the authoritative thread and consumed elsewhere;
- expensive formatting belongs outside the authoritative loop.

Current foundation: live players carry a non-zero authoritative `PlayerStateRevision`; accepted
appearance, equipment and movement updates advance it. `PlayerStateSnapshot` is an immutable,
protocol-neutral projection keyed by the exact `PlayerHandle`, and stale generations cannot capture
the replacement session. Life/mana, inventory and an asynchronous snapshot request boundary remain
future slices rather than being folded into this first projection.

---

## 10. Entity lifecycle foundation

A unified lifecycle is required before aggressive AOI or dirty-state replication.

Conceptual lifecycle:

```text
Created/Spawned
      |
      v
Active
      |
      v
Updated*
      |
      v
Removed/Despawned
```

Apply the model to at least:

- players;
- NPCs;
- projectiles;
- world items;
- tile entities;
- other network-visible runtime entities where justified.

Each lifecycle owner is responsible for:

- stable identity during lifetime;
- generation changes on slot reuse;
- authoritative current state;
- revision/dirty tracking;
- replication activation/deactivation semantics;
- deterministic cleanup;
- spatial-index membership where applicable.

This lifecycle becomes the common input to replication instead of packet-specific broadcast code.

Current player slice: spawn exchanges packet-14 active baselines before appearance/equipment state,
and authoritative disconnect broadcasts packet-14 inactive state. This ordering and disconnect
behavior are verified against TerrariaServer 1.4.5.8 `NetMessage.SyncOnePlayer`; later lifecycle
entities must follow the same generation-safe activation/deactivation boundary.

---

## 11. Native authoritative scheduler

Vega's bounded/time-sliced game-thread dispatcher contains useful ideas:

- bounded queue;
- explicit game-thread assertion;
- operation cap;
- CPU/time budget;
- incremental `step` work;
- dropped/deferred telemetry.

TerraRuntime must implement these natively inside its own authoritative game loop rather than hook a foreign Terraria update event.

Target shape:

```text
AuthoritativeGameLoop
    Ingress
    Commands
    ScheduledWork
    World
    Entities
    Replication
    CompletionApply
```

Requirements:

- global subsystem budgets, never a full budget per player;
- bounded queues;
- per-source fairness where appropriate;
- incremental long-running work;
- backlog size and oldest-age telemetry;
- no arbitrary async continuation inside the simulation hot path;
- worker results return through explicit authoritative apply boundaries.

This work is coordinated with `performance-tick-stability.md` rather than implemented as a parallel scheduler abstraction.

---

## 12. Replication layer

`RuntimeConnectionRegistry` should remain connection/transport state, not become the owner of world visibility semantics.

Target split:

```text
Authoritative entity/world state
        |
        v
Replication planner
        |
        +--> hard visibility
        +--> interest/AOI
        +--> priority/network LOD
        +--> resync/readiness
        |
        v
Recipient set
        |
        v
encode once
        |
        v
shared immutable frame
        |
        v
connection registry / outbound queues
```

Requirements:

- encode identical packets once per logical update when practical;
- transport registry only resolves recipients/queues and connection state;
- no gameplay/spatial policy hidden inside socket code;
- one slow recipient cannot block another;
- queue accounting works with shared immutable frames;
- replication decisions are observable through metrics.

---

## 13. Visibility and interest are different

Do not merge Vega scene visibility and spatial AOI into one boolean subsystem.

### Hard visibility

Question:

> Is observer A allowed to observe entity B at all?

Possible consumers:

- private scene/instance;
- spectator isolation;
- plugin-created logical layers;
- admin-only or scripted entities;
- future dimension/world-instance boundaries.

### Interest management

Question:

> Is entity B relevant enough to observer A to replicate now?

Inputs may include:

- sections/cells;
- distance;
- entity type;
- importance;
- recent state;
- forced-resync deadline.

Pipeline:

```text
candidate entities
    -> hard visibility
    -> spatial interest
    -> replication priority
    -> recipient set
```

Hard visibility may be extensible through a narrow policy contract. The spatial index, hysteresis, recipient tracking and AOI correctness remain TerraRuntime-owned.

---

## 14. Runtime-owned AOI control

Vega must not inject its own `SpatialInterestManager` implementation into TerraRuntime.

Allowed external control is deliberately narrow:

```text
IsEnabled
SetEnabled(bool)
```

or a future documented runtime-owned mode enum:

```text
Disabled
Conservative
Aggressive
CustomRuntimePreset
```

Even when modes expand, Vega selects a supported TerraRuntime mode. It does not receive ownership of:

- cell/grid implementation;
- observer sets;
- entity buckets;
- hysteresis internals;
- recipient queries;
- resync tracking;
- distance thresholds as arbitrary runtime callbacks.

The existing startup toggle and runtime control remain the foundation.

Real packet suppression stays blocked until lifecycle enter/leave and full-state-on-enter are correct and live-tested.

---

## 15. Replication priority / network LOD

AOI should not permanently collapse to only `send` versus `do not send`.

Plan for a runtime-owned priority model such as:

```text
Critical
High
Normal
Low
Dormant
```

Illustrative behavior, subject to benchmark and vanilla compatibility:

```text
near/critical entity     -> every eligible update
normal relevance         -> ordinary cadence
far but relevant         -> reduced cadence
outside interest         -> suppress deltas
resync deadline reached  -> forced state update
```

Candidates:

- players;
- NPCs;
- projectiles where safe;
- items;
- effects/state that do not require global delivery.

Global world/progression events remain global unless official client behavior proves otherwise.

Default/compatibility mode must preserve vanilla-like observable behavior.

---

## 16. Rate limiting architecture

Move the rate-limit mechanism into TerraRuntime, but do not treat Vega's current numerical limits as protocol constants.

Prefer a token-bucket or equivalent measured burst-aware model over a fixed reset-at-one-second window.

Conceptual policy:

```text
PacketRatePolicy
    Capacity
    RefillRate
    Burst
    Action
```

Possible actions:

```text
Allow
Defer
DropLowPriority
Reject
Violation
Disconnect
```

Not every packet category needs the same policy.

Requirements:

- per-connection accounting;
- optional category-level budgets, for example movement/tile/inventory/liquid/other;
- cleanup on disconnect/generation change;
- bounded memory independent of arbitrary packet IDs;
- monotonic timing;
- explicit burst behavior;
- metrics for throttle/reject/drop counts;
- thresholds established from official-client traces and load tests.

The mechanism is runtime security. Vega may expose operator configuration within safe ranges, but it must not be responsible for executing the limiter.

---

## 17. Backpressure and slow-client isolation

This is an inseparable part of replication ownership.

Each connection must have bounded outbound accounting for both:

- frame count;
- queued bytes.

When limits are exceeded, the runtime applies a deterministic policy:

1. discard/coalesce low-priority stale updates where semantics permit;
2. mark affected state for later full resync;
3. disconnect a pathological slow client if the queue cannot recover.

Never wait for socket progress on the authoritative game thread.

Telemetry should expose:

- queue frames/bytes;
- high-water marks;
- dropped/coalesced frames;
- packet/entity class causing pressure;
- resyncs requested because of drops;
- disconnects caused by slow-client policy.

---

## 18. Dirty state and world mutation integration

Every successful world/entity mutation must feed the appropriate dirty/revision mechanism.

Examples:

```text
tile mutation
    -> section revision++
    -> MarkDirty(section)
    -> encoded section cache stale
    -> save snapshot dirty
    -> replication dirty
```

```text
NPC change
    -> simulation revision++
    -> dirty flags
    -> replication planner
```

Do not build independent dirty systems for save, section cache and networking when the same authoritative mutation can update one shared revision/change source.

Deduplicate repeated dirty marks within a tick where possible.

---

## 19. Runtime/network/security telemetry ownership

Metrics describing TerraRuntime internals originate in TerraRuntime.

Vega may display, aggregate, persist or export them.

Minimum families:

### Networking

- connections by lifecycle state;
- inbound/outbound frames and bytes by packet type;
- malformed/rejected/throttled packets;
- queue frames/bytes/high-water marks;
- slow-client actions.

### Authoritative loop

- CPU and wall tick duration;
- per-phase duration;
- processed/deferred work;
- budget exhaustion;
- backlog size/age.

### Entities/replication

- active entities;
- dirty updates;
- encoded frames;
- shared-frame fan-out;
- candidate recipients;
- visibility rejects;
- AOI filtered frames/bytes;
- forced resyncs;
- missing/stale baseline events.

### World/save

- dirty sections;
- section cache hit/miss/invalidation;
- snapshot duration;
- serialization/write/fsync/replace duration;
- runtime-image cache hit/miss reason.

Telemetry collection must not require string formatting or allocation on every hot-path event.

---

## 20. Runtime readiness boundary

TerraRuntime and Vega each have readiness concepts, but they are not the same.

TerraRuntime owns at least:

```text
RuntimeInitialized
WorldLoading
WorldReady
NetworkReady
Stopping
Stopped
```

`NetworkReady` means the runtime can safely accept Terraria clients.

Vega may layer its own lifecycle around this:

```text
Vega configuration
modules
policy stores
operator services
background warmup
```

Vega module readiness must not redefine whether TerraRuntime's world/network internals are coherent.

Likewise TerraRuntime should not know whether an optional Vega analytics or update-check module is warmed.

---

## 21. World image migration guidance

Vega's derived world-image work is a useful source of performance lessons, but TerraRuntime owns its own format and validation.

Keep:

- `.wld` canonical source;
- disposable derived cache;
- source fingerprint/hash;
- schema/version checks;
- safe fallback to `.wld`;
- atomic rebuild;
- cache measured around real startup bottlenecks.

Do not copy:

- a format tied to Vega contracts;
- application/module metadata that is not required to reconstruct TerraRuntime state;
- assumptions based on patched Terraria runtime object layouts.

The cache should eventually cooperate with section revisions, dirty tracking and incremental save snapshots.

---

## 22. World clock migration rule

Do not move Vega's monotonic corrective clock implementation directly into TerraRuntime.

First establish official Terraria 1.4.5.8 behavior for:

- day/night progression;
- transitions;
- sleep/time acceleration;
- events that alter time;
- server/client synchronization;
- save/load semantics.

Implement and test vanilla behavior first.

Only after parity exists may TerraRuntime consider an optional runtime-owned correction policy, for example:

```text
Vanilla
MonotonicCorrected
```

`Vanilla` remains the default unless a deliberate compatibility decision says otherwise.

---

## 23. Public extension/policy boundary for Vega

TerraRuntime should expose semantic policy points only where an external decision is genuinely required.

Examples:

```text
PlayerAdmissionRequested
TileMutationRequested
ChestInteractionRequested
PlayerTeleportRequested
NpcSpawnRequested
```

The runtime supplies already-decoded, runtime-safe semantic data.

The external policy returns a bounded decision such as:

```text
Allow
Reject(reason)
```

Rules:

- policy callbacks do not mutate TerraRuntime collections directly;
- policy callbacks do not receive Multiplicity raw buffers unless the contract is explicitly protocol-level;
- policy execution on the game thread must be synchronous and bounded;
- I/O-dependent Vega policy must use precomputed/cacheable state or an explicit asynchronous lifecycle before the runtime apply point;
- TerraRuntime retains the final Apply stage and state ownership.

Do not expose internal AOI indexes, entity bucket collections, queue buffers or mutable world arrays to Vega.

---

## 24. Migration order

Use this order unless a concrete vertical slice requires a small dependency to land earlier:

1. [x] **Connection/session identity + generation**
2. [ ] **Packet ownership enforcement**
3. [ ] **Player/projectile/world/item/chest sanity**
4. [x] **Entity lifecycle foundation**
5. [x] **Immutable snapshots + generation/revision mutation model**
6. [x] **Native authoritative scheduler primitives**
7. [x] **Replication layer separated from connection registry**
8. [ ] **Hard visibility + runtime-owned AOI**
9. [ ] **Rate limiting + backpressure**
10. [x] **Runtime/network/security telemetry**
11. [x] **Save/runtime-world snapshot improvements**
12. [ ] **Vanilla world clock**
13. [ ] **Optional measured replication/network LOD optimizations**

Steps 4 through 8 are conceptually coupled. Do not enable aggressive AOI suppression before lifecycle and resync correctness exist.

---

## 25. Vertical-slice migration method

For each capability migrated from Vega:

```text
1. Identify current Vega behavior/invariant
2. Classify: runtime invariant vs Vega policy
3. Verify official/Multiplicity semantics
4. Add TerraRuntime contract only if needed
5. Implement through authoritative ownership
6. Add focused unit tests
7. Add protocol/integration/live tests where observable
8. Add telemetry
9. Prove NativeAOT compatibility
10. Switch Vega consumer to TerraRuntime contract
11. Remove duplicate Vega implementation
```

Do not leave two active authoritative implementations after migration.

A temporary adapter is acceptable only when clearly marked migration debt and covered by tests proving which side is authoritative.

---

## 26. Acceptance criteria

This ownership migration is considered complete only when:

- [ ] TerraRuntime can run a correct Terraria server without Vega being present;
- [x] connection/player identity enforcement is runtime-owned;
- [ ] all runtime-dangerous coordinate/index/entity sanity checks execute before Vega policy;
- [ ] Vega no longer owns packet-wire parsing beyond consumption of TerraRuntime semantic APIs;
- [x] Multiplicity remains the shared protocol codec/model implementation;
- [ ] player/NPC/projectile/item lifecycle uses generation-safe identities where slot reuse exists;
- [x] snapshots are immutable and runtime-owned;
- [x] revision-guarded mutations reject stale state deterministically;
- [x] connection registry is not the owner of gameplay/spatial recipient policy;
- [x] replication has an explicit planner/recipient boundary;
- [x] hard visibility and spatial interest are modeled separately;
- [x] AOI internals remain TerraRuntime-owned and Vega can only use documented controls;
- [ ] real AOI culling is enabled only after enter/leave/full-resync correctness is proven;
- [ ] rate limiting/backpressure are runtime security primitives;
- [x] runtime metrics originate in TerraRuntime and are consumable by Vega/TUI/API;
- [x] world/save cache formats do not depend on Vega application internals;
- [ ] vanilla world-clock behavior is independently verified before optional correction modes;
- [ ] duplicate migrated implementations are removed from Vega after consumers switch;
- [ ] all migrated slices remain green under CoreCLR tests, Linux NativeAOT, Windows NativeAOT and relevant real-client/live-world smoke tests.

---

## 27. Target boundary after migration

```text
┌─────────────────────────────────────┐
│                VEGA                 │
│                                     │
│ Accounts / Groups / Permissions     │
│ Commands / Moderation / Regions     │
│ Plugins / REST / UI / Persistence   │
│ Gameplay/application policy         │
└──────────────────┬──────────────────┘
                   │ semantic API
                   v
┌─────────────────────────────────────┐
│            TERRARUNTIME             │
│                                     │
│ Authoritative World                 │
│ Players / NPC / Items / Projectiles │
│ Chests / Tiles / TileEntities       │
│                                     │
│ Entity lifecycle                    │
│ Generation / Revision               │
│ Game loop / Scheduler               │
│ Validation / Security invariants    │
│ Replication                         │
│ Visibility                          │
│ Interest Management / AOI           │
│ Rate limiting / Backpressure        │
│ Save / Snapshot                     │
│ Runtime telemetry                   │
│ Connection lifecycle                │
└──────────────────┬──────────────────┘
                   │ typed packet wire
                   v
┌─────────────────────────────────────┐
│            MULTIPLICITY             │
│                                     │
│ Packet models / views               │
│ Decode / Encode                     │
│ Packet metadata                     │
└─────────────────────────────────────┘
```

This boundary is normative even when exact project/folder names evolve.
