using System.Reflection;
using TerraRuntime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SectionCacheRebuildPipelineTests
{
    [Fact]
    public async Task Rebuilds_and_publishes_current_dirty_section_off_thread()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);

        WorldTile tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireRed;
        world.Tiles.Set(x, y, tile);
        long revision = world.Tiles.GetSectionVersion(section);

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1);
        pipeline.Start();

        SectionCacheRebuildPipelineSnapshot snapshot = await WaitForAsync(
            pipeline,
            static value => value.PublishedFrames >= 1);

        Assert.Equal(0, snapshot.DirtyBacklog);
        Assert.Equal(1, snapshot.SubmittedRebuilds);
        Assert.Equal(1, snapshot.EncodedFrames);
        Assert.Equal(1, snapshot.PublishedFrames);
        Assert.Equal(0, snapshot.EncodeFailures);
        Assert.Equal(0, snapshot.StaleResults);
        Assert.Equal(0, snapshot.RejectedSubmissions);
        Assert.InRange(snapshot.CacheEntries, 1, snapshot.CacheMaximumEntries);
        Assert.Equal(world.Header.Dimensions.SectionCount, snapshot.CacheMaximumEntries);
        Assert.True(snapshot.CacheBytes > 0);
        Assert.True(packets.TryGetCachedSectionFrame(section, revision, out ReadOnlyMemory<byte> frame));
        Assert.Equal((byte)TerrariaMessageId.TileSection, frame.Span[2]);
    }

    [Fact]
    public async Task Deduplicates_concurrent_on_demand_misses_into_one_worker_rebuild()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        WorldSectionId section = FindUncachedSection(world, packets);
        using var encodeStarted = new ManualResetEventSlim(false);
        using var releaseEncode = new ManualResetEventSlim(false);
        int encodeCalls = 0;

        SectionCacheRebuildResult Encode(WorldSectionTileSnapshot snapshot)
        {
            Interlocked.Increment(ref encodeCalls);
            encodeStarted.Set();
            releaseEncode.Wait(TestContext.Current.CancellationToken);
            return new SectionCacheRebuildResult(
                snapshot.Section,
                snapshot.Revision,
                WorldSectionPacketEncodeResult.Encoded,
                new byte[] { 4, 0, (byte)TerrariaMessageId.TileSection, 0x5a },
                TimeSpan.Zero,
                Error: null);
        }

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            Encode);
        pipeline.Start();

        Task<ReadOnlyMemory<byte>> first = StartLookupAsync(packets, section);
        Task<ReadOnlyMemory<byte>> second = StartLookupAsync(packets, section);

        try
        {
            SectionCacheRebuildPipelineSnapshot requested = await WaitForObservedAsync(
                pipeline,
                static value => value.OnDemandRequests >= 2);
            Assert.Equal(1, requested.OnDemandUniqueRequests);
            Assert.Equal(1, requested.OnDemandDeduplicatedRequests);
            Assert.Equal(1, requested.OnDemandPendingRequests);
            Assert.Equal(2, requested.CacheMisses);
            Assert.Equal(2, requested.CacheWaits);

            pipeline.Tick();
            Assert.True(encodeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(1, pipeline.Snapshot.SubmittedRebuilds);
            releaseEncode.Set();

            SectionCacheRebuildPipelineSnapshot published = await WaitForAsync(
                pipeline,
                static value => value.PublishedFrames >= 1 && value.CacheWaitCompletions >= 2);
            ReadOnlyMemory<byte>[] frames = await Task.WhenAll(first, second);

            Assert.Equal(1, encodeCalls);
            Assert.Equal(1, published.SubmittedRebuilds);
            Assert.Equal(1, published.PublishedFrames);
            Assert.Equal(0, published.OnDemandPendingRequests);
            Assert.Equal(2, published.CacheWaitCompletions);
            Assert.Equal(0, published.CacheWaitTimeouts);
            Assert.All(frames, frame => Assert.Equal(0x5a, frame.Span[3]));
        }
        finally
        {
            releaseEncode.Set();
        }
    }

    [Fact]
    public async Task Stale_cached_section_waits_for_worker_and_records_stale_lookup()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);

        SectionCacheRebuildResult Encode(WorldSectionTileSnapshot snapshot) =>
            new(
                snapshot.Section,
                snapshot.Revision,
                WorldSectionPacketEncodeResult.Encoded,
                new byte[] { 4, 0, (byte)TerrariaMessageId.TileSection, 0x6b },
                TimeSpan.Zero,
                Error: null);

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            Encode);
        pipeline.Start();

        WorldTile tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireRed;
        world.Tiles.Set(x, y, tile);

        Task<ReadOnlyMemory<byte>> lookup = StartLookupAsync(packets, section);
        SectionCacheRebuildPipelineSnapshot requested = await WaitForObservedAsync(
            pipeline,
            static value => value.OnDemandRequests >= 1);
        Assert.Equal(1, requested.CacheMisses);
        Assert.Equal(1, requested.CacheStaleReads);
        Assert.Equal(1, requested.CacheWaits);

        SectionCacheRebuildPipelineSnapshot published = await WaitForAsync(
            pipeline,
            static value => value.PublishedFrames >= 1 && value.CacheWaitCompletions >= 1);
        ReadOnlyMemory<byte> frame = await lookup;

        Assert.Equal(0x6b, frame.Span[3]);
        Assert.Equal(1, published.OnDemandUniqueRequests);
        Assert.Equal(0, published.OnDemandPendingRequests);
        Assert.Equal(1, published.CacheWaitCompletions);
        Assert.Equal(0, published.CacheWaitTimeouts);
    }

    [Fact]
    public async Task Discards_stale_worker_result_and_rebuilds_latest_revision()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);
        using var firstEncodeStarted = new ManualResetEventSlim(false);
        using var releaseFirstEncode = new ManualResetEventSlim(false);
        int encodeCalls = 0;

        SectionCacheRebuildResult Encode(WorldSectionTileSnapshot snapshot)
        {
            int call = Interlocked.Increment(ref encodeCalls);
            if (call == 1)
            {
                firstEncodeStarted.Set();
                releaseFirstEncode.Wait(TestContext.Current.CancellationToken);
            }

            byte[] frame =
            [
                4, 0,
                (byte)TerrariaMessageId.TileSection,
                unchecked((byte)snapshot.Revision)
            ];
            return new SectionCacheRebuildResult(
                snapshot.Section,
                snapshot.Revision,
                WorldSectionPacketEncodeResult.Encoded,
                frame,
                TimeSpan.Zero,
                Error: null);
        }

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            Encode);
        pipeline.Start();

        WorldTile tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireBlue;
        world.Tiles.Set(x, y, tile);
        long firstRevision = world.Tiles.GetSectionVersion(section);
        pipeline.Tick();
        Assert.True(firstEncodeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireGreen;
        world.Tiles.Set(x, y, tile);
        long latestRevision = world.Tiles.GetSectionVersion(section);
        Assert.NotEqual(firstRevision, latestRevision);
        releaseFirstEncode.Set();

        SectionCacheRebuildPipelineSnapshot snapshot = await WaitForAsync(
            pipeline,
            static value => value.StaleResults >= 1 && value.PublishedFrames >= 1);

        Assert.True(snapshot.SubmittedRebuilds >= 2);
        Assert.Equal(1, snapshot.StaleResults);
        Assert.Equal(1, snapshot.PublishedFrames);
        Assert.Equal(0, snapshot.RejectedSubmissions);
        Assert.False(packets.TryGetCachedSectionFrame(section, firstRevision, out _));
        Assert.True(packets.TryGetCachedSectionFrame(section, latestRevision, out ReadOnlyMemory<byte> frame));
        Assert.Equal(unchecked((byte)latestRevision), frame.Span[3]);
    }

    [Fact]
    public void Does_not_capture_more_snapshots_while_all_in_flight_slots_are_occupied()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        using var encodeStarted = new ManualResetEventSlim(false);
        using var releaseEncode = new ManualResetEventSlim(false);

        SectionCacheRebuildResult Encode(WorldSectionTileSnapshot snapshot)
        {
            encodeStarted.Set();
            releaseEncode.Wait(TestContext.Current.CancellationToken);
            return new SectionCacheRebuildResult(
                snapshot.Section,
                snapshot.Revision,
                WorldSectionPacketEncodeResult.Encoded,
                new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection },
                TimeSpan.Zero,
                Error: null);
        }

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1,
            Encode);
        pipeline.Start();

        WorldTile tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireYellow;
        world.Tiles.Set(x, y, tile);
        pipeline.Tick();
        Assert.True(encodeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireBlue;
        world.Tiles.Set(x, y, tile);
        pipeline.Tick();
        Assert.Equal(2, pipeline.Snapshot.InFlight);

        tile = world.Tiles.Get(x, y);
        tile.Flags ^= WorldTileFlags.WireGreen;
        world.Tiles.Set(x, y, tile);
        Assert.Equal(1, world.Tiles.DirtySections.DirtyCount);

        long capturedBefore = pipeline.Snapshot.CapturedSnapshots;
        for (int i = 0; i < 8; i++)
            pipeline.Tick();

        SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
        Assert.Equal(capturedBefore, snapshot.CapturedSnapshots);
        Assert.Equal(2, snapshot.InFlight);
        Assert.Equal(1, snapshot.DirtyBacklog);
        Assert.Equal(0, snapshot.RejectedSubmissions);
        releaseEncode.Set();
    }

    private static Task<ReadOnlyMemory<byte>> StartLookupAsync(
        PlayerBootstrapPacketSet packets,
        WorldSectionId section) =>
        Task.Run(
            () =>
            {
                Assert.True(packets.TryGetOrRequestSectionFrame(section, out ReadOnlyMemory<byte> frame));
                return frame;
            },
            TestContext.Current.CancellationToken);

    private static async Task<SectionCacheRebuildPipelineSnapshot> WaitForObservedAsync(
        SectionCacheRebuildPipeline pipeline,
        Func<SectionCacheRebuildPipelineSnapshot, bool> predicate)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Section cache request was not observed before the test deadline.");
    }

    private static async Task<SectionCacheRebuildPipelineSnapshot> WaitForAsync(
        SectionCacheRebuildPipeline pipeline,
        Func<SectionCacheRebuildPipelineSnapshot, bool> predicate)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            pipeline.Tick();
            SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        SectionCacheRebuildPipelineSnapshot final = pipeline.Snapshot;
        throw new TimeoutException(
            $"Section cache rebuild did not reach the expected state. " +
            $"dirty={final.DirtyBacklog}, inFlight={final.InFlight}, submitted={final.SubmittedRebuilds}, " +
            $"published={final.PublishedFrames}, stale={final.StaleResults}, failures={final.EncodeFailures}.");
    }

    private static WorldSectionId FindUncachedSection(
        WorldFileData world,
        PlayerBootstrapPacketSet packets)
    {
        for (int index = 0; index < world.Header.Dimensions.SectionCount; index++)
        {
            WorldSectionId section = TerrariaSectionGeometry.FromLinearIndex(world.Header.Dimensions, index);
            long revision = world.Tiles.GetSectionVersion(section);
            if (!packets.TryGetCachedSectionFrame(section, revision, out _))
                return section;
        }

        throw new InvalidOperationException("The generated test world unexpectedly cached every network section.");
    }

    private static WorldFileData LoadCompleteWorld()
    {
        byte[] source = (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;
        WorldFileLoadLimits limits = (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        return Assert.IsType<WorldFileData>(loaded);
    }

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
