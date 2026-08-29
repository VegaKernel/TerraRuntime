# Runtime host and plugin architecture

This roadmap entry defines the production hosting model for TerraRuntime, Vega and third-party server plugins.

The design has three priorities, in this order:

1. sustained server throughput and low tick latency;
2. drop-in managed DLL plugins, including Vega itself as a DLL host module;
3. a strict API boundary so ordinary plugins cannot casually reach TerraRuntime internals or mutate authoritative state directly.

NativeAOT remains a first-class build and deployment target for TerraRuntime. The extensible plugin host deliberately uses CoreCLR because arbitrary managed DLL loading, collectible `AssemblyLoadContext` and hot replacement are part of that product profile.


> Checkbox policy: `[x]` means the item is verified on `main` by implementation plus tests/CI or an equivalent executable proof. Partial/foundation-only work remains `[ ]`.

## Two supported host profiles

TerraRuntime keeps one runtime core and two explicit host profiles instead of forcing incompatible requirements into one executable.

### Extensible production host

The normal Vega-enabled server runs on .NET 11 CoreCLR:

```text
TerraRuntime.Server[.exe]   CoreCLR
        |
        +-- TerraRuntime runtime/core
        +-- privileged host-module loader
        |
        +--> HostModules/
        |      +-- Vega.dll
        |
        +--> Vega-managed ServerPlugins/
               +-- AntiProxy.dll
               +-- Regions.dll
               +-- Administration.dll
               +-- other ordinary plugins
```

This profile is allowed to depend on JIT/runtime features that exist specifically to support the managed plugin model, but those dependencies must stay in the host/plugin boundary and must not leak into the AOT-compatible TerraRuntime core.

The default production compilation/runtime baseline for this profile is:

```xml
<ServerGarbageCollection>true</ServerGarbageCollection>
<TieredCompilation>true</TieredCompilation>
<TieredPGO>true</TieredPGO>
<PublishReadyToRun>true</PublishReadyToRun>
```

These are intentional defaults, not folklore-based permanent truths. Production-like benchmarks may justify changing them, but the burden of proof is on the change.

The performance model is:

```text
ReadyToRun
    -> reduces cold-start JIT work for precompiled methods

TieredCompilation
    -> starts methods at an appropriate tier

Dynamic PGO
    -> observes real server behavior and recompiles hot code with profile data

Server GC
    -> provides the baseline GC mode for the long-lived server workload
```

Hot-path acceptance is based on tick latency, CPU time, allocation rate, GC pauses, packet throughput and sustained player load. Startup time alone does not decide between CoreCLR and NativeAOT for the extensible server.

### NativeAOT standalone host

TerraRuntime must continue to build and ship as a NativeAOT standalone server profile:

```text
TerraRuntime.Native[.exe]
        |
        +-- TerraRuntime runtime/core
        +-- statically admitted AOT-safe dependencies
        +-- no arbitrary managed DLL plugin loading
```

The exact executable/project naming may evolve, but the architectural requirement does not: Linux x64 and Windows x64 NativeAOT publication, exercised native smoke tests and zero unexplained trimming/AOT warnings remain permanent CI gates for the TerraRuntime core.

NativeAOT is therefore not removed or demoted to a forgotten experiment. It remains:

- a deployable runtime-only server profile;
- an architectural quality gate for the core dependency graph;
- protection against accidental reflection/runtime-codegen creep in hot runtime code;
- a benchmark target against CoreCLR;
- an option for deployments that do not need managed plugins.

## Trust boundary

Vega is a DLL, but it is not treated as an ordinary untrusted server plugin.

The intended boundary is:

```text
TerraRuntime implementation
        |
        | privileged, narrow host API
        v
TerraRuntime.HostContracts
        |
        v
HostModules/Vega.dll
        |
        | restricted application/plugin API
        v
Vega.PluginSdk
        |
        +--> ServerPlugins/AntiProxy.dll
        +--> ServerPlugins/Regions.dll
        +--> ServerPlugins/Whatever.dll
```

`TerraRuntime.HostContracts` and `Vega.PluginSdk` are different boundaries with different trust levels.

### TerraRuntime.HostContracts

This is the privileged host-facing contract used by Vega or another explicitly trusted host module.

It may expose controlled capabilities required to build an application/server layer, for example:

- immutable player/world/entity snapshots;
- command ingress into the authoritative game loop;
- lifecycle events;
- runtime configuration/control surfaces;
- interest-management enable/disable control;
- safe world/player/NPC operations expressed as commands rather than mutable object references;
- bounded telemetry/operations snapshots.

It must not expose TerraRuntime implementation objects or mutable authoritative collections merely for convenience.

### Vega.PluginSdk

Ordinary server plugins compile against Vega's SDK, not TerraRuntime internals and not the privileged host contract.

A normal plugin should receive capabilities such as:

```text
IVegaPluginContext
    +-- Players
    +-- Commands
    +-- Messaging
    +-- Worlds
    +-- NPC operations
    +-- Chests
    +-- Configuration
    +-- Logging
    +-- Permissions
```

The exact surface remains Vega-owned. The important rule is that ordinary plugins cannot obtain direct references to mutable TerraRuntime state.

For example, plugin code should request a safe operation:

