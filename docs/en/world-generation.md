# World generation

[Русский](../ru/world-generation.md) · [Documentation](README.md) · [Host interfaces](host-interfaces.md) · [Worldgen roadmap](../roadmap/gameplay-worldgen-extensibility.md)

## 1. Current status

TerraRuntime has a real world-generation **framework**, but not vanilla Terraria WorldGen parity. The built-in generator is a deterministic flat dirt/stone baseline intended to exercise contracts and publication, not approximate vanilla biomes, structures or RNG ordering.

| Capability | Status |
|---|---|
| Worldgen framework | substantial |
| Trusted custom-provider surface | implemented |
| Vanilla Terraria WorldGen | incomplete |

## 2. Architecture

```mermaid
flowchart TD
    Start["Startup / trusted host"] --> Registry["World-generator source / registry"]
    Registry --> Provider["Selected IWorldGenerationProvider"]
    Provider --> Plan["BuildPlan(request, builder)"]
    Plan --> ValidatePlan["Validate dependency graph + order"]
    ValidatePlan --> Workspace["Isolated candidate workspace"]
    Workspace --> Execute["Ordered pass execution"]
    Execute --> ValidateWorld["Metadata + final validation"]
    ValidateWorld --> Publish["Candidate publication / persistence"]
```

A generator never receives the live authoritative world while building/executing its candidate.

## 3. Generator identity and request

`WorldGeneratorId` is a stable namespaced identity. It must be non-empty, contain no whitespace/control characters and is bounded to `$128$` characters. Prefer names such as `myhost:survival` or `myplugin:skyblock`.

`WorldGenerationRequest` is immutable and carries `GeneratorId`, `WorldName`, `Seed`, `WidthTiles` and `HeightTiles`. Dimensions are positive and tile-count arithmetic is checked.

## 4. Provider contract

```csharp
public interface IWorldGenerationProvider
{
    WorldGeneratorId Id { get; }
    void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder);
}
```

`BuildPlan` declares passes/order. It does not perform the expensive generation directly and does not mutate a live world.

## 5. Pass graph and ordering

Each pass has a stable `WorldGenerationPassId` and descriptor. Dependencies can express `RequiredAfter`, `OptionalAfter`, `OptionalBefore` and RNG mode.

```mermaid
flowchart LR
    A["Pass A"] -->|RequiredAfter| B["Pass B"]
    B --> C["Pass C"]
    A -. optional ordering .-> C
```

The planner rejects missing hard dependencies, duplicate pass IDs and cycles before host-supplied pass execution begins. Dependency arrays are defensively copied.

## 6. Pass execution and cancellation

```csharp
public interface IWorldGenerationPass
{
    void Execute(IWorldGenerationContext context);
}
```

Execution is synchronous against an isolated candidate workspace. Long-running loops must observe the supplied cancellation token at useful intervals. Progress can be reported through `context.ReportProgress(...)` but remains observability rather than mutation authority.

## 7. Candidate workspace

`IWorldGenerationWorkspace` exposes normalized candidate tile reads/writes rather than `WorldTileStore` or live runtime memory.

```csharp
int WidthTiles { get; }
int HeightTiles { get; }
bool TryGetTile(int x, int y, out WorldGenerationTile tile);
bool TrySetTile(int x, int y, in WorldGenerationTile tile);
```

This prevents host code from depending on internal storage layout or publishing half-generated state after an exception.

## 8. Tile and metadata surfaces

`WorldGenerationTile` carries normalized tile/wall types, frame coordinates, wire/actuator/visibility/fullbright flags, liquid state, colors and shape.

`IWorldGenerationMetadataWorkspace` exposes semantic world anchors such as spawn, dungeon anchor, world surface and rock layer instead of raw `.wld` offsets.

Writing a numeric tile ID does not automatically make a multi-tile object arrangement legal vanilla state.

## 9. RNG modes

Current RNG-mode identities are `IsolatedDeterministic`, `VanillaSharedRng` and `CustomProviderRng`.

```mermaid
flowchart TD
    Pass["Generation pass"] --> Mode{"RNG mode"}
    Mode --> Isolated["TerraRuntime isolated stream\nseed + stable pass ID"]
    Mode --> Vanilla["Terraria 1.4.5.8 UnifiedRandom\nreseeded to world seed before each pass"]
    Mode --> Custom["Reserved provider-owned stream"]
```

`IsolatedDeterministic` is the current executable custom-provider mode. Each pass receives its own TerraRuntime stream derived from the world seed and stable pass ID, so adding an unrelated custom pass does not shift another pass's random sequence.

The name `VanillaSharedRng` must not be interpreted as one continuous stream across the whole vanilla plan. Pinned TerrariaServer 1.4.5.8 evidence shows that `GenBase._random` aliases `WorldGen.genRand`, which aliases global `Main.rand`, while `WorldGenerator.RunPass` replaces `Main.rand` with `new UnifiedRandom(_seed)` immediately before every enabled generation pass. The RNG is therefore shared by vanilla code **inside one pass**, but every pass starts again from the same world seed.

