using System.Reflection;
using TerraRuntime;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SectionCacheSingleFlightFailureTests
{
    [Fact]
    public async Task On_demand_waiter_joins_existing_background_rebuild_without_second_encode()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);
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
                new byte[] { 4, 0, (byte)TerrariaMessageId.TileSection, 0x41 },
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
        pipeline.Tick();
        Assert.True(encodeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(1, pipeline.Snapshot.SubmittedRebuilds);

        Task<(bool Success, ReadOnlyMemory<byte> Frame)> lookup = StartLookupAsync(packets, section);
        await WaitForObservedAsync(
            pipeline,
            static snapshot => snapshot.OnDemandRequests >= 1 && snapshot.CacheWaits >= 1,
            tickPipeline: false);

        for (int i = 0; i < 8; i++)
            pipeline.Tick();

        Assert.False(lookup.IsCompleted);
        Assert.Equal(1, encodeCalls);
        Assert.Equal(1, pipeline.Snapshot.SubmittedRebuilds);
        Assert.Equal(1, pipeline.Snapshot.OnDemandPendingRequests);

        releaseEncode.Set();
        SectionCacheRebuildPipelineSnapshot completed = await WaitForObservedAsync(
            pipeline,
            static snapshot => snapshot.PublishedFrames >= 1 && snapshot.CacheWaitCompletions >= 1,
            tickPipeline: true);
        (bool success, ReadOnlyMemory<byte> frame) = await lookup;

        Assert.True(success);
        Assert.Equal((byte)0x41, frame.Span[3]);
        Assert.Equal(1, encodeCalls);
        Assert.Equal(1, completed.SubmittedRebuilds);
        Assert.Equal(0, completed.OnDemandPendingRequests);
        Assert.Equal(0, completed.CacheWaitTimeouts);
    }

    [Fact]
    public async Task Failed_on_demand_generation_releases_waiter_without_wait_timeout()
    {
        WorldFileData world = LoadCompleteWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        int x = world.RuntimeMetadata.SpawnX;
        int y = world.RuntimeMetadata.SpawnY;
        WorldSectionId section = TerrariaSectionGeometry.FromTile(world.Header.Dimensions, x, y);
        int encodeCalls = 0;

        SectionCacheRebuildResult Encode(WorldSectionTileSnapshot snapshot)
        {
            Interlocked.Increment(ref encodeCalls);
            return new SectionCacheRebuildResult(
                snapshot.Section,
                snapshot.Revision,
                default,
                ReadOnlyMemory<byte>.Empty,
                TimeSpan.Zero,
                new InvalidDataException("synthetic section encode failure"));
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
        tile.Flags ^= WorldTileFlags.WireGreen;
        world.Tiles.Set(x, y, tile);

        Task<(bool Success, ReadOnlyMemory<byte> Frame)> lookup = StartLookupAsync(packets, section);
        await WaitForObservedAsync(
            pipeline,
            static snapshot => snapshot.OnDemandRequests >= 1 && snapshot.CacheWaits >= 1,
            tickPipeline: false);

        SectionCacheRebuildPipelineSnapshot failed = await WaitForObservedAsync(
            pipeline,
            snapshot => snapshot.EncodeFailures >= 1 && lookup.IsCompleted,
            tickPipeline: true);
        (bool success, _) = await lookup;

        Assert.False(success);
        Assert.True(encodeCalls >= 1);
        Assert.Equal(0, failed.CacheWaitTimeouts);
        Assert.Equal(0, failed.OnDemandPendingRequests);
    }

    private static Task<(bool Success, ReadOnlyMemory<byte> Frame)> StartLookupAsync(
        PlayerBootstrapPacketSet packets,
        WorldSectionId section) =>
        Task.Run(
            () =>
            {
                bool success = packets.TryGetOrRequestSectionFrame(section, out ReadOnlyMemory<byte> frame);
                return (success, frame);
            },
            TestContext.Current.CancellationToken);

    private static async Task<SectionCacheRebuildPipelineSnapshot> WaitForObservedAsync(
        SectionCacheRebuildPipeline pipeline,
        Func<SectionCacheRebuildPipelineSnapshot, bool> predicate,
        bool tickPipeline)
    {
        for (int attempt = 0; attempt < 500; attempt++)
        {
            if (tickPipeline)
                pipeline.Tick();

            SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
            if (predicate(snapshot))
                return snapshot;

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        SectionCacheRebuildPipelineSnapshot final = pipeline.Snapshot;
        throw new TimeoutException(
            $"Section single-flight condition was not observed. dirty={final.DirtyBacklog}, " +
            $"inFlight={final.InFlight}, submitted={final.SubmittedRebuilds}, failures={final.EncodeFailures}, " +
            $"requests={final.OnDemandRequests}, pending={final.OnDemandPendingRequests}, timeouts={final.CacheWaitTimeouts}.");
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
