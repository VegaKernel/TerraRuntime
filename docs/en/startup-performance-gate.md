# Startup performance gate

TerraRuntime keeps `.wld` as the canonical world and treats `.runtime-world` as a disposable startup image. The startup gate exists to keep those two paths measurable instead of hiding every cost behind one total startup number.

## Measurements

The canonical diagnostic path must run `VanillaWorldLiquidLoadInitializer1458.TryPrepare` before cache creation, exactly as primary startup does. `wld_liquid_prepare_ms` measures this separately; `world_ready_cold_ms` includes it. Unsupported preparation fails with its tile diagnostic rather than forging the cache's prepared marker. `StartupGate` and `TileLocate` now participate in the normal solution build so API drift cannot hide until a separate workflow runs.

Executable readiness checks explicitly set `TERRARUNTIME_LOG_CONSOLE_LEVEL=Information` and use `--no-tui`: the production console defaults to errors and otherwise does not print the informational listening marker. This changes diagnostic configuration only, not server logging defaults or readiness semantics.

The gate uses two complementary sources.

`TerraRuntime.StartupGate` performs a deterministic diagnostic pass over one real Terraria 1.4.5.8 world and emits a single machine-readable `startup_gate` line. It measures and the detailed stage breakdown is verified by CJ.

- canonical file read;
- canonical tile allocation + tile decode (`wld_tile_reconstruction_ms`);
- canonical non-tile decode;
- runtime-cache build;
- validated runtime-cache load against the canonical source;
- isolated cache structure validation (`header + shard table`);
- runtime-cache tile reconstruction;
- liquid queue I/O/hash/decode/restore;
- prepared runtime-state I/O/hash/decode;
- construction of the production section-encoding context and initial bootstrap section cache;
- cold and warm `WorldReady` boundaries;
- total managed allocations and Gen0/Gen1/Gen2 collection deltas for the diagnostic pass.

`RuntimeWorldSnapshotProfiler` is deliberately a second diagnostic pass. Production cache loading remains uninstrumented on its hot path; the profiler first times the real loader, then repeats the cache stages in isolation. Aggregate worker timings can therefore exceed wall time when shard reads run concurrently.

The GitHub Actions `Startup Performance Gate` additionally launches the real TerraRuntime server twice against an official generated world. It measures the process boundary from launch until the listening message, once without runtime caches and once with warm caches. This is the executable `NetworkReady` proof.

## Generated-world startup failures

The 2026-09-08 Underworld continuation reproduced another refusal on fresh canonical Small seed `1458`: `UnsupportedLiquidDeathTile` at `(3125,951,227)`. The Dye Plants writer used an ordinary atlas stride instead of `PlaceDye`'s $34\,\text{pixels}$; loading also lacked the verified single-cell liquid-death path. The shared frame contract and bounded loading removal now cover valid styles `0..11`, with malformed frames still refused. New Small/Medium/Large regressions exercise generation, real `.wld` composition/loading and post-load liquid preparation, not just structural validation. The corrected Small file reached the real TerraRuntime CoreCLR listener and independently loaded in official Windows TerrariaServer `1.4.5.8`. This does not establish startup support for every seed.

Successful generation, atomic `.wld` publication and `--worldgen-create-smoke` do not prove that TerraRuntime can start that world. Primary startup subsequently loads the canonical file and performs post-load liquid preparation before cache admission, player bootstrap and the listener. An official TerrariaServer load is independent world-file evidence, but does not exercise TerraRuntime's preparation boundary. Acceptance must also reach the real TerraRuntime `NetworkReady` boundary with the generated file.

The 2026-09-07 investigation reproduced startup refusal after successful vanilla generation in both Windows shipping profiles. The retained logs reported `Post-load liquid preparation failed: result=UnsupportedLiquidDeathTile`, not a generation exception:

| World / profile | Seed | First reported tile `(x,y,type)` | Local evidence log |
| --- | --- | --- | --- |
| `Worlds/t.wld`, NativeAOT, Small | `8364953747496893878` | `(220,883,28)` | `artifacts/native-aot/win-x64/logs/runtime-20260907-170322574-75952-0000.jsonl` |
| `Worlds/test.wld`, CoreCLR, Small | `3679097605675226385` | `(359,924,61)` | `artifacts/coreclr/win-x64/logs/runtime-20260907-170408660-81872-0000.jsonl` |
| Detached Large reproduction, NativeAOT | `8675309` | `(174,1944,12)` | `.cache/tz36-native-win/logs/runtime-20260907-170635386-53780-0000.jsonl` |

These diagnostics establish an application-level fail-closed load refusal; they do not establish an out-of-memory failure or an OS/native process crash. The Large reproduction failed identically without the TUI. Unsupported liquid-death semantics must be implemented and verified at the existing load boundary, not bypassed to make startup appear successful. This evidence is a reproduction baseline, not a claim that every seed or world now loads.

The startup TUI previously hid the useful error: terminal ownership buffered console delivery, progress text replaced the actual cause with a generic recovery message, and the final framebuffer could disappear before the user saw it. Startup telemetry now retains the last error independently of subsequent progress, bounded to 1024 characters with control characters sanitized. On unsuccessful startup, it prints that cause and the exit code exactly once after the terminal driver releases the terminal; a timed-out join defers printing until the UI thread actually releases it. Normal successful exit and successful checkpoint recovery do not replay old errors, and `--no-tui` keeps its existing single console diagnostic. No artificial display delay, automatic retry or alternate loading path is introduced.

## Metric contract

The gate is comparative, not a fixed latency SLA. CI validates that every required stage is present, finite, non-negative and exercised on an official world. Absolute timings vary with GitHub runner hardware, filesystem cache state and CPU scheduling, so CI must not fail merely because a run is slower than an arbitrary millisecond threshold.

`cache_validation_ms` is the isolated structural cache-validation cost from the profiler. `cache_validated_load_ms` is the authoritative source-validated production cache load and includes reconstruction work. Keep both: the first answers where validation costs time, while the second is the real warm-load wall clock.

`index_construction_ms` currently covers the production `WorldSectionEncodingContext` plus `PlayerBootstrapPacketSet.Create` path. Those are startup structures used to encode and serve the initial player section synchronization. Future expensive indexes should extend this phase or gain their own named metric instead of disappearing into `NetworkReady`.

## Local usage

The separate empty-runtime **steady-state** allocation test actively warms its measured dispatch loop for at least $500\,\mathrm{ms}$, then measures one uninterrupted $1024\,\text{tick}$ batch against the unchanged $4096\,\mathrm{byte}$ budget. It does not retry or select the lowest allocation sample. This is test warmup policy, not a runtime scheduling parameter or a startup-cost exemption. A short fixed call count alone can overlap delayed [CoreCLR tier promotion](https://github.com/dotnet/runtime/blob/main/docs/design/features/tiered-compilation.md). Diagnostic OSR/tiering switches are not retained in the project configuration; NativeAOT acceptance remains separate.

`RuntimeWorldItemStore.CopyActive` avoids scanning all slots for an empty store only after validating the same seqlock version. Empty snapshots do not clear the caller's buffer, and subsequent allocations/removals remain visible. A local $200000\,\text{call}$ warmed microbenchmark decreased from about $98\,\mathrm{ms}$ to $3.5\,\mathrm{ms}$ with zero measured allocations in both versions; populated scans were approximately unchanged. This result is not evidence that the separate intermittent allocation failure is fixed.

```bash
dotnet run --project tools/TerraRuntime.StartupGate/TerraRuntime.StartupGate.csproj -c Release -- /path/to/world.wld
```

The command creates its runtime-world cache in a temporary directory and removes it afterward. It never replaces the canonical `.wld` and does not reuse the server's normal cache path.
