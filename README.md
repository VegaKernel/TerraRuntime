# TerraRuntime

High-performance clean-room **.NET 11 NativeAOT-first** server runtime for Terraria, focused on vanilla behavioral parity, security, fast startup and scalability.

Production target: a native server executable with no JIT requirement and no arbitrary managed plugin loading inside the runtime process.

## Runtime directory layout

A normal server startup uses the executable directory as the TerraRuntime root and ensures a small runtime-owned directory layout exists:

```text
TerraRuntime.Server[.exe]
Worlds/    # canonical .wld worlds and adjacent .runtime-world snapshots
config/    # reserved for TerraRuntime-owned configuration
data/      # reserved for TerraRuntime-owned durable auxiliary state
logs/      # reserved for standalone runtime diagnostics/log output
```

`--world <path.wld>` may still point to an explicit world anywhere on disk. Interactive startup without `--world` only enumerates the canonical `Worlds/` directory, avoiding ambiguous current-working-directory lookup.

A production NativeAOT publish is intentionally **not** a copied `dotnet build` directory. TerraRuntime project/package assemblies are linked into the native host, so the deployment root must not accumulate loose `TerraRuntime.*.dll`, `Multiplicity.dll`, `Terminal.Gui.dll`, `*.deps.json` or `*.runtimeconfig.json` files.

Publishing automatically creates a clean ready-to-run deployment tree under `artifacts/deploy/<RID>/`. For example:

```text
dotnet publish src/TerraRuntime/TerraRuntime.csproj -c Release -r win-x64

artifacts/deploy/win-x64/
├── TerraRuntime.Server.exe
├── Worlds/
├── config/
├── data/
└── logs/
```

The intermediate SDK publish output may contain build/debug artifacts and is not the deployment directory. CI launches all NativeAOT smoke tests from the generated clean deployment tree and rejects any unexpected root entries.

The normal Vega topology is also single-process: Vega hosts the TerraRuntime implementation in the same NativeAOT executable and consumes its stable API through `TerraRuntime.Contracts`. The standalone `TerraRuntime.Server[.exe]` remains available for development, smoke tests and runtime-only deployments.

See:

- [`docs/native-aot-baseline.md`](docs/native-aot-baseline.md) for the mandatory NativeAOT architecture, Vega hosting and clean-deployment rules;
- [`docs/aot-dependency-audit.md`](docs/aot-dependency-audit.md) for the dependency audit;
- [`docs/roadmap.md`](docs/roadmap.md) for the broader implementation plan;
- [`docs/roadmap/performance-tick-stability.md`](docs/roadmap/performance-tick-stability.md) for the detailed performance, tick-budget and interest-management roadmap;
- [`docs/roadmap/gameplay-decomposition-and-catalogs.md`](docs/roadmap/gameplay-decomposition-and-catalogs.md) for gameplay decomposition, typed vanilla IDs/catalogs and the removal of magic numbers across items, NPCs, projectiles, tiles, walls, buffs and related systems;
- [`docs/roadmap/gameplay-worldgen-extensibility.md`](docs/roadmap/gameplay-worldgen-extensibility.md) for custom NPC/projectile behavior, server-defined archetypes, Vega/plugin integration and pluggable world generation;
- [`docs/roadmap/runtime-logging-pipeline.md`](docs/roadmap/runtime-logging-pipeline.md) for the runtime-owned bounded asynchronous structured logging pipeline and Vega integration boundary.
