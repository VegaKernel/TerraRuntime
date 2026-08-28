# Gameplay and world-generation extensibility roadmap

This document defines how TerraRuntime can expose controlled customization of NPC behavior, projectile behavior and world generation without giving an external host a second mutable owner of Terraria simulation state.

The goal is not to turn TerraRuntime into a reflection-driven mod loader. The runtime remains NativeAOT-first, deterministic where vanilla requires it, and authoritative over entity/world lifecycle. Vega or another host may register behavior and generation providers through explicit TerraRuntime contracts, but TerraRuntime owns execution, validation, scheduling, state commit and replication.

The governing rule is:

> **Extensions may supply policy and behavior; TerraRuntime remains the only authority that applies simulation state.**

This work builds on the existing single-writer game loop, generation/revision handles, runtime-owned interest management and bounded-work rules from the main roadmap.

---

## 1. Scope and ownership

### TerraRuntime owns

- NPC and projectile lifecycle;
- entity slot/generation/revision identity;
- authoritative position, velocity, combat state and despawn rules;
- vanilla NPC/projectile AI implementations;
- extension dispatch and ordering;
- safe mutation contexts;
- per-world extension activation;
- exception isolation and timing telemetry;
- deterministic RNG services exposed to extensions;
- dirty tracking and replication after an extension changes authoritative state;
- world-generation pass orchestration, validation and final world commit;
- all wire/protocol representation through the existing protocol boundary.

### Vega or another host owns

- discovering/loading its own modules or plugins;
- deciding which plugin is allowed to register a behavior or generator;
- user-facing configuration and permissions;
- choosing which registered behavior/generation profile is enabled for a world;
- plugin hot-reload policy;
- application-level persistence for plugin configuration/state that is not part of canonical Terraria world state.

TerraRuntime must never reference Vega assemblies, scan plugin directories, discover arbitrary managed assemblies or depend on reflection-based registration.

---

## 2. Compatibility boundary: what “custom mob/projectile” means

An unmodified official Terraria client only understands the NPC, projectile, item, tile, wall and other network-visible type IDs implemented by that client.

Therefore TerraRuntime should support two distinct concepts.

### Server-defined custom archetype on an unmodified client

A custom NPC or projectile may have:

- a stable TerraRuntime/host-defined archetype ID;
- custom server-side AI;
- custom stats and scaling;
- custom targeting and timing;
- custom spawn/despawn rules;
- custom drops/effects assembled from vanilla-visible content;
- custom name/text where the protocol permits it;
- custom extension state;
- a chosen vanilla NPC/projectile type used as its client-visible presentation.

This enables substantially different gameplay while remaining compatible with the official client.

### Truly new client-visible content

A new sprite, animation set, NPC net ID, projectile net ID, tile type or other client-unknown representation requires a modified client and an explicitly versioned protocol/content extension.

That is **not** part of the initial TerraRuntime extensibility contract. Do not pretend a server-only plugin can create a client-native asset/type that the official client does not contain.

---

## 3. Extension registration model

Registration must be explicit and AOT-safe.

Conceptual public contracts:

```text
INpcBehaviorProvider
IProjectileBehaviorProvider
INpcArchetypeProvider
IProjectileArchetypeProvider
IWorldGenerationProvider
```

Exact names may change during implementation, but the constraints are normative.

Requirements:

- no assembly scanning;
- no runtime code generation;
- no `Type`-based magic registry as the primary dispatch mechanism;
- registration uses stable string/struct IDs and explicit instances/delegates supplied by the host;
- registration returns a lease/token so it can be removed deterministically;
- duplicate exclusive registrations are rejected with a clear conflict result;
- ordering is explicit and stable, never dependent on dictionary or assembly enumeration order;
- registrations are scoped to a runtime/world unless deliberately declared reusable;
- changes become visible only at an authoritative safe point, normally a tick boundary;
- the hot path reads an immutable/precomputed dispatch table rather than locking a mutable registry.

A host may build or replace registry snapshots off-thread, but the authoritative loop swaps the active snapshot at a defined commit point.

---

## 4. NPC behavior extension pipeline

Vanilla NPC AI remains the default behavior and the differential-test baseline.

The extension model must support three common use cases without forcing every plugin to replace the whole AI routine.

