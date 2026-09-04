# TerraRuntime roadmap

TerraRuntime is a clean-room C# implementation of the Terraria dedicated server runtime. The project preserves observable vanilla gameplay behavior while replacing fragile legacy internals with explicit, testable, secure and high-performance subsystems.

The governing rule is: **behavioral parity where players can observe it; freedom of implementation everywhere else.**

Detailed performance and tick-stability work is tracked in [`roadmap/performance-tick-stability.md`](roadmap/performance-tick-stability.md). NativeAOT production constraints are normative and live in [`native-aot-baseline.md`](native-aot-baseline.md).


> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

## Reference hierarchy

When sources disagree, use this order of authority:

```text
TerrariaServer 1.4.5.8 decompiled  = primary behavioral/runtime truth
Multiplicity 3.0.0                 = primary protocol library for protocol 326 / Terraria 1.4.5.8
terrustia                          = secondary independent cross-check
TShock / OTAPI                    = behavioral/history reference only
```

Rules:

- The locally decompiled official `TerrariaServer.exe` **1.4.5.8** is the primary source of truth for vanilla behavior, `.wld` layout, gameplay ordering, state transitions, packet handling semantics and runtime constants.
- `VegaKernel/Multiplicity` **3.0.0** is the current protocol implementation baseline for protocol **326 / Terraria 1.4.5.8**. Keep golden bytes and real official-client captures as independent verification of its wire behavior.
- `terrustia` is valuable as an independent implementation, testing reference and source of architectural/performance ideas, but it never overrides the current official 1.4.5.8 server when behavior or format details differ.
- TShock/OTAPI are useful for historical behavior, compatibility knowledge and exploit lessons, but they never define TerraRuntime architecture or override current vanilla behavior.
- Never infer a 1.4.5.8 file/packet/gameplay layout from an older-version reference when the 1.4.5.8 decompiled server can answer the question directly.
- Decompiled official source remains local reference material only and is never committed or copied method-for-method into clean implementation code.

## Platform baseline

- Target **.NET 11** from the start.
- The shipping server is **NativeAOT-first**. Linux x64 and Windows x64 native publication and exercised smoke paths are part of the production contract.
- CoreCLR may be used during development, debugging and profiling when it improves iteration, but production architecture must not rely on JIT-only behavior.
- Do not preserve compatibility with older .NET runtimes unless a concrete deployment requirement appears later.
- Use current .NET 11 BCL, GC, `System.IO.Pipelines`, spans, source generators and performance APIs instead of carrying legacy compatibility abstractions.
- Enable nullable reference types, warnings as errors, deterministic builds and analyzers.
- Do not introduce arbitrary managed DLL loading, runtime code generation, reflection-driven registration or serializers without an explicit trimming/AOT contract.
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

```mermaid
flowchart TD
    TCP["TCP clients"] --> Read["Connection read loops"]
    Read --> Frame["Frame decoder / protocol validation"]
    Frame --> Commands["Typed inbound commands"]
    Commands --> Loop["Authoritative game loop<br/>single writer"]
    Loop --> State["World / Players / NPCs / Projectiles / Items"]
    State --> Gameplay["Deterministic gameplay systems"]
    Loop --> Events["Outbound state / events"]
    Events --> Sync["Per-client sync planner"]
    Sync --> Queues["Encoded packet queues"]
    Queues --> Writers["Socket writers"]
```

Connection code never mutates the world directly. Background work produces immutable results that are applied through explicit game-loop commands.

## Phase 0 - Repository and .NET 11 baseline

- [x] Keep production code under root `src/`.
- [x] Keep tests under root `tests/`.
- [x] Keep locally decompiled official server output under ignored `decompiled/` only.
- [x] Pin the .NET 11 SDK through `global.json`.
- [x] Record the exact Terraria dedicated-server version and binary SHA-256 used as behavioral reference.
- [x] Maintain reproducible local tooling to download and decompile the official server.
- [x] Add .NET 11 CI for restore, build, test and formatting/analyzers.
- [x] Keep Linux and Windows NativeAOT publish + native smoke jobs green.
- [x] Enable nullable, warnings as errors and deterministic builds.
- [ ] Remove dependencies and patterns retained only for old .NET/Mono compatibility.

## Phase 1 - Typed protocol core

Build a standalone Terraria protocol layer before gameplay.

### Multiplicity bootstrap

Use the `VegaKernel/Multiplicity` NuGet package (`Multiplicity`) as the typed packet implementation.

- [x] Baseline package: `Multiplicity` **3.0.0**, protocol **326 / Terraria 1.4.5.8**.
- [ ] Use its typed packet model when packets need ownership, mutation or re-serialization.
- [ ] Prefer its zero-copy `PacketView` / `PacketViewParser` path for hot-path inspection.
- [x] Keep Multiplicity behind the runtime protocol boundary so gameplay code does not depend on concrete packet-library types.
- [ ] Put protocol fixes in Multiplicity when they belong to the shared protocol model rather than duplicating a second full packet parser in the runtime.
- [ ] Keep golden-byte and real-client captures as the final independent protocol verification; a green Multiplicity round trip cannot prove parity by itself.

### Framing

- [x] Incremental parser for `[u16 length][u8 message id][payload]`.
- [x] Correctly handle fragmented and coalesced TCP reads.
- [x] Reject impossible lengths, oversized frames and truncated payloads deterministically.
- [x] Never allocate directly from an untrusted client-declared length without a hard ceiling.

### Typed packets

- [ ] Replace magic offsets in gameplay code with typed packet records/structs.
- [x] Centralize message IDs in one named catalog.
- [x] Use explicit bounded `TryDecode`/`Decode` contracts.
- [ ] Represent optional wire sections with named flags and types rather than scattered bit arithmetic.
- [ ] Preserve raw trailing bytes only when protocol semantics genuinely require transparent relay.
- [ ] Use source generation for codecs only where it simplifies correctness or produces measurable gains.

### .NET 11 hot path

- [x] `ReadOnlySpan<byte>` / `Span<byte>` codecs.
- [x] `SequenceReader<byte>` where segmented input is useful.
- [x] `IBufferWriter<byte>` for encoding.
- [x] `PipeReader` / `PipeWriter` compatible framing.
- [ ] Avoid `MemoryStream`, `BinaryReader`, LINQ and temporary arrays on common packet paths.
- [ ] Use `ArrayPool<T>` or `MemoryPool<T>` for large temporary buffers when measurement justifies pooling.

