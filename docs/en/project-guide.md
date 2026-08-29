# TerraRuntime project guide

[Русский](../ru/project-guide.md) · [Documentation](README.md) · [Architecture](architecture.md) · [Host interfaces](host-interfaces.md) · [Roadmap](../roadmap.md)

## 1. What TerraRuntime is

TerraRuntime is a clean-room Terraria server runtime for .NET 11. It targets observable parity with the official 1.4.5.8 dedicated server while using a different internal architecture built around explicit state ownership, bounded work, testable boundaries, and gameplay code that is independent from socket transport details.

The governing principle is:

> Preserve vanilla-visible behavior; internal implementation may change when the observable contract remains intact.

TerraRuntime is not a fork of TerrariaServer and does not host TerrariaServer runtime objects. The official server is used as the behavioral source of truth and as a differential reference. Protocol 326 / Terraria 1.4.5.8 is modeled through Multiplicity behind TerraRuntime's own protocol boundary.

## 2. Shipping profiles

The project deliberately supports two different shipping profiles.

### Standalone NativeAOT

`TerraRuntime.Server` is the standalone server executable. The runtime core must remain NativeAOT-compatible; Linux x64 and Windows x64 publish/smoke jobs are mandatory CI gates.

The NativeAOT profile does not load arbitrary managed DLL plugins.

### Extensible CoreCLR

`TerraRuntime.Extensible.Server` is a self-contained CoreCLR host for a trusted host module, primarily Vega. It preserves the same runtime ownership rules while exposing a narrow privileged `TerraRuntime.HostContracts` surface.

Ordinary Vega plugins do not receive TerraRuntime implementation objects. The boundary is:

```text
TerraRuntime implementation
        |
        v
TerraRuntime.HostContracts
        |
        v
trusted host module (Vega)
        |
        v
Vega.PluginSdk / ordinary plugins
```

## 3. Repository map

| Path | Responsibility |
|---|---|
| `src/TerraRuntime` | standalone composition root, startup, gameplay/network/world composition, TUI |
| `src/TerraRuntime.ExtensibleHost` | CoreCLR host, trusted host module loading, host environment |
| `src/TerraRuntime.HostContracts` | narrow public surface for trusted host modules |
| `src/TerraRuntime.Contracts` | stable runtime/gameplay snapshots, IDs, and control contracts |
| `src/TerraRuntime.Core` | authoritative state, command execution, NPC/projectile/item/player systems, scheduling |
| `src/TerraRuntime.Network` | connection pipeline, frame ingress/egress, queues, and network contracts |
| `src/TerraRuntime.Protocol` | protocol boundary and shared codec/framing concepts |
| `src/TerraRuntime.Protocol.Multiplicity` | Multiplicity adapter behind the runtime protocol boundary |
| `src/TerraRuntime.Transport` | low-level transport primitives where separated from network policy |
| `src/TerraRuntime.World` | `.wld`, tiles, sections, world cache, collision, liquids, and persistence helpers |
| `tests/TerraRuntime.Tests` | unit, integration, and contract tests |
| `tests/TerraRuntime.HostModuleFixture` | extensible host/module boundary fixture |
| `tools/` | reference probes, world verification, and CI tooling |
| `docs/roadmap/` | detailed subsystem roadmaps |

## 4. Build

The SDK is pinned through `global.json`. The main solution is `TerraRuntime.slnx`.

A typical development cycle is:

```bash
dotnet restore TerraRuntime.slnx
dotnet build TerraRuntime.slnx -c Release
dotnet test TerraRuntime.slnx -c Release --no-build
```

A normal build is not sufficient for a shipping change. Runtime-core work must keep Linux/Windows NativeAOT publication and exercised smoke paths healthy.

## 5. Runtime layout

The standalone runtime treats the executable directory as its root and creates/uses:

```text
TerraRuntime.Server[.exe]
Worlds/
config/
data/
logs/
```

