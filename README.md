# TerraRuntime

High-performance clean-room **.NET 11** server runtime for Terraria, focused on vanilla behavioral parity, security, fast startup and scalability.

TerraRuntime deliberately keeps two shipping profiles:

- an extensible CoreCLR host for Vega and ordinary drop-in managed DLL plugins;
- a standalone NativeAOT host with no arbitrary managed plugin loading.

The runtime core stays NativeAOT-compatible and continues to pass Linux x64 and Windows x64 native publication/smoke gates even though the Vega-enabled plugin host uses CoreCLR.

## Documentation

TerraRuntime keeps first-class bilingual documentation in parallel with code changes:

- [Русская документация](docs/ru/README.md)
- [English documentation](docs/en/README.md)
- [Roadmap](docs/roadmap.md)

The project guides cover runtime operation, architecture and trusted-host interfaces. When behavior, architecture, public contracts, deployment, persistence or supported scope changes, both RU and EN documentation are updated in the same change rather than deferred to a later documentation pass.

## Runtime directory layout

A normal standalone runtime startup uses the executable directory as the TerraRuntime root and ensures a small runtime-owned directory layout exists:

```text
TerraRuntime.Server[.exe]
Worlds/    # canonical .wld worlds and adjacent .runtime-world snapshots
config/    # reserved for TerraRuntime-owned configuration
data/      # reserved for TerraRuntime-owned durable auxiliary state
logs/      # reserved for standalone runtime diagnostics/log output
```

`--world <path.wld>` may still point to an explicit world anywhere on disk. Interactive startup without `--world` only enumerates the canonical `Worlds/` directory, avoiding ambiguous current-working-directory lookup.

## NativeAOT standalone deployment

A production NativeAOT publish is intentionally **not** a copied `dotnet build` directory. TerraRuntime project/package assemblies are linked into the native host, so the NativeAOT deployment root must not accumulate loose managed `TerraRuntime.*.dll`, `Multiplicity.dll`, `Terminal.Gui.dll`, `*.deps.json` or `*.runtimeconfig.json` files. Required native sidecars emitted by the NativeAOT publish remain next to the executable so the artifact can be started as-is.

Publish directly into the final artifact directory:

```text
dotnet publish src/TerraRuntime/TerraRuntime.csproj -c Release -r win-x64 -o artifacts/native-aot/win-x64

artifacts/native-aot/win-x64/
├── TerraRuntime.Server.exe
├── libonigwrap.dll          # required native Terminal.Gui dependency
├── Worlds/
├── config/
├── data/
└── logs/
```

The equivalent Linux artifact contains `libonigwrap.so` next to `TerraRuntime.Server`. Release publish symbols such as `*.pdb` and `*.dbg` are excluded from the runnable artifact. CI explicitly requires the platform `libonigwrap` sidecar and launches every NativeAOT smoke path directly from `artifacts/native-aot/<RID>/`, so a missing runtime dependency fails before the artifact can be treated as deployable.

## Vega and managed plugins

The extensible .NET 11 CoreCLR host is published as a self-contained single-file executable. Framework/runtime and TerraRuntime implementation dependencies required to start the host are bundled into that executable, while the human-facing artifact keeps explicit runtime/plugin/data directories:

```text
dotnet publish src/TerraRuntime.ExtensibleHost/TerraRuntime.ExtensibleHost.csproj -c Release -r win-x64 -o artifacts/coreclr/win-x64

artifacts/coreclr/win-x64/
├── TerraRuntime.Extensible.Server.exe
├── runtime/
├── HostModules/
│   └── Vega.dll
├── ServerPlugins/
│   └── *.dll
├── Worlds/
├── config/
├── data/
└── logs/
```

`runtime/` is reserved for deliberately external sidecar dependencies if the host ever admits one; the current self-contained single-file baseline does not require loose framework DLLs there. Release debug symbols are excluded from the runnable artifact. CI installs a drop-in host-module fixture and launches the server directly from `artifacts/coreclr/<RID>/`.

Vega is a trusted host module and receives a narrow privileged TerraRuntime host contract. Ordinary plugins remain behind `Vega.PluginSdk`; they do not receive TerraRuntime implementation objects or mutable authoritative state directly.

The CoreCLR production baseline is:

```xml
<ServerGarbageCollection>true</ServerGarbageCollection>
<TieredCompilation>true</TieredCompilation>
<TieredPGO>true</TieredPGO>
<PublishReadyToRun>true</PublishReadyToRun>
```

NativeAOT is not removed by this model. It remains a standalone deployment profile, benchmark target and mandatory architectural/CI gate for the TerraRuntime core.

See:

- [`docs/ru/README.md`](docs/ru/README.md) and [`docs/en/README.md`](docs/en/README.md) for the bilingual documentation entry points;
- [`docs/roadmap/documentation.md`](docs/roadmap/documentation.md) for the permanent documentation policy and coverage plan;
- [`docs/roadmap/runtime-host-plugin-architecture.md`](docs/roadmap/runtime-host-plugin-architecture.md) for the CoreCLR host, trusted Vega DLL, Plugin SDK boundary and dual-host acceptance criteria;
- [`docs/native-aot-baseline.md`](docs/native-aot-baseline.md) for the NativeAOT/CoreCLR hosting split and native build gates;
- [`docs/aot-dependency-audit.md`](docs/aot-dependency-audit.md) for the dependency audit;
- [`docs/roadmap.md`](docs/roadmap.md) for the broader implementation plan;
- [`docs/roadmap/performance-tick-stability.md`](docs/roadmap/performance-tick-stability.md) for the detailed performance, tick-budget and interest-management roadmap;
- [`docs/roadmap/gameplay-decomposition-and-catalogs.md`](docs/roadmap/gameplay-decomposition-and-catalogs.md) for gameplay decomposition, typed vanilla IDs/catalogs and the removal of magic numbers across items, NPCs, projectiles, tiles, walls, buffs and related systems;
- [`docs/roadmap/npc-ai-parity.md`](docs/roadmap/npc-ai-parity.md) for the explicit non-complete vanilla NPC/AI coverage ledger and expansion plan;
- [`docs/roadmap/gameplay-worldgen-extensibility.md`](docs/roadmap/gameplay-worldgen-extensibility.md) for custom NPC/projectile behavior, server-defined archetypes, Vega/plugin integration and pluggable world generation;
- [`docs/roadmap/runtime-logging-pipeline.md`](docs/roadmap/runtime-logging-pipeline.md) for the runtime-owned bounded asynchronous structured logging pipeline and Vega integration boundary.