### Security

- [x] Separate byte decoding from gameplay/state validation.
- [x] Per-message size ceilings.
- [x] Per-connection rate accounting.
- [x] Protocol fuzz tests and a permanent malformed-packet corpus.
- [x] Parsing failure returns a bounded error and never crashes the server process.

## Phase 2 - Networking runtime

- [x] Use `System.IO.Pipelines` or an equivalent measured low-allocation .NET 11 socket pipeline.
- [x] One read loop and one write path per connection.
- [x] Bounded outbound queues with an explicit slow-client policy.
- [ ] Size queue limits from measured real workloads and configured player count rather than a guessed constant.
- [x] Batch already queued small frames into fewer socket writes without intentionally delaying latency-sensitive traffic.
- [x] Use `TCP_NODELAY` unless measurement demonstrates a better policy.
- [x] Handshake deadline plus normal idle timeout.
- [x] Gate maximum concurrent connections before allocating expensive player state.
- [x] Cancellation must release all connection resources on every exit path.

### DoS hardening

- [x] Global and per-connection budgets for expensive work.
- [x] Bound password/KDF work, compression and section requests.
- [x] Independent rate limits for tile edits, liquids, chat, item operations and other expensive packet classes.
- [x] Distinguish malformed protocol, rate limit, invalid state and gameplay rejection in structured telemetry.

## Phase 3 - Authoritative game loop and threading

Use a single-writer simulation model initially, borrowing the useful actor-style ownership pattern demonstrated by terrustia.

- [x] One dedicated game-loop thread owns mutable world, players, NPCs, projectiles, items and progression state.
- [x] Connections submit typed commands/events through bounded channels.
- [ ] No socket callback, TUI thread, timer callback or background worker mutates game state directly.
- [ ] No locks on the main simulation hot path.
- [x] Preserve packet order per connection.
- [x] Bound inbound work processed per tick so one connection cannot monopolize a frame.
- [ ] Use global operation budgets per subsystem; never multiply a full subsystem budget by player count.
- [x] The command loop keeps a hard global operation cap, per-source fairness quota and optional authoritative-thread CPU-time cap, with deferred-work and backlog-age telemetry.
- [x] Fixed 60 Hz simulation schedule with an explicit missed-tick policy.
- [x] Measure both wall time and CPU time; report them separately so OS contention is not confused with simulation cost.
- [x] Record timing per simulation phase and report the worst phase, not only total tick time.

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

### Worker pool

Multithreading is used aggressively only where ownership is clear.

- [x] Networking remains asynchronous and independent per connection.
- [ ] Disk I/O, compression, hashing and other blocking work run outside the game loop.
- [x] CPU-heavy background work uses bounded dedicated workers, not unbounded `Task.Run` fan-out.
- [x] Workers receive immutable snapshots or isolated buffers and return results through explicit completion channels.
- [ ] The game thread applies results at well-defined commit points.
- [ ] Do not parallelize gameplay/worldgen passes that share an order-sensitive RNG stream or read state modified by neighbouring work.
- [ ] Parallelize only proven-independent work and require bit-identical/deterministic regression tests when vanilla ordering matters.

## Phase 4 - World representation

- [ ] Preserve vanilla `.wld` compatibility as a hard requirement unless explicitly versioned otherwise.
- [ ] Separate persistence representation from in-memory simulation representation.
- [ ] Partition the world into sections/chunks for locality, visibility and dirty tracking.
- [ ] Track dirty regions so networking and saving do not scan the entire world.
- [ ] Avoid allocating objects per tile.
- [ ] Benchmark AoS versus SoA/packed layouts before choosing a permanent tile representation.
- [ ] Cache derived section metadata only with explicit invalidation rules.

### Encoded section cache

Borrow the useful terrustia pattern, adapted to .NET 11:

- [ ] cache already encoded/compressed section packets after first construction;
- [ ] invalidate a cached section only when a tile/world mutation affecting it commits;
- [ ] disable dirty-tracking overhead during initial world load/generation when nearly every tile is written;
- [ ] never mark a section as delivered until its encoding has actually succeeded;
- [ ] keep cache memory bounded and observable.

### Save pipeline

- [x] Snapshot required mutable state on the authoritative game-loop owner.
- [x] Serialize and write detached save snapshots outside the game loop.
- [x] Atomically replace the canonical save only after successful complete serialization.
- [x] Keep disk I/O off the simulation thread; tile-shadow synchronization uses a bounded default budget of `4 sections/tick`.
- [x] Permit only one save serialization at a time; coalesce redundant autosave requests rather than building a backlog.
- [x] Graceful shutdown (`Ctrl+C` / POSIX `SIGTERM`) stops the authoritative owner, captures the newest final state and waits for the save coordinator to commit it.
- [x] Maintain the save tile shadow incrementally from dirty sections instead of copying the complete tile array in one save tick.
- [x] Flush save contents before publication and, on Linux, `fsync` the parent directory after replace/move so the directory entry has an explicit durability barrier.
- [x] Regression-test the pre-publication process-crash invariant with a real `SIGKILL`: an existing canonical save stays byte-identical, a first save stays hidden, and the next normal save can still commit (`Authoritative World Save` run `33267501627`).
- [x] Complete broad interrupted-save/crash recovery: durable marker-authorized roll-forward, unsealed-orphan discard, live-writer exclusion, stale-conflict quarantine, previous-generation backup safety, and post-publication stale-sidecar cleanup are covered by unit tests plus the dedicated real-`SIGKILL` recovery workflow.
- [x] Keep one validated previous-generation backup and perform fail-closed automatic rollback for structurally/content-corrupt canonical checkpoints. `World Checkpoint Recovery` run `33269875235` proved backup rotation, exact recovery, invalid-backup refusal, official TerrariaServer 1.4.5.8 reload, and no rollback for unsupported future version `327`.
- [x] Add lease-safe orphan `.tmp` cleanup without deleting a temporary file owned by another live writer/process. `Authoritative World Save` run `33270924996` proved cleanup helper `5/5`, real host startup cleanup before world load `1/1`, plus the SIGKILL durability contract; cleanup also runs before later writes, while unleased legacy temporaries are left untouched because ownership cannot be proven.