### Observe/decorate vanilla

Use when a plugin wants to make a small change while preserving normal AI.

Examples:

- alter target selection under a condition;
- add an effect after movement;
- scale velocity or damage;
- add a server-side phase/timer;
- suppress one vanilla action while leaving the rest intact.

### Replace vanilla behavior

Use when a custom archetype owns the NPC’s complete server-side AI.

The vanilla AI function for that tick is not executed unless the replacement explicitly delegates to it.

### Compose ordered behavior stages

The target shape should be middleware-like but synchronous and allocation-light:

```text
NPC tick
  -> pre-behavior decorators
  -> optional exclusive replacement OR vanilla AI
  -> post-behavior decorators
  -> authoritative validation/normalization
  -> revision + dirty tracking
  -> replication planning
```

Do not expose an unconstrained asynchronous `OnTick` API. NPC behavior executes on the authoritative game-loop thread because it mutates authoritative simulation state.

### NPC behavior context

Extensions should receive a narrow runtime-owned context rather than raw mutable collections.

Useful capabilities include:

- current NPC handle, type/archetype and immutable identity;
- read-only NPC state plus controlled setters/commands for the current NPC;
- bounded queries for nearby players/NPCs/world geometry;
- target selection helpers;
- collision/world-query helpers;
- spawn requests submitted through runtime-owned entity creation APIs;
- projectile creation requests through the authoritative projectile subsystem;
- deterministic extension RNG;
- current world/tick identity;
- extension-scoped state access.

The context must not expose the internal NPC array, player array, transport queues or raw packet sending.

### Lifecycle hooks

The first useful surface should cover:

- spawn/initialization;
- AI/tick;
- target-change decision points where needed;
- hit/damage decision points where needed;
- death/despawn cleanup.

Do not add dozens of speculative callbacks in advance. Add new semantic hooks only when a real Vega/plugin use case cannot be represented safely by the current behavior context.

---

## 5. Projectile behavior extension pipeline

Projectiles need their own contract rather than reusing NPC behavior through a generic entity callback.

NPCs and projectiles have different lifecycle, collision, ownership and replication semantics. A universal `IEntityTickPlugin` would create an attractive but expensive and unsafe abstraction.

Target pipeline:

```text
Projectile tick
  -> pre-behavior decorators
  -> optional exclusive replacement OR vanilla projectile AI
  -> movement/collision resolution
  -> hit/tile-collision semantic hooks
  -> post-behavior decorators
  -> lifetime/kill decision
  -> revision + dirty tracking
  -> replication planning
```

The projectile behavior context should expose, through controlled APIs:

- projectile handle/generation/revision;
- authoritative owner information;
- type/archetype and vanilla presentation type;
- position, velocity, rotation, scale and lifetime fields that are safe to customize;
- bounded player/NPC/world queries;
- collision helpers;
- damage/hit requests through the combat subsystem;
- child-projectile spawn requests;
- deterministic extension RNG;
- extension-scoped state.

Initial semantic hooks should cover:

- spawn/initialization;
- AI/tick;
- tile collision;
- hit NPC/player where the authoritative combat path supports it;
- kill/despawn.

Projectile ownership, legal target validation, finite values, slot/generation validity and protocol-safe field ranges remain runtime invariants even when behavior is custom.

---

## 6. Custom NPC/projectile archetypes

Behavior and presentation should be separated.

Conceptually:

```text
CustomNpcArchetype
    Id
    VanillaPresentationNpcType
    BaseStats
    BehaviorId
    SpawnPolicyId?
    DropPolicyId?

CustomProjectileArchetype
    Id
    VanillaPresentationProjectileType
    BaseStats
    BehaviorId
```

Requirements:

- archetype IDs are namespaced/stable and never confused with Terraria wire IDs;
- the runtime validates the chosen vanilla presentation ID against the active Terraria protocol/version;
- network replication always emits protocol-valid vanilla-visible values unless an explicit future modded-client protocol extension is active;
- custom archetype identity may be exposed in TerraRuntime snapshots/diagnostics without leaking it into unsupported vanilla packet fields;
- save/persistence semantics are explicit: ephemeral custom entities do not silently become new `.wld` entity types;
- if persistent custom state is later required, use a separately versioned extension store rather than corrupting canonical `.wld` compatibility.