```text
plugin
  -> Vega.PluginSdk operation
  -> Vega policy/validation
  -> TerraRuntime host contract command
  -> authoritative game-loop queue
  -> mutation on the game thread
```

It must not become:

```text
plugin
  -> TerraRuntime.World.Players[index].Position = ...
```

## Plugin loading ownership

TerraRuntime owns loading of explicitly trusted host modules such as Vega.

Vega continues to own the ordinary server-plugin lifecycle and its policy boundary:

- `ServerPlugins/*.dll` discovery;
- plugin metadata/compatibility checks;
- one collectible `AssemblyLoadContext` per hot-loadable plugin where appropriate;
- activation/deactivation;
- hot replacement;
- command/event registration;
- configuration/data directories;
- failure isolation at the plugin lifecycle boundary;
- permission and capability policy.

This preserves the existing reason Vega has a Plugin SDK at all: plugins receive a stable restricted surface instead of the keys to the runtime engine room.

No per-tick plugin dispatch path may depend on assembly scanning or repeated reflection. Reflection may be used at bounded plugin discovery/activation boundaries when required by the CoreCLR host, while steady-state dispatch uses explicit registrations and cached delegates/interfaces.

## Security model

The SDK boundary protects runtime invariants, compatibility and accidental misuse. It is not a cryptographic sandbox for hostile code.

A managed DLL running in the same CoreCLR process can potentially use reflection, unsafe code, native interop or other mechanisms if an actively malicious author is determined to escape the intended API. Therefore:

- ordinary plugins are treated as trusted-to-run code but least-privileged by API design;
- TerraRuntime internals remain non-public wherever practical;
- no plugin receives mutable runtime objects just because it is convenient;
- true hostile-code isolation requires a separate process/sandbox and is a different feature.

The normal plugin architecture must not introduce IPC into the gameplay hot path merely to pretend same-process managed code is fully sandboxed.

## Deployment layout

Keep the human-facing server root clean even though the CoreCLR profile necessarily ships managed assemblies:

```text
TerraRuntime/
├── TerraRuntime.Server.exe
├── runtime/
│   └── managed/native runtime dependencies
├── HostModules/
│   └── Vega.dll
├── ServerPlugins/
│   ├── AntiProxy.dll
│   ├── Regions.dll
│   └── ...
├── Worlds/
├── config/
├── data/
└── logs/
```

The exact dependency subdirectory may be named `runtime/` or `libs/`, but loose framework/runtime libraries must not flood the root directory.

The NativeAOT deployment remains independently clean and does not need `HostModules/` or `ServerPlugins/` when managed plugin loading is absent.

## Project-boundary implications

The source tree should evolve toward explicit layering:

```text
TerraRuntime.Contracts / core-safe contracts
TerraRuntime.Core
TerraRuntime.Network
TerraRuntime.World
TerraRuntime.Protocol
        |
        +--> NativeAOT host
        |
        +--> CoreCLR extensible host
                    |
                    +--> TerraRuntime.HostContracts
                    +--> trusted host-module loader
                               |
                               +--> Vega.dll
                                         |
                                         +--> Vega.PluginSdk
                                         +--> ServerPlugins/*.dll
```

AOT analyzers remain mandatory for the runtime core and NativeAOT shipping graph. CoreCLR-only plugin-host code must be isolated so `AssemblyLoadContext`, managed DLL loading and other deliberate dynamic-host features do not contaminate the NativeAOT core graph.

## Performance acceptance

The extensible CoreCLR profile and standalone NativeAOT profile must be benchmarked separately on the same scenarios.

At minimum compare:

- startup to `NetworkReady`;
- idle CPU and memory;
- mean/p95/p99 tick time;
- worst simulation phase;
- allocations per tick;
- GC collection counts and pause time;
- packet throughput and bytes/sec;
- 24-player realistic workload;
- high-connection stress workload;
- plugin dispatch overhead with representative Vega plugins enabled.

Do not claim NativeAOT or CoreCLR is universally faster. Keep the profile that wins the workload it is intended to serve.

For the Vega-enabled extensible server, sustained runtime performance plus drop-in DLL extensibility is the governing target. For the standalone profile, NativeAOT remains a first-class deployment and architectural constraint.

## Acceptance criteria

This roadmap item is complete when:

1. [x] TerraRuntime core remains buildable and smoke-tested under NativeAOT on Linux x64 and Windows x64.
2. [ ] A CoreCLR extensible host can load `HostModules/Vega.dll` without exposing TerraRuntime implementation assemblies as a public plugin SDK.
3. [ ] Vega can load an ordinary managed plugin by placing its DLL in `ServerPlugins/`.
4. [ ] Ordinary plugins compile only against `Vega.PluginSdk` for normal server capabilities.
5. [ ] Plugin mutations cross Vega policy and TerraRuntime command boundaries before touching authoritative state.
6. [x] CoreCLR production defaults include Server GC, Tiered Compilation, Dynamic PGO and ReadyToRun as listed above.
7. [ ] Hot reload remains available for compatible Vega plugins through collectible `AssemblyLoadContext`.
8. [ ] The CoreCLR and NativeAOT profiles have reproducible performance comparisons rather than assumption-based claims.