## Phase 5 - Fast startup / cached runtime world image

Keep `.wld` as the canonical source of truth and add a disposable optimized runtime cache, following the useful lessons from Vega's derived world-image work.

```text
.wld              = canonical Terraria persistence / recovery format
.runtime-world     = disposable, versioned, optimized startup image
```

The runtime image should contain **already prepared runtime state**, not merely a second copy of the `.wld` bytes.

Candidate contents:

- [ ] packed/predecoded tiles or section blocks;
- [ ] world metadata;
- [ ] chests, signs and tile entities;
- [ ] liquid state and pending liquid work where required for behavioral parity;
- [ ] section metadata and dirty-state bootstrap data;
- [ ] measured expensive runtime indexes;
- [ ] prebuilt data useful for initial player section synchronization.

### Validity

Cache acceptance requires at least:

- [x] source `.wld` content hash/fingerprint;
- [x] Terraria world format/version;
- [x] runtime-image schema version;
- [x] critical compiler/layout parameters;
- [x] per-section or whole-image integrity checks.

Timestamps alone never decide validity.

### Atomic rebuild and fallback

```mermaid
flowchart LR
    Temp["world.runtime.tmp"] --> Write["Complete write"]
    Write --> Flush["Flush + integrity checks"]
    Flush --> Replace["Atomic replace"]
    Replace --> Cache["world.runtime-world"]
```

- [x] Cache corruption/staleness is never canonical world corruption.
- [x] Any validation/load failure falls back automatically to `.wld` and records a machine-readable miss reason.
- [x] Saving completes the canonical `.wld` first, then schedules a coalesced runtime-image rebuild.
- [x] Shutdown may wait for the final runtime-image rebuild so the next boot gets the newest cache.

### Performance gate

Measure independently:

- [ ] file read;
- [ ] tile reconstruction;
- [ ] liquids/post-load initialization;
- [ ] index construction;
- [ ] cache validation;
- [ ] `WorldReady`;
- [ ] `NetworkReady`;
- [ ] allocations and GC deltas.

Vega already demonstrated an important lesson: caching only serialized/tile data can miss the real startup bottleneck, while caching safe post-load runtime state can materially reduce startup. TerraRuntime should design the image around measured post-load work from the beginning.

## Phase 6 - Gameplay parity

Implement observable behavior subsystem by subsystem using the official server plus independent implementations such as terrustia as behavioral references, never as blindly copied architecture.

Priority:

1. [ ] handshake and spawn flow;
2. [ ] player state and movement;
3. [ ] inventory/items;
4. [ ] tile manipulation and sections;
5. [ ] chests/signs/tile entities;
6. [ ] projectiles and combat;
7. [ ] NPC lifecycle and spawning;
8. [ ] NPC AI;
9. [ ] bosses;
10. [ ] drops;
11. [ ] housing/town NPC behavior;
12. [ ] invasions/events;
13. [ ] wiring/liquids;
14. [ ] world progression;
15. [ ] world generation.

Current boss verticals now include Deerclops `AI_123`: authoritative state transitions, world collision/snow queries, projectile IDs `961/962/965`, source-ordered Classic/Expert/Master loot and persistent `downedDeerclops`. Full Deerclops parity remains intentionally false until player `Slow` buff application and Expert passive `playerInteraction[]`-selected shadow hands have an authoritative runtime owner.

For every subsystem:

- [ ] document observable vanilla behavior;
- [ ] add deterministic unit tests;
- [ ] add integration tests with a real Terraria client/bot where practical;
- [ ] maintain explicit known divergences;
- [ ] do not mark work complete merely because it compiles or resembles decompiled code.

## Phase 7 - Server-authoritative validation

Close exploit classes inherited from client-trusting designs without turning the server into a false-positive anti-cheat machine.

### Identity/session

- [x] Ignore client-claimed player slot where server ownership is already known.
- [x] Validate packet legality against the connection state machine.
- [x] Reject impossible pre-handshake and pre-spawn operations.

### Movement

- [ ] Keep server-known position/velocity history.
- [ ] Validate exceptional movement through explicit server-known states such as teleport, mount or respawn.
- [ ] Keep tolerances compatible with real network jitter and vanilla behavior.

### Inventory/items

- [ ] Server owns world item identity and authoritative item slots.
- [ ] Validate pickup distance, ownership, stack bounds and legal transitions.
- [ ] Never accept arbitrary client item metadata without validation.

### Tiles/world edits

- [x] Bounds-check coordinates before indexing.
- [ ] Validate action type and required world state.
- [ ] Use per-player edit budgets based on vanilla-compatible ceilings.
- [ ] Guard against overflow and oversized area operations.

### NPC/projectile/combat

- [x] Server owns entity identity and lifecycle.
- [ ] Validate targets against live entities across every combat source. The verified direct-melee slice and the admitted trusted-projectile slice already use generation-safe live NPC geometry; PvP must use the same generation-safe player identity and server-owned health/combat state rather than trusting packet 117. Unsupported legacy combat keeps an explicit compatibility fallback until its formula is imported.
- [x] Internally use generation/revision handles so stale slot reuse cannot mutate a different entity.

### Combat integrity

> **One gameplay formula. Anti-cheat must not maintain a separate copy of damage, ammo selection, projectile provenance, velocity, crit, or combat timing rules.**

Combat integrity is not an external packet-sniffing `AntiCheat`. The intended ownership chain is `AuthoritativeCombatCalculator -> CombatValidator -> world mutation`, so normal combat and cheat resistance cannot drift into two competing gameplay implementations. Client `damage`, `crit`, projectile velocity and other combat fields become hints/diagnostics only as source-backed coverage expands.

#### Authoritative item use

- [ ] Apply the same authoritative item-use calculation to PvE and PvP. Tools are combat sources too: pickaxes, axes and hammers with vanilla damage must pass through the same damage/cadence/range rules as swords rather than bypassing combat integrity.
- [ ] Server determines the active weapon from authoritative inventory/equipment state.
- [ ] Validate `useTime`, `useAnimation`, cooldowns and legal attack cadence using the same gameplay facts that drive normal item use. The strict direct-melee slice now enforces player-global `useTime` while allowing one swing to hit multiple targets on the same tick, plus per-target animation cadence.
- [ ] Client-provided `damage` / `crit` are never a source of truth on fully authoritative combat paths.
- [ ] Validate weapon ownership and selected inventory slot for every combat family. The strict direct-melee slice already resolves the server-owned selected inventory item; projectile weapon/ammo source mapping remains open.
- [ ] Reject impossible item-use cadence before projectile spawn or world mutation.