---

## 7. Extension-scoped entity state

Custom AI needs per-entity state, but the hot path must not become a dictionary of arbitrary `object` values.

Target requirements:

- state is owned by a registered behavior/archetype;
- state lifetime is tied to entity generation and is removed on despawn/slot reuse;
- registration removal cleans or invalidates its state deterministically;
- no reflection-based serialization is required;
- no per-tick allocation is required for common state access;
- the implementation should prefer pre-registered typed/fixed slots, compact side arrays or another measured layout over `Dictionary<string, object>` per entity;
- large plugin state remains outside the core entity record unless benchmarks prove otherwise.

A first implementation may use a simple bounded side store if it is not on a proven hot path, but the ceiling and upgrade trigger must be documented.

---

## 8. RNG and determinism

Vanilla RNG ordering is observable in multiple gameplay/worldgen systems and must not be accidentally perturbed by an extension that merely observes or decorates behavior.

Rules:

- vanilla AI continues using its verified vanilla-compatible RNG stream/order;
- extension behavior receives a separate deterministic extension RNG by default;
- extension RNG streams are derived from stable world seed/runtime identity plus extension/archetype/entity identity in a documented way;
- adding/removing an unrelated extension must not silently shift vanilla RNG state;
- a full behavior replacement may intentionally own its own deterministic RNG semantics;
- any API that deliberately allows an extension to participate in the vanilla RNG stream must be explicit, privileged and regression-tested because it can change downstream vanilla outcomes.

Deterministic replay tests should cover behavior registration order, fixed seeds and entity slot reuse.

---

## 9. Performance budgets and fault isolation

A plugin callback that runs once per NPC/projectile tick can destroy a 60 Hz server very efficiently, which is an impressive but undesirable form of extensibility.

Requirements:

- behavior dispatch adds no avoidable allocation on the common path;
- no `Task`, blocking I/O, sleep, file access or network access from the authoritative behavior callback;
- expose per-provider call count, wall/CPU time where practical, worst call and accumulated tick cost;
- account NPC and projectile extension time inside their corresponding game-loop phase metrics;
- support per-world enable/disable without restarting the process;
- catch extension exceptions at the registration boundary so one provider does not crash the server;
- define deterministic failure policy per registration mode.

Recommended failure policy:

```text
Decorator throws
  -> record fault
  -> skip that decorator for the current call
  -> preserve vanilla/remaining pipeline when safe

Exclusive replacement throws
  -> record fault
  -> mark provider unhealthy
  -> fall back to vanilla behavior for compatible vanilla-backed archetypes
     OR safely despawn an extension-only archetype if vanilla fallback is undefined
```

Repeated-fault and repeated-over-budget thresholds may auto-disable a registration at a safe tick boundary. The runtime cannot hard-preempt arbitrary synchronous managed code; strict CPU isolation would require a stronger sandbox/process boundary and is not promised by this in-process contract.

---

## 10. Hot reload and registration lifetime

TerraRuntime should support safe re-registration without becoming the plugin loader itself.

- Vega may unload/reload a plugin and submit a new registration set.
- TerraRuntime swaps registry snapshots only at a safe authoritative boundary.
- active entities must not hold a raw delegate/reference that keeps an unloaded plugin load context alive indefinitely;
- prefer stable behavior IDs plus registry indirection or explicit registration leases whose retirement is observable;
- unregistering a decorator returns affected entities to the remaining pipeline on the next safe boundary;
- unregistering an exclusive replacement must have an explicit fallback: vanilla, alternate registered provider or controlled despawn;
- extension-scoped state cleanup must finish before a plugin unload is considered complete by the host.

Hot reload is a host capability. TerraRuntime only guarantees deterministic registration lifecycle and cleanup semantics.

---

## 11. World-generation extensibility

Vanilla world generation remains a first-class generator and the reference implementation for official-client-compatible worlds.

Custom generation should be exposed as an explicit pass plan rather than arbitrary mutation of the live world from a plugin thread.

Conceptual model:

```text
WorldGenerationRequest
  -> select generation profile
  -> build validated pass plan
  -> create isolated generation workspace
  -> execute passes in deterministic order
  -> validate generated world
  -> finalize runtime metadata/indexes
  -> commit as world
  -> WorldReady
```

