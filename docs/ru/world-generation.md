# World generation

[English](../en/world-generation.md) · [Документация](README.md) · [Host interfaces](host-interfaces.md) · [Worldgen roadmap](../roadmap/gameplay-worldgen-extensibility.md)

## 1. Текущий статус

У TerraRuntime уже есть настоящий world-generation **framework**, но vanilla Terraria WorldGen parity пока нет.

Текущий built-in generator является deterministic flat dirt/stone baseline. Его задача — прогнать полный generation pipeline и contracts, а не изображать Terraria biomes, structures, progression objects или vanilla RNG ordering.

Это нормативное различие:

```text
worldgen framework      существенно реализован
custom provider surface реализован
vanilla Terraria WorldGen incomplete
```

## 2. Архитектура

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

Generator не получает live authoritative world во время построения plan.

## 3. Generator identity

`WorldGeneratorId` — stable namespaced identity selectable generation profile.

Value type проверяет:

- non-empty value;
- maximum length 128 characters;
- отсутствие whitespace/control characters.

Используйте namespaced IDs вроде:

```text
myhost:survival
myplugin:skyblock
```

вместо коротких global names, которые легко столкнуть между собой.

## 4. Generation request

`WorldGenerationRequest` immutable и содержит:

```text
GeneratorId
WorldName
Seed (ulong)
WidthTiles
HeightTiles
```

Request валидирует assigned generator identity, non-empty bounded world name, positive dimensions и checked tile-count arithmetic.

Provider должен воспринимать request как input, а не место хранения mutable generation state.

## 5. Provider contract

Selectable custom generator реализует:

```csharp
public interface IWorldGenerationProvider
{
    WorldGeneratorId Id { get; }
    void BuildPlan(in WorldGenerationRequest request, IWorldGenerationPlanBuilder builder);
}
```

`BuildPlan` объявляет passes и ordering. Он не должен прямо выполнять expensive world generation или мутировать live world.

Так TerraRuntime может проверить plan до запуска host-supplied generation code.

## 6. Pass identity и ordering

Каждый pass имеет stable `WorldGenerationPassId` и `WorldGenerationPassDescriptor`.

Descriptor может задавать:

- `RequiredAfter`: hard dependencies, которые обязаны существовать;
- `OptionalAfter`: ordering hint при наличии referenced pass;
- `OptionalBefore`: ordering hint при наличии referenced pass;
- RNG mode.

Dependency arrays defensively copied. Provider не может изменить ordering metadata за спиной runtime после staging.

Planner обязан reject invalid graphs: missing hard dependency, duplicate pass IDs, cyclic ordering и другие ambiguous plans.

## 7. Pass execution

Pass реализует:

```csharp
public interface IWorldGenerationPass
{
    void Execute(IWorldGenerationContext context);
}
```

Execution synchronous и идёт по isolated candidate workspace. Long-running pass обязан проверять supplied cancellation token в разумных точках.

Pass может публиковать bounded progress через `context.ReportProgress(...)`.

## 8. Candidate workspace

`IWorldGenerationWorkspace` открывает только normalized tile surface candidate world:

```csharp
int WidthTiles { get; }
int HeightTiles { get; }

bool TryGetTile(int x, int y, out WorldGenerationTile tile);
bool TrySetTile(int x, int y, in WorldGenerationTile tile);
```

Workspace намеренно не является `WorldTileStore` и не является live world. Это не даёт host code зависеть от internal storage layout или публиковать half-generated state после exception.

Runtime валидирует writes на workspace boundary.

## 9. Normalized generation tile

`WorldGenerationTile` содержит generator-facing tile state:

- tile/wall type;
- frame coordinates;
- active/wire/actuator/visibility/fullbright flags;
- liquid amount/type;
- tile/wall color;
- shape.

Generator-facing types не зависят от internal `TerraRuntime.World` implementation types.

Raw vanilla content IDs всё равно требуют verified semantics. Возможность записать numeric tile ID не делает invalid multi-tile object layout легальным.

## 10. World metadata

Одних tiles недостаточно для complete world.

`IWorldGenerationMetadataWorkspace` сейчас предоставляет semantic operations для anchors:

- spawn point;
- dungeon anchor;
- world surface и rock layer.

Provider задаёт gameplay concepts вместо raw `.wld` header offsets. TerraRuntime остаётся ответственным за persistence-format-specific representation и validation.

Surface расширяется только тогда, когда generated world реально нуждается в новом stable semantic state.

## 11. RNG modes

`WorldGenerationPassDescriptor` поддерживает:

```text
IsolatedDeterministic
VanillaSharedRng
CustomProviderRng
```

Current custom-provider baseline рассчитан на deterministic isolated pass execution там, где это подходит.

Vanilla WorldGen намного чувствительнее: множество official passes разделяют/order RNG consumption. Future vanilla parity должна сохранять ordering явно, а не parallelize passes только потому, что они кажутся вычислительно независимыми.

Pass с vanilla shared RNG нельзя без доказательств двигать, reorder или выполнять concurrently.

## 12. Runtime RNG surface

