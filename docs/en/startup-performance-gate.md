# Startup performance gate

TerraRuntime keeps `.wld` as the canonical world and treats `.runtime-world` as a disposable startup image. The startup gate exists to keep those two paths measurable instead of hiding every cost behind one total startup number.

## Measurements

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

## Metric contract

The gate is comparative, not a fixed latency SLA. CI validates that every required stage is present, finite, non-negative and exercised on an official world. Absolute timings vary with GitHub runner hardware, filesystem cache state and CPU scheduling, so CI must not fail merely because a run is slower than an arbitrary millisecond threshold.

`cache_validation_ms` is the isolated structural cache-validation cost from the profiler. `cache_validated_load_ms` is the authoritative source-validated production cache load and includes reconstruction work. Keep both: the first answers where validation costs time, while the second is the real warm-load wall clock.

`index_construction_ms` currently covers the production `WorldSectionEncodingContext` plus `PlayerBootstrapPacketSet.Create` path. Those are startup structures used to encode and serve the initial player section synchronization. Future expensive indexes should extend this phase or gain their own named metric instead of disappearing into `NetworkReady`.

## Local usage

```bash
dotnet run --project tools/TerraRuntime.StartupGate/TerraRuntime.StartupGate.csproj -c Release -- /path/to/world.wld
```

The command creates its runtime-world cache in a temporary directory and removes it afterward. It never replaces the canonical `.wld` and does not reuse the server's normal cache path.
