# World generation

[Русский](../ru/world-generation.md) · [Documentation](README.md) · [Host interfaces](host-interfaces.md) · [Worldgen roadmap](../roadmap/gameplay-worldgen-extensibility.md)

## 1. Current status

TerraRuntime has a real world-generation **framework**, but it does not yet have vanilla Terraria WorldGen parity.

The current built-in generator is a deterministic flat dirt/stone baseline. Its purpose is to exercise the complete generation pipeline and contracts, not to approximate Terraria biomes, structures, progression objects or vanilla RNG ordering.

This distinction is normative:

```text
worldgen framework      substantially implemented
custom provider surface implemented
vanilla Terraria WorldGen incomplete
```

## 2. Architecture

```text
startup / trusted host
       |
       v
world-generator source/registry
       |
       v
selected IWorldGenerationProvider
       |
       v
BuildPlan(request, builder)
       |
       v
validated pass graph/order
       |
       v
isolated candidate workspace
       |
       v
ordered pass execution
       |
       v
metadata/final validation
       |
       v
candidate publication / persistence path
```

A generator never receives the live authoritative world while building its plan.

## 3. Generator identity

`WorldGeneratorId` is a stable namespaced identity for a selectable generation profile.

Rules enforced by the value type include:

- non-empty value;
- maximum length of 128 characters;
- no whitespace/control characters.

Use namespaced IDs such as:

```text
myhost:survival
myplugin:skyblock
```

rather than short global names likely to collide.

## 4. Generation request

`WorldGenerationRequest` is immutable and includes:

```text
GeneratorId
WorldName
Seed (ulong)
WidthTiles
HeightTiles
```

The request validates assigned generator identity, non-empty bounded world name, positive dimensions and checked tile-count arithmetic.

A provider should treat the request as input, not as a place to store mutable generation state.

## 5. Provider contract

A selectable custom generator implements:

```csharp
public interface IWorldGenerationProvider
{
    WorldGeneratorId Id { get; }
    void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder);
}
```

`BuildPlan` declares passes and ordering. It does not execute expensive world generation directly and does not mutate a live world.

This separation lets TerraRuntime validate the plan before executing host-supplied generation code.

## 6. Pass identity and ordering

Every pass has a stable `WorldGenerationPassId` and a `WorldGenerationPassDescriptor`.

Descriptors can declare:

- `RequiredAfter`: hard dependencies that must exist;
- `OptionalAfter`: ordering hint when the referenced pass exists;
- `OptionalBefore`: ordering hint when the referenced pass exists;
- RNG mode.

Descriptor dependency arrays are defensively copied. A provider cannot mutate ordering metadata behind the runtime after staging it.

The planner must reject invalid graphs such as missing hard dependencies, duplicate pass IDs or cyclic ordering rather than executing an ambiguous plan.

## 7. Pass execution

A pass implements:

```csharp
public interface IWorldGenerationPass
{
    void Execute(IWorldGenerationContext context);
}
```

Pass execution is synchronous against an isolated candidate workspace. Long-running passes must observe the supplied cancellation token at useful intervals.

A pass can report bounded progress through `context.ReportProgress(...)`.

## 8. Candidate workspace

`IWorldGenerationWorkspace` exposes only the candidate world's normalized tile surface:

```csharp
int WidthTiles { get; }
int HeightTiles { get; }

bool TryGetTile(int x, int y, out WorldGenerationTile tile);
bool TrySetTile(int x, int y, in WorldGenerationTile tile);
```

The workspace is intentionally not `WorldTileStore` and not the live world. This prevents host code from depending on internal storage layouts or publishing half-generated state during an exception.

The runtime validates writes at the workspace boundary.

## 9. Normalized generation tile

`WorldGenerationTile` carries generator-facing tile state such as:

- tile and wall type;
- frame coordinates;
- active/wire/actuator/visibility/fullbright flags;
- liquid amount/type;
- tile/wall color;
- shape.

Generator-facing types are independent from internal `TerraRuntime.World` implementation types.

Raw vanilla content IDs still require verified semantics. A custom generator being able to write a numeric tile ID does not make an invalid multi-tile object layout legal.

## 10. World metadata

Tile data alone is not a complete world.

`IWorldGenerationMetadataWorkspace` currently provides semantic operations for required anchors such as:

- spawn point;
- dungeon anchor;
- world surface and rock layer.

The provider sets gameplay concepts rather than raw `.wld` header offsets. TerraRuntime remains responsible for persistence-format-specific representation and validation.

This surface will grow only when a generated world needs additional stable semantic state.

## 11. RNG modes

`WorldGenerationPassDescriptor` currently supports three RNG-mode identities:

```text
IsolatedDeterministic
VanillaSharedRng
CustomProviderRng
```

The current custom-provider baseline is designed around deterministic isolated pass execution where practical.

Vanilla WorldGen is much more sensitive: many official passes share/order RNG consumption. Future vanilla parity must preserve that ordering explicitly rather than parallelizing passes because they appear computationally independent.

A pass marked or designed around vanilla shared RNG must not be casually moved/reordered or executed concurrently.

## 12. Runtime RNG surface

A generation pass consumes `IWorldGenerationRandom` rather than a host-global `Random` instance.

The surface exposes deterministic integer primitives such as:

```text
NextUInt64
NextUInt32
NextInt32(exclusiveMax)
```

This gives the runtime control over deterministic construction and prevents a provider from coupling correctness to process-global randomness.

## 13. Progress

Generation progress is represented by `WorldGenerationProgress`:

```text
PassId
PassIndex
PassCount
Fraction
Message
```

A progress sink is optional. Generation correctness must not depend on a UI consuming the progress stream.

Progress callbacks must be treated as observability, not as a mutation API for the candidate world.

## 14. Trusted-host registration

Trusted CoreCLR host modules register providers through `ITerraRuntimeWorldGeneratorRegistry`.

Registration returns a lifetime handle. The host retires/disposes that registration before unloading the module that owns the provider instance.

The runtime does not scan arbitrary assemblies looking for `IWorldGenerationProvider` implementations.

This explicit registration is compatible with the project's AOT/static-registration discipline.

## 15. Listing generators

Normal startup supports:

```text
TerraRuntime.Server --list-world-generators
```

The command lists custom generators visible from the supplied trusted-host generator source.

The built-in runtime generator source and host-registered provider source are composed deliberately rather than discovered by reflection.

## 16. Built-in flat generator

The current built-in generator ID is:

```text
terraruntime:flat
```

Its plan has a terrain pass followed by a metadata pass.

The terrain pass builds a simple deterministic dirt/stone world. The metadata pass establishes spawn, dungeon anchor and world layers.

It deliberately uses simple frame-free tile types so the baseline tests the complete pipeline without pretending that complex Terraria object framing has been implemented.

## 17. Isolation and publication

Generation happens in an isolated candidate workspace. The live/authoritative server world is not incrementally replaced while passes are running.

The desired failure model is:

```text
all passes + validation succeed
    -> publish candidate

any pass/validation fails
    -> discard candidate
```

A half-generated candidate must not become the running world merely because some earlier passes succeeded.

## 18. Cancellation and failure

Passes receive a cancellation token and should check it in long loops.

Provider exceptions, invalid plans, invalid candidate writes or final validation failures must abort the candidate safely.

Failure diagnostics should identify the generator/pass where possible without leaking mutable runtime objects to the host.

## 19. NativeAOT and CoreCLR boundaries

The generation **contracts** and runtime planner/executor remain compatible with the NativeAOT-first core architecture.

Dynamic discovery/loading of arbitrary generator DLLs is not required by the core.

The CoreCLR extensible host may load trusted modules and explicitly register their providers. The standalone NativeAOT host uses statically known/built-in providers unless another AOT-compatible registration path is added deliberately.

## 20. Adding a custom generator

Typical host-side flow:

```csharp
public sealed class MyGenerator : IWorldGenerationProvider
{
    public WorldGeneratorId Id => new("example:flat-plus");

    public void BuildPlan(
        in WorldGenerationRequest request,
        IWorldGenerationPlanBuilder builder)
    {
        builder.Add(
            new WorldGenerationPassDescriptor(new("example:terrain")),
            new TerrainPass());

        builder.Add(
            new WorldGenerationPassDescriptor(
                new("example:metadata"),
                requiredAfter: [new("example:terrain")]),
            new MetadataPass());
    }
}
```

Register it during trusted host bootstrap:

```csharp
var result = environment.WorldGenerators.TryRegister(
    new MyGenerator(),
    out var registration);
```

The owning module retains `registration` and disposes it before unload.

## 21. Provider rules

A provider/pass must not:

- mutate the currently running world;
- retain the candidate workspace after execution;
- assume internal `WorldTileStore` memory layout;
- use reflection-based discovery as a hidden registration mechanism;
- ignore cancellation in large loops;
- make generation success depend on TUI availability;
- claim vanilla parity for guessed structures/RNG behavior.

## 22. Vanilla WorldGen work

Actual vanilla WorldGen remains a large late-stage parity project.

It requires, among other things:

- official pass ordering;
- shared RNG sequencing;
- terrain/biome generation;
- ores/caves/liquids;
- structures and dungeons;
- chests/objects/tile entities;
- world progression metadata;
- framing and support rules;
- deterministic/reference seed tests;
- generated-world validation against Terraria 1.4.5.8.

Pass-level parallelism is forbidden when it changes the official shared RNG/order semantics.

## 23. Evidence

Current framework evidence includes tests for planner ordering/validation, executor behavior, RNG behavior, workspace/finalization, provider registry lifetime and startup world-creation parsing, plus CI contracts that exercise generated world handling.

Future vanilla-worldgen parity needs independent official reference worlds/statistics for selected seeds. A custom flat-world unit test cannot prove vanilla WorldGen.

## 24. Current limitations

- built-in generation is flat baseline only;
- vanilla biomes/structures/events/progression generation are not complete;
- metadata workspace exposes only the semantic anchors currently needed;
- full canonical `.wld` creation/writer support continues to evolve with persistence work;
- not every raw tile/object combination accepted at the normalized tile level is a legal vanilla object arrangement.

## 25. Change checklist

A worldgen change is incomplete unless, where relevant, plan dependencies are validated, candidate mutation remains isolated, RNG ordering is explicit, long work is cancellable, metadata is semantic rather than raw file offsets, provider lifetime is safe across host unload, failure does not publish a partial world, and this page plus `docs/ru/world-generation.md` are updated together.