TerraRuntime now contains a source-pinned `VanillaUnifiedRandom1458` implementation with fingerprints for the official `SetSeed`, `InternalSample`, `Sample` and large-range algorithms. The general executor still rejects `VanillaSharedRng` until the exact official seed-text-to-`int` conversion and a non-lossy vanilla RNG context surface are integrated. It fails closed instead of mapping Terraria behavior onto invented `UInt32`/`UInt64` semantics.

Passes must not be parallelized or reordered merely because they look computationally independent. Custom pass code consumes `IWorldGenerationRandom`, not process-global `Random`.

## 10. Progress

`WorldGenerationProgress` contains `PassId`, `PassIndex`, `PassCount`, `Fraction` and `Message`. Progress sinks are optional; generation correctness does not depend on a UI consuming them.

## 11. Trusted-host registration

Trusted CoreCLR modules register providers explicitly through `ITerraRuntimeWorldGeneratorRegistry`. Registration returns a lifetime handle that must be retired/disposed before unloading the module owning the provider.

The runtime does not reflection-scan arbitrary assemblies for generators. This keeps registration compatible with AOT/static-discovery constraints.

Generator listing remains literal CLI:

```text
TerraRuntime.Server --list-world-generators
```

## 12. Built-in flat generator

The built-in ID is `terraruntime:flat`. Its plan runs a terrain pass and then a metadata pass. It deliberately uses simple deterministic dirt/stone, frame-free tiles and required world anchors.

It is an infrastructure baseline, **not** a vanilla WorldGen approximation.

## 13. Isolation and publication

```mermaid
stateDiagram-v2
    [*] --> Candidate
    Candidate --> Executing: valid plan
    Executing --> Validating: all passes complete
    Executing --> Discarded: pass failure / cancellation
    Validating --> Published: final validation succeeds
    Validating --> Discarded: validation fails
    Published --> [*]
    Discarded --> [*]
```

The running authoritative world is never incrementally replaced by partial pass output.

## 14. NativeAOT and CoreCLR boundary

Generation contracts/planner/executor remain compatible with the NativeAOT-first core. Dynamic arbitrary DLL discovery is not required.

CoreCLR can load trusted modules and explicitly register providers. NativeAOT uses statically known/built-in providers unless another deliberate AOT-compatible registration path is introduced.

## 15. Adding a custom generator

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

Register during trusted-host bootstrap and retain/dispose the returned registration before unload.

## 16. Provider rules

A provider/pass must not mutate the running world, retain candidate workspace references after execution, assume `WorldTileStore` memory layout, hide reflection-based discovery, ignore cancellation in large loops, depend on TUI availability, or claim vanilla parity for guessed structures/RNG behavior.

## 17. Vanilla WorldGen work

The pinned TerrariaServer 1.4.5.8 source contract currently resolves all `$109$` `WorldGen.AddPasses()` registrations to exact pass names with zero unresolved entries. The ordered-name sequence is fingerprinted, and special-seed filtering is fingerprinted separately so conditional behavior is not confused with base registration order.

`TerrainPass` is the first implementation target. Its constructor, feature enum, `ApplyPass`, surface-offset logic, column fill and beach-retarget helpers are source-fingerprinted. Official behavior confirms Dirt tile type `$0$` between surface and rock layer and Stone tile type `$1$` below rock layer, with inactive tiles above the generated surface. Terrain also derives the generation-time surface/rock ranges and water/lava lines.

Actual vanilla WorldGen remains substantial work after Terrain: exact pre-pass bootstrap state, jungle/desert/ocean shaping, caves/ores/liquids, structures/dungeons, chests/objects/tile entities, progression metadata, framing/support rules, special seeds and deterministic/reference-seed validation against TerrariaServer 1.4.5.8.

## 18. Evidence and limitations

Current evidence covers planner validation/order, executor behavior, custom RNG isolation, the exact Terraria 1.4.5.8 `UnifiedRandom` algorithm, the `$109$`-pass registration catalog, initial `TerrainPass` source behavior, workspace/finalization, registry lifetime, startup world-creation parsing and trusted-host generation integration.

The built-in selectable generator remains flat only. Vanilla Terrain is not yet exposed as a selectable generator, vanilla structures/biomes/progression are incomplete, the semantic metadata surface is intentionally narrow, and canonical fresh `.wld` output must continue to pass both TerraRuntime and official-server acceptance.

## 19. Change checklist

A worldgen change is incomplete unless dependencies are validated, candidate mutation remains isolated, RNG ordering is explicit, long work is cancellable, metadata stays semantic, provider lifetime is safe across unload, failure cannot publish a partial world, diagrams use Mermaid, dimensional values use LaTeX where applicable, and this page changes together with `docs/ru/world-generation.md`.
