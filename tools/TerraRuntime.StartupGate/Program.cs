using System.Diagnostics;
using TerraRuntime.Application;
using TerraRuntime.World;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: TerraRuntime.StartupGate <world.wld>");
    return 2;
}

string worldPath = Path.GetFullPath(args[0]);
if (!File.Exists(worldPath))
{
    Console.Error.WriteLine($"World file not found: {worldPath}");
    return 3;
}

WorldFileLoadLimits limits = CreateLimits();
string scratchDirectory = Path.Combine(
    Path.GetTempPath(),
    "terraruntime-startup-gate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratchDirectory);
string cachePath = Path.Combine(scratchDirectory, Path.GetFileNameWithoutExtension(worldPath) + ".runtime-world");

long allocatedAtStart = GC.GetTotalAllocatedBytes(precise: false);
int gen0AtStart = GC.CollectionCount(0);
int gen1AtStart = GC.CollectionCount(1);
int gen2AtStart = GC.CollectionCount(2);
long coldStart = Stopwatch.GetTimestamp();

try
{
    long stageStart = Stopwatch.GetTimestamp();
    byte[] canonicalBytes = File.ReadAllBytes(worldPath);
    TimeSpan fileRead = Stopwatch.GetElapsedTime(stageStart);

    WorldFileLoadDiagnostic canonicalDiagnostic = WorldFileLoader.TryLoad(
        canonicalBytes,
        limits,
        out WorldFileData? canonicalWorld,
        out WorldFileLoadProfile canonicalProfile);
    if (!canonicalDiagnostic.IsLoaded || canonicalWorld is null)
    {
        Console.Error.WriteLine(
            $"Canonical startup load failed: result={canonicalDiagnostic.Result}, stage={canonicalDiagnostic.Stage}, code={canonicalDiagnostic.StageResultCode}.");
        return 10;
    }

    TimeSpan coldWorldReady = Stopwatch.GetElapsedTime(coldStart);

    stageStart = Stopwatch.GetTimestamp();
    WorldSectionEncodingContext sectionContext = WorldSectionEncodingContext.Capture(canonicalWorld);
    PlayerBootstrapPacketSet bootstrapPackets = PlayerBootstrapPacketSet.Create(canonicalWorld);
    TimeSpan indexConstruction = Stopwatch.GetElapsedTime(stageStart);
    GC.KeepAlive(sectionContext);
    GC.KeepAlive(bootstrapPackets);

    if (!RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp sourceStamp))
    {
        Console.Error.WriteLine($"Could not stat canonical world: {worldPath}");
        return 11;
    }

    stageStart = Stopwatch.GetTimestamp();
    RuntimeWorldSnapshotWriteDiagnostic cacheWrite = RuntimeWorldSnapshotCache.TryWriteAtomic(
        cachePath,
        canonicalBytes,
        sourceStamp,
        canonicalWorld);
    TimeSpan cacheWriteDuration = Stopwatch.GetElapsedTime(stageStart);
    if (!cacheWrite.IsWritten)
    {
        Console.Error.WriteLine($"Runtime cache build failed: result={cacheWrite.Result}.");
        return 12;
    }

    long warmWorldReadyStart = Stopwatch.GetTimestamp();
    RuntimeWorldSnapshotLoadDiagnostic validatedDiagnostic = RuntimeWorldSnapshotCache.TryLoadValidatedSource(
        cachePath,
        worldPath,
        limits,
        out WorldFileData? validatedWorld);
    TimeSpan cacheValidatedLoad = Stopwatch.GetElapsedTime(warmWorldReadyStart);
    if (!validatedDiagnostic.IsLoaded || validatedWorld is null)
    {
        Console.Error.WriteLine(
            $"Validated runtime cache load failed: result={validatedDiagnostic.Result}, code={validatedDiagnostic.DetailCode}.");
        return 13;
    }

    RuntimeWorldSnapshotLoadDiagnostic profileDiagnostic = RuntimeWorldSnapshotProfiler.TryLoad(
        cachePath,
        sourceStamp,
        limits,
        RuntimeWorldCacheReadOptions.Default,
        out WorldFileData? profiledWorld,
        out RuntimeWorldSnapshotLoadProfile cacheProfile);
    if (!profileDiagnostic.IsLoaded || profiledWorld is null)
    {
        Console.Error.WriteLine(
            $"Runtime cache profiling failed: result={profileDiagnostic.Result}, code={profileDiagnostic.DetailCode}.");
        return 14;
    }

    if (validatedWorld.Header.WorldId != canonicalWorld.Header.WorldId ||
        profiledWorld.Header.WorldId != canonicalWorld.Header.WorldId ||
        validatedWorld.Tiles.Count != canonicalWorld.Tiles.Count ||
        profiledWorld.Tiles.Count != canonicalWorld.Tiles.Count)
    {
        Console.Error.WriteLine("Startup gate loaded inconsistent canonical/cache world identities.");
        return 15;
    }

    long allocatedBytes = Math.Max(0L, GC.GetTotalAllocatedBytes(precise: false) - allocatedAtStart);
    int gen0Collections = Math.Max(0, GC.CollectionCount(0) - gen0AtStart);
    int gen1Collections = Math.Max(0, GC.CollectionCount(1) - gen1AtStart);
    int gen2Collections = Math.Max(0, GC.CollectionCount(2) - gen2AtStart);

    TimeSpan canonicalTileReconstruction = canonicalProfile.TileAllocation + canonicalProfile.TileDecode;
    TimeSpan cacheStructuralValidation = cacheProfile.Header + cacheProfile.ShardTable;
    TimeSpan cacheTileReconstruction = cacheProfile.TileAllocation + cacheProfile.TileWall;
    TimeSpan cacheLiquidPostLoad = cacheProfile.LiquidIo + cacheProfile.LiquidHash +
        cacheProfile.LiquidDecode + cacheProfile.LiquidRestore;
    TimeSpan cachePreparedState = cacheProfile.PreparedIo + cacheProfile.PreparedHash + cacheProfile.PreparedDecode;

    Console.WriteLine(FormattableString.Invariant($"startup_gate world={Path.GetFileName(worldPath)} file_read_ms={fileRead.TotalMilliseconds:F3} wld_total_ms={canonicalProfile.Total.TotalMilliseconds:F3} wld_tile_reconstruction_ms={canonicalTileReconstruction.TotalMilliseconds:F3} wld_non_tile_ms={canonicalProfile.NonTileSections.TotalMilliseconds:F3} cache_write_ms={cacheWriteDuration.TotalMilliseconds:F3} cache_validated_load_ms={cacheValidatedLoad.TotalMilliseconds:F3} cache_validation_ms={cacheStructuralValidation.TotalMilliseconds:F3} cache_parallel_wall_ms={cacheProfile.ParallelWall.TotalMilliseconds:F3} cache_tile_reconstruction_ms={cacheTileReconstruction.TotalMilliseconds:F3} cache_liquid_postload_ms={cacheLiquidPostLoad.TotalMilliseconds:F3} cache_prepared_state_ms={cachePreparedState.TotalMilliseconds:F3} index_construction_ms={indexConstruction.TotalMilliseconds:F3} world_ready_cold_ms={coldWorldReady.TotalMilliseconds:F3} world_ready_warm_ms={cacheValidatedLoad.TotalMilliseconds:F3} allocated_mib={allocatedBytes / (1024d * 1024d):F3} gen0_collections={gen0Collections} gen1_collections={gen1Collections} gen2_collections={gen2Collections} cache_shards={cacheProfile.ShardCount} cache_tile_bytes={cacheProfile.TilePayloadBytes} cache_prepared_bytes={cacheProfile.PreparedPayloadBytes} cache_liquid_bytes={cacheProfile.LiquidPayloadBytes}"));

    return 0;
}
finally
{
    try
    {
        Directory.Delete(scratchDirectory, recursive: true);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
    }
}

