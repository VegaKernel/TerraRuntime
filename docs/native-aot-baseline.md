# NativeAOT-first baseline

TerraRuntime targets a **pure NativeAOT production build on .NET 11**.

This is an architectural constraint, not a later optimization pass.

## Production rule

The shipping server is expected to be published as a native executable:

```text
TerraRuntime source graph
        |
        v
.NET 11 NativeAOT publish
        |
        +-- linux-x64/TerraRuntime.Server
        +-- win-x64/TerraRuntime.Server.exe
```

CoreCLR may still be used during development when it improves debugging, profiling or iteration speed. It is not the production architecture target.

## Vega hosting model

The normal production topology is **Vega and TerraRuntime in the same NativeAOT process**.

The source graph remains split by explicit contracts even though deployment is a single native host:

```text
Vega source graph
    |
    +-- Vega application layer
    +-- TerraRuntime implementation
    +-- TerraRuntime.Contracts
    +-- other admitted AOT-safe dependencies
    |
    v
.NET 11 NativeAOT publish
    |
    +-- Vega.Server[.exe]
```

`TerraRuntime.Contracts` is the stable compile-time boundary for Vega-facing runtime interfaces, handles, snapshots and DTOs. Vega should depend on contracts instead of implementation details wherever a public contract is sufficient.

A standalone `TerraRuntime.Server[.exe]` remains supported for runtime development, smoke tests, debugging and deployments that intentionally do not include Vega. That standalone host does not imply an IPC boundary for the normal Vega topology.

## Clean production layout

NativeAOT project and package assemblies are build inputs, not files that should be copied wholesale into the production server root. A normal `dotnet build` output may contain many managed DLLs; it is not a deployment layout.

The intended standalone TerraRuntime deployment root is:

```text
TerraRuntime.Server[.exe]
Worlds/
config/
data/
logs/
```

A NativeAOT `dotnet publish` of `src/TerraRuntime/TerraRuntime.csproj` automatically recreates this clean tree under:

```text
artifacts/deploy/<RuntimeIdentifier>/
```

The clean tree contains only the native executable and the runtime-owned directories above. The SDK/intermediate publish directory is deliberately kept separate and may contain build or debug artifacts that are not part of deployment.

The Vega-hosted deployment follows the same rule: the root is centered around `Vega.Server[.exe]` plus application/runtime data directories, not a loose pile of `TerraRuntime.*.dll`, `Multiplicity.dll`, `Terminal.Gui.dll` or other managed build artifacts.

CI launches NativeAOT smoke paths from the clean deployment tree and rejects unexpected root entries. That makes the one-file runtime assumption executable evidence: if the native server accidentally starts depending on a loose sidecar, the smoke gate fails.

If a dependency genuinely requires a native sidecar library that cannot be statically linked into the executable, it must be admitted explicitly as a deployment dependency. When the platform loader permits it, such sidecars belong under a dedicated `runtime/native/` location rather than turning the server root into a generic library directory.

Debug symbols and other developer-only artifacts belong in build/CI artifacts rather than the normal deployment package unless a deployment explicitly opts into them.

## AOT-safe design rules

Production code must not depend on runtime features that require a JIT or arbitrary managed assembly loading.

Do not introduce these into the shipping graph:

- `Assembly.Load*` or arbitrary managed DLL loading;
- collectible/dynamic plugin `AssemblyLoadContext` use;
- `Reflection.Emit`;
- `DynamicMethod`;
- runtime code generation;
- expression-tree compilation that requires generated IL;
- reflection-based serializers without an explicit trimming/AOT contract;
- runtime assembly scanning as a registration mechanism.

Prefer:

- explicit/static registration;
- compile-time generated registries;
- source generators;
- `System.Text.Json` source generation when JSON is needed;
- typed protocol codecs;
- `Span<T>`, `ReadOnlySpan<T>`, `IBufferWriter<T>` and bounded buffers;
- BCL functionality over unnecessary dependencies.

## Dependency admission gate

Every package that enters a shipping TerraRuntime project must pass:

1. .NET 11 build with AOT/trimming analyzers enabled;
2. NativeAOT publish for every supported production RID;
3. zero unexplained trim/AOT warnings;
4. startup of the produced native executable;
5. an exercised smoke path through the dependency, not merely successful linking;
6. repeat validation on package upgrades.

The current dependency audit is recorded in [`aot-dependency-audit.md`](aot-dependency-audit.md).

## Current CI contract

CI must keep all three gates green:

```text
build + tests
NativeAOT linux-x64 + native smoke from artifacts/deploy/linux-x64
NativeAOT win-x64 + native smoke from artifacts/deploy/win-x64
```

A change that cannot satisfy the NativeAOT jobs is considered an architectural regression unless the runtime architecture itself is deliberately changed.

## Internal project policy

`src/Directory.Build.props` applies `IsAotCompatible=true` and trim/AOT analysis to every current and future production project under `src/`. A new production project therefore enters the AOT contract automatically instead of relying on somebody remembering to copy project flags.

`TerraRuntime.Server` sets `PublishAot=true` and `IlcTreatWarningsAsErrors=true` by default. JIT-specific tuning such as tiered compilation or Dynamic PGO must not become part of the production design assumptions.

## Process boundaries

NativeAOT does **not** require TerraRuntime itself to be split into multiple processes. The core server remains a single native process unless a real isolation or operational requirement justifies another process.

Optional transport/sandbox processes may exist for isolation, tooling or operations, but IPC is not part of the normal game-loop hot path and is not required merely to achieve AOT.