`Worlds/` is the canonical directory used by interactive world selection. An explicit `--world <path.wld>` may point outside that directory.

The extensible CoreCLR deployment adds dedicated trusted-host and plugin directories:

```text
TerraRuntime.Extensible.Server[.exe]
runtime/
HostModules/
ServerPlugins/
Worlds/
config/
data/
logs/
```

## 6. How a client connection is processed

The main data path is intentionally one-way with respect to ownership:

```text
TCP socket
  -> connection read loop
  -> bounded frame decoder
  -> protocol validation/decode
  -> owned typed command
  -> authoritative game-loop queue
  -> gameplay/state validation
  -> authoritative mutation
  -> immutable outbound event/snapshot
  -> recipient/sync planning
  -> encoded frame
  -> bounded per-client outbound queue
  -> socket writer
```

A network callback is not allowed to mutate world/player/NPC/projectile/item state directly.

Every received packet is untrusted input. Before authoritative mutation it passes framing/size checks, connection-state legality, and subsystem-specific validation as applicable.

## 7. Authoritative game loop

Mutable simulation state belongs to one dedicated game-loop thread.

The baseline simulation schedule is 60 Hz. The loop is not required to drain an unlimited inbound backlog in one tick: command processing uses a hard global cap, per-source fairness, and deferred-work telemetry.

A simplified tick is composed of phases such as:

```text
inbound commands
clock/events
world/liquids/growth
items
NPC AI
projectiles
combat
spawning
progression
visibility/sync planning
outbound snapshots
```

The exact phase set grows with gameplay parity. The invariant is more important than a frozen list: mutable state is changed by its owner, and blocking disk/network work does not run on the simulation hot path.

## 8. Player join and initial synchronization

The join flow follows vanilla protocol ordering. TerraRuntime assigns the server-owned player slot, advances the connection through legal handshake states, sends world metadata and sections, and then transitions the player into joined/spawned state.

Live CI probes use the real official TerrariaServer and officially generated `.wld` files as independent evidence. Critical join/movement behavior is checked separately from self-roundtrip tests, because an encoder and decoder can agree on the same protocol bug.

Section/bootstrap work remains bounded so one joining client cannot stop simulation for already connected players.

## 9. World and `.wld`

Terraria `.wld` remains the canonical persistent representation. A runtime cache is never the source of truth.

The world subsystem is responsible for, as parity grows:

- parsing and verifying supported `.wld` layouts;
- runtime tile/world representation;
- sections and section encoding;
- chests, signs, and tile entities;
- collision/world queries;
- liquid work;
- save snapshots;
- the derived `.runtime-world` cache.

Unknown or insufficiently verified file layouts are handled conservatively. Being able to read part of a file does not imply permission to rewrite it safely.

## 10. Runtime world cache

`.runtime-world` is a disposable derived image used to accelerate startup.

```text
world.wld            canonical
world.runtime-world  derived cache
```

The cache may contain prepared runtime state so expensive reconstruction does not need to repeat on every boot. Validation failure, corruption, or source/schema mismatch must fall back to `.wld`.

Corruption of a derived cache must never become corruption of the canonical world.

## 11. Saving

The save path separates a short authoritative snapshot/commit boundary from work outside the game loop.

Target flow:

```text
authoritative state
  -> bounded snapshot capture
  -> background serialization/write
  -> flush/validation
  -> atomic replace canonical .wld
  -> derived runtime cache rebuild
```

Only bounded/coalesced save work is allowed. Autosave must not build an unbounded queue.

TUI/operations can display save state, but the UI does not own save state and does not mutate the world directly.

## 12. NPCs, projectiles, and gameplay

Gameplay is being implemented subsystem by subsystem. The codebase already contains separate runtime stores, snapshots, definition catalogs, and state/AI steppers for part of NPC and projectile behavior.

Decomposition rules:

