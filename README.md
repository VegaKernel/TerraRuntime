# TerraRuntime

High-performance clean-room **.NET 11 NativeAOT-first** server runtime for Terraria, focused on vanilla behavioral parity, security, fast startup and scalability.

Production target: a native server executable with no JIT requirement and no arbitrary managed plugin loading inside the runtime process.

See:

- [`docs/native-aot-baseline.md`](docs/native-aot-baseline.md) for the mandatory NativeAOT architecture rules;
- [`docs/aot-dependency-audit.md`](docs/aot-dependency-audit.md) for the dependency audit;
- [`docs/roadmap.md`](docs/roadmap.md) for the broader implementation plan.