#### Authoritative ammo selection

- [ ] Implement a server-side vanilla-equivalent `PickAmmo` path.
- [ ] Match vanilla priority across dedicated ammo slots and normal inventory.
- [ ] Validate weapon/ammo compatibility through authoritative `useAmmo` / `ammo` facts.
- [ ] Apply ammo `shootSpeed`, damage and knockback contributions server-side.
- [ ] Apply ammo-specific projectile type transformations server-side.
- [ ] Apply ammo-conservation effects using server-owned RNG/state.
- [ ] Consume/decrement ammo only on the server and replicate the resulting inventory mutation.
- [ ] Differential-test `PickAmmo` against TerrariaServer 1.4.5.8, including conservation and unusual ammo transforms.

Checkpoint slice: strict projectile provenance now follows the source-backed `PickAmmo` search order `coin -> ammo -> main inventory`, rejects an unsupported earlier compatible candidate instead of skipping ahead, and owns ammo decrement. The admitted ammo paths are Wooden/Iron/Copper/Tin/Lead/Silver/Tungsten/Gold/Platinum Bow with Wooden/Flaming/Unholy/Jester Arrow; Flintlock Pistol, Musket, Minishark, Handgun, The Undertaker and Revolver with Musket Ball/Silver Bullet; plus Grenade Launcher, Rocket Launcher and Proximity Mine Launcher with Rocket I-IV. Prefix-free Magic Missile, Flamelash and Rainbow Rod are also admitted as source-backed channeled magic projectile sources with server-owned damage, launch magnitude, cadence and mana consumption. The launcher path reproduces vanilla `AmmoID.Rocket` base-projectile-plus-ammo-offset `PickAmmo` transformation, so the three launchers resolve to projectile IDs 133..144 without trusting packet 27. Silver Bullet transformation, bullet/rocket damage/knockback/speed, ammo consumption and Minishark's 1-in-3 conservation are server-owned; Magic Quiver's arrow damage/speed and 20% conservation remain modeled. Shuriken, Bone, Throwing Knife, Poisoned Knife, Rotten Egg, Star Anise and Bone Dagger are separately admitted as selected-stack standalone projectile sources. Later rocket-ammo variants (Dry/Wet/Lava/Honey and related 1.4.5.8 entries), other ammo families, transforms and conservation sources remain open and fail closed for combat trust.

#### Projectile spawn validation

- [ ] Validate legal `ProjectileType` for the authoritative weapon/ammo/use path.
- [ ] Track and validate full `Weapon -> Ammo -> Projectile` provenance.
- [ ] Validate spawn position against the authoritative player/item-use geometry.
- [ ] Validate initial projectile velocity for every player-owned projectile before it can damage NPCs or players.
- [ ] Validate projectile damage for both NPC and PvP targets.
- [ ] Validate projectile knockback.
- [ ] Validate `ai[]` and special spawn parameters for source-backed weapon families.
- [ ] Validate projectile count per item use, including multishot/special weapons.
- [ ] Promote a client packet-27 projectile into authoritative combat only after provenance/spawn validation succeeds; unverified client spawns remain diagnostic/compatibility state and cannot damage authoritative entities.

#### Projectile velocity-magnitude envelope

- [ ] Server computes the legal launch-speed magnitude interval `[MinLaunchSpeed, MaxLaunchSpeed]` for every authoritative player-owned projectile. Combat integrity does not require generic angular/aim validation; weapon-specific direction mechanics may be modeled by gameplay code only where they are required for vanilla behavior.
- [ ] Calculate the base interval from source-backed weapon `shootSpeed` plus authoritative ammo `shootSpeed` / `PickAmmo` semantics. Deterministic weapons may collapse the interval to `MinLaunchSpeed == MaxLaunchSpeed`.
- [ ] Apply prefix, buff/debuff, armor/set-bonus, accessory and class speed modifiers from authoritative player state before deriving the final interval.
- [ ] Apply weapon-specific speed modifiers and exceptional launch mechanics.
- [ ] Expand the interval only for source-backed vanilla RNG/speed variance; do not add arbitrary anti-cheat tolerance that can mask forged velocity.
- [ ] Validate `|velocity|` against `[MinLaunchSpeed, MaxLaunchSpeed]` before the projectile can become combat-trusted.
- [ ] Add specialized magnitude-envelope calculators for weapons whose launch-speed mechanics cannot be represented by the generic weapon+ammo path.
- [ ] Record expected min/max launch speed, received speed magnitude and all authoritative modifier inputs in combat diagnostics.
- [ ] Hard-reject impossible launch-speed magnitude before the projectile enters authoritative combat state.

Checkpoint slice: admitted bow/arrow and basic gun/bullet generations now derive a real `VanillaLaunchSpeedEnvelope` from authoritative weapon `shootSpeed`, ammo `shootSpeed`, supported ranged prefixes and the represented class modifiers; Magic Quiver applies only to the admitted arrow family. The admitted standalone thrown sources use source-backed fixed launch magnitudes. Deterministic combinations collapse to one magnitude with only a small floating-point/network representation epsilon in `ContainsMagnitude`; there is no generic angular envelope. Full buff/debuff, armor/set, accessory and special-weapon speed coverage remains open.

#### Authoritative projectile simulation