static WorldFileLoadLimits CreateLimits() =>
    new(
        MaxTileCount: 8_400L * 2_400,
        MaxItemsPerChest: 100,
        MaxTotalChestItems: 1_000_000,
        MaxTextBytesPerSign: 64 * 1024,
        MaxTotalSignTextBytes: 64L * 1024 * 1024,
        Npcs: new WorldFileNpcDecodeOptions(
            MaxShimmeredTownNpcIndices: 1_024,
            MaxShimmerIndexExclusive: 1_024,
            MaxTownNpcs: 1_024,
            MaxPersistentNpcs: 4_096,
            MaxNameBytesPerTownNpc: 4 * 1024,
            MaxTotalNameBytes: 4L * 1024 * 1024),
        MaxTileEntities: 100_000,
        MaxPressurePlates: 1_000_000,
        MaxTownRooms: VanillaWorldFormat326.NpcTypeCount,
        Bestiary: new WorldFileBestiaryLimits(
            MaxKillEntries: 100_000,
            MaxSightEntries: 100_000,
            MaxChatEntries: 100_000,
            MaxPersistentIdBytes: 4 * 1024,
            MaxTotalPersistentIdBytes: 64L * 1024 * 1024),
        RuntimeMetadata: new WorldFileRuntimeMetadataLimits(
            MaxStringBytes: 64 * 1024,
            MaxTotalStringBytes: 64L * 1024 * 1024,
            MaxAnglerNames: 4_096,
            MaxBannerEntries: 8_192,
            MaxPartyNpcEntries: 4_096,
            MaxManifestBytes: 4 * 1024 * 1024));