Generation pass использует `IWorldGenerationRandom`, а не host-global `Random`.

Surface предоставляет deterministic integer primitives:

```text
NextUInt64
NextUInt32
NextInt32(exclusiveMax)
```

Это оставляет runtime контроль над deterministic construction и не привязывает correctness provider к process-global randomness.

## 13. Progress

Generation progress представлен `WorldGenerationProgress`:

```text
PassId
PassIndex
PassCount
Fraction
Message
```

Progress sink optional. Generation correctness не зависит от того, читает ли UI progress stream.

Progress callbacks являются observability, а не mutation API candidate world.

## 14. Trusted-host registration

Trusted CoreCLR host modules регистрируют providers через `ITerraRuntimeWorldGeneratorRegistry`.

Registration возвращает lifetime handle. Host retire/dispose registration до unload module, владеющего provider instance.

Runtime не сканирует arbitrary assemblies в поисках `IWorldGenerationProvider` implementations.

Explicit registration соответствует AOT/static-registration discipline проекта.

## 15. Listing generators

Normal startup поддерживает:

```text
TerraRuntime.Server --list-world-generators
```

Command показывает custom generators, видимые из supplied trusted-host generator source.

Built-in runtime source и host-registered provider source компонуются явно, а не ищутся reflection scanning.

## 16. Built-in flat generator

Текущий built-in generator ID:

```text
terraruntime:flat
```

Plan содержит terrain pass, затем metadata pass.

Terrain pass строит простой deterministic dirt/stone world. Metadata pass задаёт spawn, dungeon anchor и world layers.

Выбраны простые frame-free tile types, чтобы baseline проверял complete pipeline, не делая вид, что complex Terraria object framing уже реализован.

## 17. Isolation и publication

Generation выполняется в isolated candidate workspace. Live/authoritative server world не заменяется по кускам во время выполнения passes.

Желаемая failure model:

```text
all passes + validation succeed
    -> publish candidate

any pass/validation fails
    -> discard candidate
```

Half-generated candidate не становится running world только потому, что ранние passes успели завершиться.

## 18. Cancellation и failure

Passes получают cancellation token и проверяют его в длинных loops.

Provider exceptions, invalid plans, invalid candidate writes и final validation failures безопасно abort candidate.

Failure diagnostics должны по возможности указывать generator/pass, не выдавая host mutable runtime objects.

## 19. NativeAOT и CoreCLR boundaries

Generation **contracts** и runtime planner/executor остаются совместимыми с NativeAOT-first core architecture.

Dynamic discovery/loading arbitrary generator DLLs core не требуется.

CoreCLR extensible host может загрузить trusted modules и явно зарегистрировать providers. Standalone NativeAOT host использует statically known/built-in providers, пока намеренно не появится другой AOT-compatible registration path.

## 20. Добавление custom generator

Типичный host-side flow:

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

Регистрация во время trusted host bootstrap:

```csharp
var result = environment.WorldGenerators.TryRegister(
    new MyGenerator(),
    out var registration);
```

Owning module хранит `registration` и dispose его до unload.

## 21. Правила provider

Provider/pass не должен:

- мутировать currently running world;
- удерживать candidate workspace после execution;
- предполагать internal `WorldTileStore` memory layout;
- использовать reflection-based discovery как скрытый registration mechanism;
- игнорировать cancellation в больших loops;
- делать generation success зависимым от TUI availability;
- объявлять vanilla parity для guessed structures/RNG behavior.

## 22. Vanilla WorldGen work

Настоящий vanilla WorldGen остаётся большим late-stage parity project.

Потребуются, среди прочего:

- official pass ordering;
- shared RNG sequencing;
- terrain/biome generation;
- ores/caves/liquids;
- structures/dungeons;
- chests/objects/tile entities;
- world progression metadata;
- framing/support rules;
- deterministic/reference seed tests;
- generated-world validation against Terraria 1.4.5.8.

Pass-level parallelism запрещён, если меняет official shared RNG/order semantics.

## 23. Evidence

Current framework evidence включает tests planner ordering/validation, executor behavior, RNG behavior, workspace/finalization, provider registry lifetime, startup world-creation parsing и CI contracts generated world handling.

Future vanilla-worldgen parity потребует independent official reference worlds/statistics для selected seeds. Custom flat-world unit test не доказывает vanilla WorldGen.

## 24. Текущие ограничения

- built-in generation только flat baseline;
- vanilla biomes/structures/events/progression generation incomplete;
- metadata workspace открывает только semantic anchors, нужные сейчас;
- full canonical `.wld` creation/writer support развивается вместе с persistence;
- не всякая raw tile/object combination, принятая normalized tile level, является legal vanilla object arrangement.

## 25. Checklist изменения worldgen

Worldgen change не завершён, пока по необходимости plan dependencies validated, candidate mutation isolated, RNG ordering explicit, long work cancellable, metadata semantic вместо raw file offsets, provider lifetime safe across host unload, failure не публикует partial world, а эта страница обновлена вместе с `docs/en/world-generation.md`.