- [x] After a projectile generation is marked combat-trusted, client position/velocity updates are never authoritative for its simulation.
- [ ] Server simulates acceleration, gravity, drag, homing, bouncing and source-backed projectile AI.
- [ ] Client projectile position/velocity updates may be retained for diagnostics/divergence metrics only.
- [x] Combat-trusted generations reject owner packet-27 state rewrites (`type`, damage, position/velocity, ownership and related authoritative fields) and packet-29 early termination. The only current packet-27 `ai[]` exception is the explicit controlled-magic intent channel: trusted Magic Missile/Flamelash/Rainbow Rod may submit bounded cursor target `ai[0]/ai[1]`, but the server still owns projectile state and movement.
Checkpoint slice: Magic Missile (16), Flamelash (34) and Rainbow Rod (79) now form the authoritative modern aiStyle-9 client-steered projectile channel. Strict spawn validation derives magic damage from server-owned equipment state, validates the exact vanilla launch magnitude, requires zero initial ai state, consumes mana on the server, and generation-scopes combat trust. During channeling, matching owner packet 27 traffic cannot move or accelerate the projectile directly; only `ai[0]/ai[1]` cursor intent is accepted and clamped to the source-backed 1920x1200 player-reachable area. Packet-13 `controlUseItem` and the authoritative held item drive release, after which aiStyle-9 movement is server-simulated. Release now performs the vanilla 800-pixel nearest-NPC line-of-sight acquisition in physical slot order for the currently modeled chaseable candidate set and homes against server-owned NPC centers. Flamelash's two-hit/local-12 immunity and Rainbow Rod's three-hit/local-12 immunity are represented; Rainbow Rod also keeps the source-backed channelled tile-contact damping instead of dying on first collision. Remaining gaps include localNPCImmunity-aware post-hit target reacquisition plus exact transient `friendly/chaseable/immortal` NPC-flag parity, full mana-cost modifier/refill authority, the legacy Flying Knife aiStyle-9 intent model, later controlled families and presentation/localAI-only details.

- [ ] Complete vanilla projectile AI families. `BasicArrow`, `Thrown`, source-backed Enchanted Boomerang type 6, launcher `Bomb` aiStyle-16 types 133..144, controlled aiStyle-9 Magic Missile/Flamelash/Rainbow Rod, the admitted Skeletron/Deerclops families and other existing source-backed slices remain explicit profiles. The server-owned hostile slice now also covers straight no-gravity AI_001 beams for Wall of Flesh/Probe/Retinazer/Golem (83/84/100/259), Plantera Seed/Poison Seed (275/276) including the Expert-mode homing/minimum-speed/tile-collision/lifetime rules, and Golem Fireball (258) aiStyle-8 free flight plus its four bounces/fifth-impact termination. Known unsupported families still fail closed.
- [ ] Complete projectile ownership/source validation. Connection ownership is enforced, combat-trusted generations are immutable to client packet 27/29 state mutation, and strict source-backed packet-27 promotion now covers the admitted bow/arrow, early gun/bullet, Rocket I-IV launcher, standalone thrown and prefix-free Magic Missile/Flamelash/Rainbow Rod families. Controlled magic consumes packet-27 cursor `ai[0]/ai[1]` only as validated intent and uses packet-13 `controlUseItem` plus the authoritative selected item for release. Unsupported projectile sources remain compatibility/diagnostic state and cannot enter authoritative combat.
- [ ] Complete entity collision. Tile/world collision already exists. Deterministic post-simulation passes now select generation-safe NPC and player AABBs for trusted admitted friendly projectiles; PvP applies hostility/team gating and a source-backed 40-tick projectile-local player immunity window. Launcher types 133..144 now use aiStyle-16 grenade/mine bounce or straight-rocket impact arming and authoritative 128x128/200x200 on-kill explosion AABBs. Exceptional hitboxes, explosive self-hurt/owner-hit rules and type-specific target exceptions remain open.
- [ ] Buff/debuff application from projectile hits.
- [x] Source-backed projectile lifetime remains runtime-owned rather than client-owned.
- [ ] Complete spawn ordering. Physical projectile-slot then NPC-slot hit ordering is deterministic and committed damage precedes penetration, but child-projectile/on-hit spawn ordering is not complete.
- [ ] Complete NPC/projectile side effects. Trusted admitted hits now commit server-resolved NPC/PvP damage, consume source-backed penetration, apply ordinary shared owner/NPC immunity for admitted multi-hit families, apply permanent local NPC immunity for grenade-launcher variants 133/136/139/142, apply Flamelash/Rainbow Rod's source-backed 12-tick projectile-local NPC immunity, and apply the source-backed 40-tick projectile/player immunity baseline in PvP. Launcher Kill() damage is replayed from a generation-safe same-tick termination handoff after the live projectile is removed. Buffs/debuffs, remaining local/static immunity variants, explosive self-hurt, rocket world/tile-destruction side effects, child spawns and other type-specific on-hit/on-kill effects remain open.

#### Authoritative damage calculation

- [ ] Server-authoritative damage calculation for every player combat source and target class (PvE + PvP). The first strict direct-melee slice covers source-backed Muramasa and Copper Pickaxe facts; Copper Pickaxe is deliberately included to prevent tools from becoming an unvalidated PvP damage bypass. Wire damage/crit are diagnostics only on accepted strict paths.
- [ ] Use one explicit pipeline: `AttackContext -> AuthoritativeAttackDamage -> TargetMitigation -> FinalDamageToHp`. Client-reported final damage is never reused as the result on authoritative paths.
- [ ] `AttackContext` is built entirely from server-owned attacker state: selected weapon/tool, ammo, item/ammo prefixes, armor and set bonuses, accessories, buffs/debuffs, class modifiers, armor penetration, world/difficulty state and source-specific mechanics.
- [ ] Include weapon + ammo contributions.
- [ ] Include item/ammo prefixes.
- [ ] Include attacker armor/set bonuses and accessories.
- [ ] Include attacker buffs/debuffs and class modifiers.
- [ ] Calculate crit server-side for every weapon/projectile family. The verified direct-melee slice already rolls crit server-side.
- [ ] Include armor penetration and other source-backed offensive modifiers.
- [ ] Apply `TargetMitigation` from authoritative target state after attack damage is resolved.
- [ ] For NPC targets include NPC defense, damage-reduction/defense modifiers, buffs/debuffs, immunity state and source-backed exceptional mechanics.
- [ ] For PvP player targets include equipped armor, armor/set bonuses, accessories, defense, endurance/damage reduction, buffs/debuffs, dodge/avoidance, immunity frames and PvP-specific vanilla rules; never reuse NPC defense math blindly for players.
- [ ] Include difficulty/world-state modifiers at the vanilla stage where they actually apply.
- [ ] Include vanilla damage variance using server-owned RNG.
- [ ] Implement source-backed special weapon/projectile damage mechanics without a parallel anti-cheat formula.
- [ ] Damage envelopes must be derived from the same attacker/target gameplay facts used to compute real damage, not from static anti-cheat constants. Source-backed prefix multipliers are imported; full armor/accessory/player-buff/target-mitigation coverage remains intentionally incomplete and must fail closed/fall back rather than be guessed.
- [ ] Validate NPC/player hit target and range across all hit shapes. Strict direct melee has a conservative impossible-distance guard; PvP direct melee must apply the same item-use geometry against player hitboxes, and trusted admitted projectiles must collide server-side with both NPC and hostile legal player targets.
- [ ] Reject impossible damage before world mutation across every combat path. The strict calculator/validator path already rejects before interaction/HP/loot/replication mutation; unsupported legacy combat remains the blocker.

