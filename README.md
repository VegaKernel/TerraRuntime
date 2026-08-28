# TerraRuntime

High-performance clean-room **.NET 11 NativeAOT-first** server runtime for Terraria, focused on vanilla behavioral parity, security, fast startup and scalability.

Production target: a native server executable with no JIT requirement and no arbitrary managed plugin loading inside the runtime process.

See:

- [`docs/native-aot-baseline.md`](docs/native-aot-baseline.md) for the mandatory NativeAOT architecture rules;
- [`docs/aot-dependency-audit.md`](docs/aot-dependency-audit.md) for the dependency audit;
- [`docs/roadmap.md`](docs/roadmap.md) for the broader implementation plan;
- [`docs/roadmap/performance-tick-stability.md`](docs/roadmap/performance-tick-stability.md) for the detailed performance, tick-budget and interest-management roadmap;
- [`docs/roadmap/gameplay-decomposition-and-catalogs.md`](docs/roadmap/gameplay-decomposition-and-catalogs.md) for gameplay decomposition, typed vanilla IDs/catalogs and the removal of magic numbers across items, NPCs, projectiles, tiles, walls, buffs and related systems;
- [`docs/roadmap/gameplay-worldgen-extensibility.md`](docs/roadmap/gameplay-worldgen-extensibility.md) for custom NPC/projectile behavior, server-defined archetypes, Vega/plugin integration and pluggable world generation;
- [`docs/roadmap/runtime-logging-pipeline.md`](docs/roadmap/runtime-logging-pipeline.md) for the runtime-owned bounded asynchronous structured logging pipeline and Vega integration boundary.
