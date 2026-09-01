# Executable architecture gates

[Русский](../ru/architecture-gates.md) · [Architecture](architecture.md) · [Gameplay decomposition roadmap](../roadmap/gameplay-decomposition-and-catalogs.md)

## Purpose

TerraRuntime architecture rules are executable constraints, not only diagrams. `RuntimeArchitectureBoundaryTests` runs in the ordinary `TerraRuntime.Tests` suite, which the main CI workflow executes after the Release build.

The tests inspect compiled assembly metadata. This makes the gate independent from source-file layout and catches a dependency as soon as it becomes a real runtime assembly reference.

## Protected boundaries

### External server/runtime independence

Every `TerraRuntime.*` assembly reachable from the production roots is inspected for direct references whose names begin with `Terraria`, `TShock`, `OTAPI` or `Vega`.

Those dependencies are forbidden. TerrariaServer 1.4.5.8, TShock/OTAPI and Vega can be reference material or an external host, but they are not runtime implementation dependencies of TerraRuntime.

### Multiplicity adapter isolation

`Multiplicity` is allowed only in `TerraRuntime.Protocol.Multiplicity`. The gate asserts both sides of the contract:

- the adapter still references the Multiplicity package;
- no other production assembly references a `Multiplicity*` assembly directly.

This protects the protocol abstraction from slowly dissolving as convenient packet types leak into gameplay or host code.

### Foundation dependency direction

The gate keeps a deliberately small allow-set for the low-level projects:

| Assembly | Allowed direct `TerraRuntime*` references |
| --- | --- |
| `TerraRuntime.Contracts` | none |
| `TerraRuntime.Core` | `TerraRuntime.Contracts` |
| `TerraRuntime.HostContracts` | `TerraRuntime.Contracts` |
| `TerraRuntime.Protocol` | none |
| `TerraRuntime.World` | `TerraRuntime.Contracts` |
| `TerraRuntime.Network` | `TerraRuntime.Contracts`, `TerraRuntime.Protocol` |
| `TerraRuntime.Protocol.Multiplicity` | `TerraRuntime.Contracts`, `TerraRuntime.Protocol`, `TerraRuntime.World` |

The test rejects new direct production dependencies outside this allow-set. It does not require every allowed edge to exist forever, so removing coupling does not require changing the test.

### Host contract surface

`TerraRuntime.HostContracts` may expose its own types, shared `TerraRuntime.Contracts` types and third-party/framework presentation types that are intentionally part of the host API. It may not expose types from concrete TerraRuntime implementation assemblies such as Core, World, Network, protocol adapters or the server executable.

The test walks exported types, bases/interfaces and public constructor, method, property, field and event signatures, recursively including generic and element types.

## CI behavior

The main workflow runs:

```text
dotnet build build/TerraRuntime.slnx -c Release --no-restore
dotnet test tests/TerraRuntime.Tests/TerraRuntime.Tests.csproj -c Release --no-build
```

Therefore an architecture violation fails the same required build/test path as behavioral regressions. No separate source-regex job is needed for these assembly-level rules.

## Changing a boundary

If a new dependency is genuinely required, change the architecture documentation and the executable allow-set in the same reviewed commit. Do not widen the allow-set merely to make a red test disappear. The failure is the point: dependency direction should be an explicit decision rather than an accidental side effect of adding a convenient reference.