Checkpoint slice: the shared contracts now explicitly represent `AttackContext -> AuthoritativeAttackDamage -> TargetMitigation -> FinalDamageToHp`. Direct melee/tools (Copper Pickaxe/Axe/Hammer/Broadsword and Muramasa) consume an authoritative attacker equipment snapshot. The modeled equipment subset includes the Copper armor set, Cobalt Shield, Warrior/Ranger/Sorcerer Emblem, Magic Quiver, Shark Tooth Necklace and source-backed combat accessory prefixes; unknown active combat equipment fails closed. PvP target mitigation now consumes the equipped target snapshot and uses source-backed Classic/Expert/Master player-defense effectiveness plus endurance/no-knockback semantics. Player buffs/debuffs, dodge and most armor/accessory families remain open, so those combinations do not become `CombatTrusted`.

#### Combat envelopes

- [ ] `MaxDamagePerHit` derived from authoritative gameplay state rather than a static anti-cheat constant.
- [ ] Validate whether a crit is possible for the active weapon/source/state.
- [ ] `MaxHitsPerSecond` / legal hit cadence per weapon/projectile family. Strict direct melee has a player-global `useTime` gate plus per-target animation gate; admitted strict packet-27 projectile sources now also enforce source-backed weapon/useTime cadence before promotion. Remaining projectile families and multi-projectile-per-use rules stay open.
- [ ] `MaxProjectilesPerUse`.
- [ ] `MaxProjectilesPerSecond`.
- [x] Sliding-window DPS ceiling for the verified direct-melee path.
- [ ] Extend `MaxDps` envelopes to authoritative windows such as 1 / 5 / 10 seconds and projectile combat.

#### Hard rejection

Impossible combat events must not be applied to authoritative world state merely because they were well-formed packets.

- [ ] Reject impossible projectile type/source.
- [ ] Reject impossible/incompatible ammo.
- [ ] Reject impossible damage.
- [ ] Reject impossible initial projectile speed magnitude outside the authoritative `[MinLaunchSpeed, MaxLaunchSpeed]` interval.
- [ ] Reject impossible attack cadence/cooldown state. Strict direct melee rejects cross-target cadence bypass before mutation, and admitted strict projectile sources reject same-player packet-27 spawn cadence before promotion. Remaining weapon families and multi-shot rules are open.
- [ ] Reject projectiles without legal authoritative provenance.
- [ ] Reject hits against impossible NPC or PvP player targets, friendly/team-protected players, or targets at impossible range.
- [ ] Perform rejection before HP, inventory/ammo, buffs, loot, projectile side effects or replication mutate authoritative state.

#### Anomaly detection

Anomaly detection is a second-line diagnostic layer. It must never replace hard authoritative rejection of physically impossible combat.

- [x] Statistical crit/damage-roll anomaly detection for strict-path client claims; this is diagnostic evidence, not the source of authoritative damage.
- [ ] Detect anomalous crit rate across meaningful sample windows.
- [ ] Detect suspicious damage RNG distributions / constant max-roll behavior.
- [ ] Detect unusual DPS patterns that remain technically inside individual-hit limits.
- [x] Suspicion score with tick-based decay for strict-path rejects/anomalies.
- [ ] Extend `SuspicionScore` evidence to ammo, projectile provenance, velocity and cadence anomalies without allowing score alone to mutate gameplay.

#### Diagnostics

- [x] Bounded diagnostics ring explaining every strict-path rejected hit before mutation.
- [x] Record exact strict-path rejected-attack reason/code.
- [ ] Record expected/received damage and relevant envelope inputs.
- [ ] Record authoritative min/max launch speed, received speed magnitude and the modifier inputs that produced the interval.
- [ ] Record weapon/ammo/projectile IDs and provenance chain.
- [x] Strict direct-melee diagnostics record tick, generation-safe player identity and target NPC generation; projectile-generation diagnostics remain open.
- [ ] Allow bounded verbose combat audit for a selected player without enabling global packet spam.

#### Parity and exploit regression tests

- [ ] Differential combat tests against TerrariaServer 1.4.5.8.
- [ ] Differential `PickAmmo` tests against TerrariaServer 1.4.5.8.
- [ ] Projectile spawn parity tests.
- [ ] Projectile initial speed-magnitude envelope parity tests, including deterministic and vanilla-RNG speed ranges.
- [ ] Damage parity tests, including defense, crit, armor penetration and variance.
- [ ] Weapon cadence parity tests.
- [ ] Dedicated tests for weapons with unusual projectile count, transforms, velocity or AI parameters.
- [ ] Maintain a regression corpus for known exploit classes: forged damage, forged crit, impossible ammo/projectile, velocity hacks, cadence hacks, provenance spoofing, impossible range and projectile state rewrites.

## Phase 8 - Synchronization and scalability

Preserve observable results, not inefficient vanilla broadcast mechanics.

- [x] Movement-driven packet-10 tile-section streaming keeps a bounded 5x3 world window around each playing client beyond the initial spawn bootstrap; per-connection sent-section state prevents redundant retransmission.
- [ ] Section-aware player visibility/interest sets.
- [ ] Dirty-state-driven NPC/projectile/item synchronization.
- [ ] Skip updates for clients that cannot observe an entity, with a bounded forced-resync interval so distant entities never freeze forever.
- [ ] Apply the same visibility logic to movement relay where compatible instead of unconditional O(players²) broadcast.
- [ ] Encode one immutable frame once and share it among recipient queues when the bytes are identical.
- [ ] Explicit delta/full-sync policy with resync deadlines.
- [ ] Compare low-frequency progress/state packets against the last transmitted value before sending unchanged data every tick.
- [ ] Build expensive per-tick roster/spatial summaries lazily once per tick and only if a subsystem actually asks for them.

