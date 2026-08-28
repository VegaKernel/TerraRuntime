# NativeAOT dependency audit

This document records the NativeAOT/trimming review of NuGet dependencies found across the accessible `TerraZ-Team` C# projects as of 2026-08-27.

## Runtime policy

TerraRuntime is **AOT-first**.

- The production game engine should remain compatible with full .NET 11 NativeAOT publication.
- CoreCLR remains a development, debugging, profiling and benchmark fallback.
- Dynamic C# plugins, `AssemblyLoadContext`, hot reload and arbitrary assembly loading belong to the Vega process, not TerraRuntime.
- A third-party dependency may enter the TerraRuntime production graph only after an AOT publish smoke passes with no unexplained trim/AOT warnings and the relevant runtime path is exercised.
- A library being usable by Vega does not make it suitable for TerraRuntime.

## Audit scope

GitHub code search currently reports 52 TerraZ-Team `.csproj` files containing `PackageReference`. Additional newer/unindexed repositories were inspected manually, including StateGuard, LeashedEntityGuard, BadPig, AdditionalPylons, ClientPlatformDetector, FakeProvider and RegionExt.Core.

The dependency graph is much smaller than the repository count because most TerraZ plugins depend only on TShock/TZ.TShockAPI.

## Classification

Legend:

- **Green**: NativeAOT-friendly or explicitly supported; still requires TerraRuntime CI smoke before use.
- **Yellow**: usable only with constraints or needs an exact-version AOT smoke.
- **Red**: do not put in the TerraRuntime production graph.
- **Vega-only**: may be perfectly useful, but belongs outside the game engine.

| Package | Version observed | Status | Decision |
| --- | --- | --- | --- |
| `Multiplicity` | 2.6.1, 2.6.2; current VegaKernel 2.6.3 | **Green candidate** | Keep as TerraRuntime protocol bootstrap. The package has no third-party `PackageReference` graph and no known runtime code generation requirement. Migrate the library itself to .NET 11, set `IsAotCompatible=true`, enable trim/AOT analyzers and keep a real NativeAOT publish gate. |
| `Terminal.Gui` | 2.4.17 | **Green / runtime UI** | Pinned for the opt-in local TUI. The 2.4.16 line fixed a trimming/NativeAOT configuration regression, so upgrades remain gated. TerraRuntime exercises a real `--tui-smoke` with the ANSI driver in normal CI and in both `linux-x64` and `win-x64` NativeAOT jobs; all three paths have passed for this exact version. Keep the same publish + exercised-smoke gate on every upgrade. |
| `GTranslate` | 2.2.8 | **Green / Vega-only by role** | Exact v2.2.8 declares `IsTrimmable=true` and `IsAotCompatible=true` for net8-compatible targets. Its JSON path is based on `System.Text.Json`. AOT itself is not a blocker. Translation/network policy still belongs in Vega, not the game simulation. |
| `MySqlConnector` | 2.6.1 | **Green / preferably Vega-only** | NativeAOT and trimming have been officially supported since the 2.4/2.5 line and later releases maintain AOT coverage. If MySQL access is ever required in a fixed-function AOT process, prefer this over `MySql.Data`. Database/business state should normally stay in Vega. |
| `Npgsql` | 9.0.3 | **Green with constraints / preferably Vega-only** | Npgsql supports NativeAOT/trimming from 8.0. Avoid explicitly dynamic APIs such as dynamic JSON, records-as-tuples, unmapped types and runtime `Type` composite mapping. Normal ADO.NET paths are suitable. |
| `Telegram.Bot` | 22.9.5.3 | **Green / Vega-only** | NativeAOT/trimming support was added in 22.2. No reason to put Telegram integration in TerraRuntime. |
| `SixLabors.ImageSharp` | 3.1.12 | **Yellow-green / tooling only** | Exact v3.1.12 declares `IsTrimmable=true` and is fully managed, but does not explicitly declare `IsAotCompatible` in that project file. Require an exact-version publish/runtime smoke. Image rendering belongs in tooling/operations rather than the game engine. Prefer a current patched release before new use. |
| `Microsoft.Data.Sqlite` | 9.0.0 | **Yellow** | ADO.NET SQLite has trimming/AOT work, but NativeAOT deployment has had unresolved `e_sqlite3` native-library packaging/runtime issues. If needed, test each RID with an actual open/read/write smoke. Do not make the engine depend on it by default. |
| `MySql.Data` | 9.1.0 | **Yellow-red** | No strong NativeAOT guarantee was found for this exact line, and Oracle Connector/NET has had NativeAOT failures. Prefer `MySqlConnector`. Keep database integration outside TerraRuntime. |
| `Discord.Net` | 3.20.1 | **Yellow-red / Vega-only** | No explicit NativeAOT compatibility contract was found for the exact version during this audit. Command/interaction discovery is also a reflection-sensitive area. Keep Discord integration in Vega. |
| `Newtonsoft.Json` | 13.0.3 | **Red for new TerraRuntime code** | The project enables AOT/trim analyzers but intentionally does not declare itself `IsAotCompatible`/`IsTrimmable`; generic reflection-based serialization remains a trimming hazard. Use source-generated `System.Text.Json` in TerraRuntime. |
| `Ionic.Zip` | 1.9.1.8 | **Red** | Deprecated, unmaintained, .NET Framework-era package. Replace with `System.IO.Compression`. |
| `System.Diagnostics.PerformanceCounter` | 9.0.0 | **Red for runtime architecture** | Windows-specific legacy metrics mechanism and unnecessary for a cross-platform game engine. Use modern `System.Diagnostics.Metrics`, process/GC/runtime counters and structured telemetry. |
| `TShock` | 6.1.3 | **Red** | Plugin/server framework layer with dynamic plugin assumptions. Behavior may be mined as a reference; the package must not enter TerraRuntime. |
| `TZ.TShockAPI` | 6.1.3 | **Red** | Same boundary as TShock. Extremely common across TerraZ plugins, but belongs outside TerraRuntime. |
| `OTAPI.Upcoming` | 3.3.12 (via TSAPI) | **Red** | Runtime patching/detour architecture conflicts with the fixed NativeAOT engine goal. TerraRuntime replaces this integration layer rather than embedding it. |
| `TZ.FakeProvider.Abstractions` | 1.0.7 observed; repo currently 1.0.9 | **Red as package** | Even the abstractions package directly depends on `TZ.TShockAPI`. Reuse the architectural ideas by defining TerraRuntime-owned provider/scene contracts, not by referencing this package. |
| `TZ.RegionExt.Core` | 1.0.1 | **Red as package** | Directly depends on `TZ.TShockAPI` plus Multiplicity. Region behavior can be ported to Vega/runtime contracts without carrying the TShock dependency graph. |
| `TZ.TelegramBot.Abstractions` | 1.0.3 | **Red for TerraRuntime / Vega-only** | Directly depends on Telegram.Bot and TZ.TShockAPI. It is plugin integration surface, not engine infrastructure. |
| `Microsoft.NET.Test.Sdk` | 17.11.1, 18.7.0 | **Test-only** | No effect on production NativeAOT graph. |
| `xunit` | 2.9.2, 2.9.3 | **Test-only** | No effect on production NativeAOT graph. |
| `xunit.runner.visualstudio` | 2.8.2, 3.1.5 | **Test-only** | No effect on production NativeAOT graph. |

