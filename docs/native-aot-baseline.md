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
NativeAOT linux-x64 + native smoke
NativeAOT win-x64 + native smoke
```

A change that cannot satisfy the NativeAOT jobs is considered an architectural regression unless the runtime architecture itself is deliberately changed.

## Internal project policy

`src/Directory.Build.props` applies `IsAotCompatible=true` and trim/AOT analysis to every current and future production project under `src/`. A new production project therefore enters the AOT contract automatically instead of relying on somebody remembering to copy project flags.

`TerraRuntime.Server` sets `PublishAot=true` and `IlcTreatWarningsAsErrors=true` by default. JIT-specific tuning such as tiered compilation or Dynamic PGO must not become part of the production design assumptions.

## Process boundaries

NativeAOT does **not** require TerraRuntime itself to be split into multiple processes. The core server remains a single native process unless a real isolation or operational requirement justifies another process.

Optional transport/sandbox processes may exist for isolation, tooling or operations, but IPC is not part of the normal game-loop hot path and is not required merely to achieve AOT.