### Runtime-owned interest management

Interest management belongs to TerraRuntime, not Vega or another host layer.

- [x] External hosts receive only the narrow world-scoped `IInterestManagementControl` toggle (`IsEnabled` / `SetEnabled(bool)`).
- [x] The standalone host accepts `--interest-management`; the same mechanism can be switched at runtime through the control interface without restarting the world.
- [x] Spatial policy, cell/section layout, radii, hysteresis, full-resync rules and entity-specific routing remain internal TerraRuntime details.
- [x] Disabling interest management is fail-open and restores vanilla-like global recipient selection.
- [x] Current foundation tracks authoritative player positions in a section-based compact bitset index on spawn, movement and disconnect.
- [ ] Enabling the feature currently uses a passthrough policy. Actual packet suppression must remain disabled until enter/leave transitions, full state on entry, out-of-range semantics and forced resync are implemented and live-tested.
- [x] Invalid/non-finite/out-of-world positions are removed from spatial membership so later visibility logic can fail open rather than leave a player stuck in a stale cell.
- [ ] Initial player-player policy should use hysteresis, for example a smaller enter radius and larger leave radius, to avoid boundary oscillation.
- [ ] Teleport, respawn and slot reuse must recalculate/clear visibility immediately.

### Join pipeline

A player joining must not freeze everybody already online.

- [ ] Create a staged join state machine.
- [ ] Spread first-time uncached section generation/compression across multiple ticks under an explicit **global subsystem budget**, not a full budget per joining player.
- [ ] Prioritize the minimum sections/state required to enter the world.
- [ ] Continue streaming remaining interest data after initial spawn where protocol behavior allows it.
- [ ] Keep outbound queue growth bounded during join bursts.

Benchmark 1, 8, 24, 64, 128 and 255 connections. Any optimization that changes player-visible vanilla behavior requires an explicit compatibility decision.

## Phase 9 - CPU, memory and GC optimization

After correctness baselines exist:

- [ ] Profile allocations per tick and packet type.
- [ ] Keep common movement/control packet processing allocation-free where practical.
- [ ] Replace hot heap objects with structs only when lifetime and copying costs justify it.
- [ ] Pool compression and large temporary buffers only after measuring real benefit; revert experiments that increase RSS or paging cost.
- [ ] Cache immutable encoded packets only with explicit invalidation.
- [ ] Avoid unnecessary async state machines inside the core tick.
- [ ] Use source-generated logging/serialization on hot paths where beneficial.
- [ ] Measure allocation rate, collection counts, pause time and heap size on .NET 11 where the runtime exposes them safely under NativeAOT.
- [ ] Tune GC settings only from production-like native benchmarks rather than folklore.
- [ ] `GC.TryStartNoGCRegion` is not a baseline architecture assumption.
- [ ] `unsafe` requires a benchmark demonstrating material benefit plus focused correctness tests.

### Runtime specialization

Main shipping server runtime:

- [x] .NET 11 NativeAOT;
- [x] `linux-x64` and `win-x64` are exercised production targets;
- [ ] zero unexplained trim/AOT warnings;
- [x] native executable startup plus exercised loop/protocol/network/world smoke paths are required in CI;
- [ ] Server GC and other GC settings are changed only from compatible production-like benchmarks;
- [ ] no tiered-JIT, dynamic-PGO or ReadyToRun assumption may enter production architecture because the shipping process has no JIT requirement.

CoreCLR remains useful for development-only debugging/profiling experiments, but a change that only works under CoreCLR is not considered production-compatible.

## Phase 10 - Operations API and Terminal UI

Borrow Vega's operations boundary instead of wiring a terminal toolkit into the simulation.

```mermaid
flowchart TD
    Runtime["Game runtime"] --> Ops["Immutable operations snapshots + safe commands"]
    Ops --> TUI["Terminal.Gui"]
    Ops --> Console["Plain console"]
    Ops --> Future["Future web / API"]
```

- [x] Operations read models are immutable/bounded projections.
- [x] TUI must never traverse mutable world/player/NPC collections.
- [x] TUI has its own event loop/thread.
- [x] Game-state administrative mutations are marshalled back through the same game-loop command boundary used by other control surfaces; host-network endpoint replacement stays outside authoritative game state behind `ListenerManager`.
- [x] TUI failure is not a server-readiness failure; support interactive, plain-console and headless modes independently.
- [x] Use Terminal.Gui v2 as the first UI implementation, but keep core contracts toolkit-independent.
- [x] Dashboard: lifecycle, world, TPS/tick phase, players, queues, packet rates, memory/GC, save/cache state, warnings and recent structured logs.
- [x] `ListenerManager` separates listening generations from accepted client lifetime; live bind-address/port replacement uses `Active -> Draining -> Closed` and is exposed through bounded dashboard settings without disconnecting accepted clients.
- [x] Logs viewer uses bounded retention, filters and follow/pause without blocking telemetry refresh.

## Phase 11 - Observability and performance discipline

- [ ] Structured logging from the start.
- [ ] Stable event IDs/categories.
- [x] Tick CPU/wall duration and worst phase.
- [x] Command processed/deferred counts, budget exhaustion count and oldest pending command age.
- [x] Queue depth and slow-client drops, including the packet type that filled the queue.
- [x] Packet counts/bytes by message ID and direction.
- [x] Invalid/malformed/rejected packet counters.
- [x] Active players/NPCs/projectiles/items.
- [x] Spatial-index membership/section changes/invalid-position counters.
- [x] Save snapshot duration, serialization duration and write duration separately.
- [x] World-cache hit/miss/invalidation reason and load/build times.
- [ ] GC allocation rate, collections, pause time and heap size where available.

Telemetry must not add heavy formatting/allocation to every hot-path operation.

Every performance hypothesis should have a reproducible benchmark/harness. Failed optimizations should be reverted and the measurement/reason documented so the same attractive mistake is not repeated later.

## Phase 12 - Test strategy

### Unit tests

- [ ] packet codecs;
- [ ] flags/bit layouts;
- [ ] world math;
- [ ] AI state transitions;
- [ ] drops and probability rule structure;
- [ ] validation/rate limits;
- [ ] save/load components.