## Direct file references found

These are not NuGet packages but should not silently enter the engine either:

- `FakeGroup.dll` in Donations;
- `WorldSlicer.dll` in FlagCapture.

Any reusable behavior from such assemblies must be ported behind TerraRuntime/Vega-owned contracts. An opaque binary reference is not accepted into the NativeAOT runtime graph.

## Packages not found in the TerraZ-Team PackageReference scan

No direct references were found for the following common candidates during the audit:

- `StackExchange.Redis`;
- `Dapper`;
- `RestSharp`;
- `MessagePack`.

This is not a recommendation for or against those packages; it only means they are not part of the currently observed TerraZ-Team NuGet graph.

## Required CI gate

Every package that enters a shipping TerraRuntime project must pass all of the following:

1. `net11.0` restore/build with trim and AOT analyzers enabled;
2. `dotnet publish -c Release -r linux-x64 -p:PublishAot=true`;
3. equivalent publish on the supported Windows RID;
4. zero unexplained IL2xxx/IL3xxx AOT/trimming warnings;
5. process startup smoke of the native binary;
6. an exercised runtime path for the package, not merely successful linking;
7. dependency and native-library vulnerability scan;
8. repeat the gate on every package upgrade.

A package is not considered safe merely because NativeAOT can link it. The exercised behavior must also run correctly after trimming.

## Recommended TerraRuntime production dependency policy

Keep the engine dependency graph deliberately boring:

```text
TerraRuntime
  +-- .NET 11 BCL
  +-- Multiplicity (temporary/owned protocol boundary)
  +-- narrowly justified AOT-verified libraries only

Vega / external tooling
  +-- databases
  +-- Telegram / Discord / translation integrations
  +-- dynamic plugins
  +-- administration/business integrations
```

The practical result of the TerraZ-Team audit is favorable: there is no large third-party dependency set that forces TerraRuntime back to CoreCLR. The packages that are clearly hostile to the NativeAOT goal are overwhelmingly part of the legacy TShock/TSAPI/plugin layer that TerraRuntime should not reference in the first place.