- packet IDs stay at the protocol boundary;
- content IDs become version-pinned domain concepts;
- live entity identity is separate from content type;
- AI/physics/combat does not encode packets directly;
- network replication is derived from authoritative state/events.

Full vanilla parity is not complete. Major remaining breadth includes NPC AI coverage, bosses, events, housing, loot, wiring/liquids, progression, and vanilla world generation. The roadmap is the status authority, not the existence of a class with a promising name.

## 13. Interest management

Interest management belongs to TerraRuntime. An external host receives only a narrow enable/disable control contract.

Spatial layout, hysteresis, resync policy, and recipient selection remain runtime implementation details. Disabling the mechanism must fail open toward vanilla-like broad recipient selection.

Packet suppression must not be enabled merely because a spatial index exists. Enter/leave semantics, full state on entry, and forced resync behavior must be proven first.

## 14. TUI and operations boundary

The terminal UI does not traverse mutable runtime collections. It consumes bounded immutable projections and sends administrative mutations back through a controlled command boundary.

Therefore a UI failure must not change world-state ownership or become a prerequisite for network readiness.

The extensible host can register independent dashboard providers through a host contract, but it cannot inject arbitrary controls into TerraRuntime's built-in system dashboard.

## 15. Trusted host modules

A trusted host module exists only in the CoreCLR profile. Its lifecycle is split into bootstrap and runtime attachment:

```text
load module
  -> StartAsync(environment)
  -> TerraRuntime starts authoritative runtime
  -> AttachRuntimeAsync(runtime contracts)
  -> normal operation
  -> DetachRuntimeAsync()
  -> StopAsync()
```

`ITerraRuntimeHostEnvironment` exposes deployment paths and registration surfaces that do not require a live world. `ITerraRuntimeHostRuntime` is attached later and exposes snapshots/controlled operations without mutable implementation state.

See [Host interfaces](host-interfaces.md) for details and examples.

## 16. World generation

The world-generation framework is separated from any one generator: registry → provider → validated plan → isolated workspace → final acceptance.

The current built-in generator is a deterministic flat baseline and does not claim vanilla WorldGen parity. This is deliberate: vanilla world generation is large and RNG-order-sensitive, so the extension architecture is developed separately from the eventual full port of vanilla passes.

A trusted host can register a custom generator through `ITerraRuntimeWorldGeneratorRegistry`; TerraRuntime remains responsible for plan validation, execution boundaries, and accepting the final world.

## 17. Errors and security

Normal network/gameplay failure should be local and bounded.

Architectural paths are forbidden when client-controlled input can:

- allocate unbounded memory;
- create an unbounded queue backlog;
- crash the server process through a decoder exception;
- trigger blocking expensive work without a budget;
- mutate state before connection/gameplay legality is validated.

Malformed protocol, rate limit, invalid state, and gameplay rejection should remain distinguishable categories instead of collapsing into one generic catch-all.

## 18. How compatibility is proven

TerraRuntime uses several independent evidence layers:

1. unit/contract tests;
2. golden packet/file facts;
3. officially generated `.wld` files;
4. official client/server captures;
5. live process probes;
6. differential checks against TerrariaServer 1.4.5.8;
7. Linux/Windows NativeAOT publish + smoke.

A green self-roundtrip without independent evidence is not sufficient proof of protocol/gameplay parity.

## 19. Project change rule

Update the matching RU/EN documents together with code. Minimum mapping:

| Change | Documentation |
|---|---|
| public host/runtime contract | `host-interfaces.md` and, when needed, `architecture.md` |
| lifecycle/ownership/threading | `architecture.md` + `project-guide.md` |
| CLI/deployment/startup | `project-guide.md` |
| persistence/cache/world format | `project-guide.md` + `architecture.md` |
| new gameplay subsystem boundary | `architecture.md` and the corresponding roadmap |
| new limitation/known divergence | user-facing guide + roadmap |

Documentation must describe implemented behavior and clearly separate it from target design.