### Golden-byte tests

Pin critical packet layouts to known-good bytes instead of relying only on encode/decode round trips.

### Independent real-client capture/replay

Do not trust only tests where both the client and server use Multiplicity. A shared protocol mistake can make both sides agree on the same wrong bytes.

- [ ] Record raw bidirectional sessions from the official Terraria client.
- [ ] Preserve selected captures as small test fixtures.
- [ ] Replay captures in CI and verify framing consumes every byte exactly.
- [ ] Require the blocking join sequence to contain the expected critical packets.
- [ ] Track packet-id coverage of each capture so absence is never mistaken for proof.

### Differential tests

Drive equivalent scenarios against the official dedicated server and TerraRuntime and compare observable state/output.

### Real process stress tests

For networking/queue/scheduler behavior, prefer a real server subprocess and independent client process/load generator where in-process tests hide OS scheduling and backpressure behavior.

Maintain scenarios for:

- [ ] simultaneous joins;
- [ ] sustained movement relay;
- [ ] slow readers;
- [ ] large section/chest bursts;
- [ ] save during player load;
- [ ] 24-player normal load and 255-connection stress.

### Real-client integration

Maintain bots/scripts for:

- [ ] handshake/join;
- [ ] movement/inventory/tile edits;
- [ ] chest/sign interaction;
- [ ] boss progression;
- [ ] events;
- [ ] save/restart;
- [ ] long-running soak tests.

### Fuzzing

- [ ] frame parsing;
- [ ] all variable-length packet decoders;
- [ ] section/tile decompression;
- [ ] `.wld` parsing;
- [ ] command/text parsing.

A fuzz scenario should be able to throw a large malformed corpus at a running server and then prove the server still accepts a valid connection afterwards.

### Crash/exception budget

Pin dangerous fail-fast assumptions in tests rather than prose. Production network/gameplay paths should return explicit errors or isolate failures; any intentional invariant-throw site must be reviewable and counted so the number cannot silently grow.

## Phase 13 - World generation

Worldgen comes last because it is huge, RNG-order-sensitive and unnecessary for proving the runtime architecture.

- [x] First load existing vanilla worlds correctly.
- [ ] Port worldgen pass by pass.
- [ ] Treat RNG stream compatibility as explicit behavior.
- [ ] Keep statistical generated-world tests plus selected deterministic seeds.
- [ ] Profile worldgen by CPU time per pass as well as wall time.
- [ ] Do not parallelize pass-level work sharing Terraria's order-sensitive RNG stream.
- [ ] For genuinely independent intra-pass work, use read/compute on workers and deterministic apply on the owner thread, then prove bit-identical output against the single-thread reference.
- [ ] Do not let worldgen block protocol, runtime or gameplay replacement.

## Performance acceptance direction

Concrete targets will be replaced by measurements on defined hardware. See [`roadmap/performance-tick-stability.md`](roadmap/performance-tick-stability.md) for the detailed gates and stress matrix.

- Common movement/control packets should avoid avoidable heap allocations.
- Typical ticks must stay comfortably below the 16.67 ms budget with room for spikes.
- Idle CPU should approach sleeping cost rather than burn an entire core.
- Ordinary sync must not scan the entire world.
- No unbounded queue/allocation may be controlled by a client.
- Joining a player must not stall simulation while sections are compressed or written.
- Autosave must not perform disk serialization on the authoritative game thread.
- Fast-start cache must always have a safe `.wld` fallback and must demonstrate an end-to-end `WorldReady`/`NetworkReady` win before default enablement.
- 24-player realistic workload is the first meaningful optimization baseline; 255 connections are a stress/scalability target.

## Non-goals

- Source or binary compatibility with original Terraria server internals.
- Reproducing private class layouts or global-state architecture.
- Keeping Mono or obsolete .NET compatibility baggage.
- Premature ECS conversion because it is fashionable.
- Parallelizing every subsystem.
- Copying decompiled method bodies into the clean implementation.
- Trading vanilla-visible behavior for benchmark numbers without documenting the divergence.
- Introducing IPC or worker processes solely to advertise NativeAOT.

## First milestone

```mermaid
flowchart TD
    Client["Official Terraria client"] --> Runtime["TerraRuntime<br/>.NET 11 NativeAOT"]
    Runtime --> Handshake["Multiplicity-backed typed handshake"]
    Runtime --> Slot["Player slot assignment"]
    Runtime --> World["World metadata"]
    Runtime --> Sections["Section request / response"]
    Runtime --> Spawn["Spawn"]
    Runtime --> Movement["Movement relay"]
    Runtime --> Disconnect["Clean disconnect"]
```

Completion requires a real client to join an existing vanilla world, move, receive nearby world state and disconnect cleanly. The same build must survive malformed frame tests without crashing or allocating unbounded memory.

## Continuous bilingual documentation

Detailed documentation work is tracked in [`roadmap/documentation.md`](roadmap/documentation.md).

Documentation is a permanent parallel workstream, not a final cleanup phase after code completion.

- [x] Maintain first-class documentation trees under `docs/ru/` and `docs/en/`.
- [x] Provide bilingual project guides describing build, startup, runtime operation, worlds, persistence, gameplay boundaries and operational behavior.
- [x] Provide bilingual architecture documentation describing ownership, threading, data flow, NativeAOT/CoreCLR profiles, network/protocol boundaries, persistence and extension boundaries.
- [x] Provide bilingual host-interface documentation with lifecycle, status/error semantics and interaction examples.
- [x] Require code changes that alter behavior, architecture, public contracts, CLI/deployment, persistence, lifecycle or supported scope to update both language versions in the same change.
- [x] Treat code/documentation mismatch as an incomplete change in repository agent rules and Definition of Done.
- [x] Expand dedicated bilingual subsystem guides for protocol/networking, world persistence/cache, gameplay, synchronization, operations/TUI, worldgen and security.
- [x] Validate repository-local documentation links in CI.
- [x] Validate required RU/EN mirrored page sets in CI without requiring line-by-line translation equivalence.

For every new significant subsystem, documentation must describe purpose, owner, inputs/outputs, lifecycle, public integration surface, safety/failure semantics, observable behavior, known limitations and verification evidence. Roadmap target design must remain visibly distinct from behavior that is already implemented.