### Generation contracts

Conceptual public surface:

```text
IWorldGenerationProvider
IWorldGenerationPass
WorldGenerationPassDescriptor
WorldGenerationPlan
WorldGenerationContext
WorldGenerationResult
```

Exact names may change, but the following capabilities are required.

A provider may:

- select the vanilla generator unchanged;
- add custom passes before/after named extension points;
- replace a specifically replaceable pass;
- build a complete custom pass plan;
- choose parameters such as seed, size and supported vanilla world options;
- report progress through a bounded runtime-owned progress channel.

### Stable pass IDs and dependencies

Each pass has a stable ID and explicit ordering/dependency metadata.

The planner must:

- reject duplicate exclusive pass IDs;
- detect cycles;
- reject missing required dependencies;
- distinguish optional ordering hints from hard dependencies;
- produce one deterministic final order;
- record the resolved plan in diagnostics so a generated world can be reproduced.

Do not let registration order in a dictionary silently decide world layout.

### Isolated generation workspace

Worldgen must not mutate an already-live authoritative world from a background plugin callback.

- generation runs against an isolated workspace owned by the generation job;
- the workspace uses runtime-owned tile/world accessors, not raw process globals;
- bounds and supported vanilla content IDs are validated by the runtime;
- final commit occurs only after required validation succeeds;
- cancellation/failure discards the incomplete workspace rather than partially replacing a live world;
- generated `.wld` output remains compatible with Terraria 1.4.5.8 unless an explicitly different future format is selected.

For a new world created before server readiness, worldgen may use dedicated workers where ownership is isolated. For regeneration of a running server, generate a separate candidate world and switch only through an explicit lifecycle operation; never rewrite the active tile array concurrently with gameplay.

### Worldgen RNG rules

Vanilla parity mode must preserve verified vanilla RNG ordering.

Custom passes should receive an isolated deterministic RNG stream by default so inserting a decorative custom pass does not shift every later vanilla random decision.

A pass descriptor should state one of the following semantics:

```text
VanillaSharedRng     = participates in verified vanilla order; restricted use
IsolatedDeterministic = independent stream derived from seed + stable pass ID
CustomProviderRng    = provider owns documented deterministic semantics
```

Only the vanilla generator and deliberately compatible replacement passes should normally use `VanillaSharedRng`.

### Parallelism

- never parallelize vanilla passes that share order-sensitive mutable state/RNG merely because worker threads exist;
- independent custom computation may run on workers against immutable/isolated data;
- deterministic apply order is required when worker results modify shared generation state;
- require bit-identical tests for any optimization claiming vanilla-equivalent output;
- report CPU/wall time, allocations and output impact per pass.

### Content compatibility

A custom generator targeting the official client may only emit vanilla-known tile/wall/liquid/entity types and protocol-valid metadata.

Generating genuinely new tile/entity types belongs to a future modded-client protocol/content layer, not to the baseline worldgen API.

---

## 12. Vega integration boundary

The intended dependency direction is:

```text
Vega plugin/module
    |
    | Vega permission/config/hot-reload policy
    v
Vega TerraRuntime adapter
    |
    | explicit registration / enable-disable requests
    v
TerraRuntime.Contracts
    |
    v
TerraRuntime authoritative registries and world/game loop
```

Vega should be able to expose high-level plugin SDK methods such as:

```text
RegisterNpcBehavior(...)
RegisterProjectileBehavior(...)
RegisterNpcArchetype(...)
RegisterProjectileArchetype(...)
RegisterWorldGenerator(...)
EnableForWorld(...)
```

Those are Vega-facing conveniences. The actual execution contract and invariants belong to TerraRuntime.

Vega must not:

- patch TerraRuntime entity arrays directly;
- send fake packets as a substitute for authoritative state mutation;
- run NPC/projectile AI on a separate Vega thread;
- own worldgen pass ordering;
- bypass generation/revision handles;
- bypass dirty tracking/replication after a custom mutation.

---

## 13. Security and trust model

This is a trusted-host extension API, not a security sandbox for hostile native/in-process code.

TerraRuntime still validates every extension-produced mutation before commit:

- finite numeric state;
- world bounds;
- valid entity handles/generations;
- supported vanilla type IDs for official-client mode;
- legal stack/lifetime/range values where applicable;
- bounded spawn requests;
- bounded world-query areas;
- bounded per-tick work submitted back through runtime APIs.

A provider cannot use a behavior context to obtain raw socket access or arbitrary packet emission.

If untrusted third-party code later requires strong isolation, design an out-of-process/sandboxed extension transport separately rather than pretending in-process C# callbacks are a security boundary.

---

## 14. Testing requirements

### NPC behavior

- vanilla behavior path is unchanged with no registrations;
- decorator ordering is deterministic;
- exclusive replacement suppresses vanilla only when selected;
- exception fallback works;
- unregister returns to the expected behavior;
- slot reuse clears extension state;
- fixed-seed extension RNG replay is deterministic;
- custom archetype replicates a protocol-valid vanilla presentation.

### Projectile behavior

- vanilla behavior path remains unchanged by default;
- ownership/generation checks cannot be bypassed by custom behavior;
- collision/hit hooks preserve authoritative combat validation;
- child projectile spawning is bounded and generation-safe;
- kill/despawn cleans extension state;
- custom archetype never emits unsupported client type IDs.

### Worldgen

- vanilla generator still matches selected deterministic reference seeds where exact parity is required;
- pass graph ordering/cycle/conflict tests;
- isolated RNG insertion does not perturb vanilla downstream RNG;
- cancellation leaves no partially committed world;
- invalid custom tile/entity IDs are rejected;
- real generated `.wld` files load in the official 1.4.5.8 server/client for official-client-compatible profiles;
- generation plan and seed are sufficient to reproduce deterministic custom output.

### Performance

Benchmark at least:

- no extensions registered;
- one decorator for all active NPCs;
- one replacement behavior for a dense NPC workload;
- high projectile-count workload with and without a decorator;
- registration snapshot swap;
- worldgen baseline versus representative custom pass plans.

The zero-extension path must remain extremely close to the direct vanilla dispatch path. Extensibility is not permission to add a virtual-call forest and allocations to every entity tick.

---

## 15. Delivery order

### G0 - Contract foundation

- stable behavior/archetype IDs;
- explicit AOT-safe registration API;
- immutable registry snapshots and tick-boundary swap;
- extension diagnostics identity;
- extension RNG service;
- extension-state lifecycle primitive.

### G1 - NPC behavior

- vanilla/default dispatch;
- decorators;
- exclusive replacement;
- spawn/tick/despawn hooks;
- Vega adapter proof-of-concept;
- focused performance gate.

### G2 - Projectile behavior

- dedicated projectile pipeline;
- collision/hit/kill semantics;
- child spawn requests;
- Vega adapter proof-of-concept;
- focused performance gate.

### G3 - Custom archetypes

- server-defined NPC/projectile archetype descriptors;
- vanilla presentation mapping;
- snapshot/diagnostic identity;
- explicit persistence semantics.

### G4 - Worldgen pass planner

- provider/pass contracts;
- stable pass IDs/dependencies;
- deterministic planner;
- isolated workspace;
- progress/cancellation;
- isolated deterministic RNG mode.

### G5 - Vega/plugin integration

- permission/config policy stays in Vega;
- registration leases tied to plugin lifecycle;
- safe unregister/hot-reload flow;
- per-world enable/disable;
- example plugin implementing a modified vanilla mob and projectile plus a small custom worldgen pass.

---

## Definition of done

This roadmap slice is not complete until:

- TerraRuntime can run with zero extensions and preserve vanilla behavior/performance baselines;
- a host can explicitly register an NPC decorator and replacement without reflection or runtime assembly scanning;
- a host can explicitly register a projectile decorator and replacement through a dedicated projectile contract;
- custom server-side NPC/projectile archetypes can use vanilla client presentations safely;
- registration removal and plugin reload cannot leave stale callbacks or per-entity state behind;
- extension faults are isolated and observable;
- extension CPU cost is visible in tick telemetry;
- a custom world-generation provider can add/replace passes in a deterministic validated plan;
- failed/cancelled generation cannot partially commit a world;
- official-client-compatible generated worlds contain only protocol/content IDs the client understands;
- Linux and Windows NativeAOT smoke paths remain green with the extension contracts present.
