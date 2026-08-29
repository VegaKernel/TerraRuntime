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
        Assert.True(packets.TryGetCachedSectionFrame(section, revision, out ReadOnlyMemory<byte> frame));
        Assert.Equal((byte)TerrariaMessageId.TileSection, frame.Span[2]);
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
                releaseFirstEncode.Wait();
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
        Assert.True(firstEncodeStarted.Wait(TimeSpan.FromSeconds(5)));

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
            releaseEncode.Wait();
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
        Assert.True(encodeStarted.Wait(TimeSpan.FromSeconds(5)));

        long capturedBefore = pipeline.Snapshot.CapturedSnapshots;
        for (int i = 0; i < 8; i++)
            pipeline.Tick();

        Assert.Equal(capturedBefore, pipeline.Snapshot.CapturedSnapshots);
        releaseEncode.Set();
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

            await Task.Delay(10);
        }

        SectionCacheRebuildPipelineSnapshot final = pipeline.Snapshot;
        throw new TimeoutException(
            $"Section cache rebuild did not reach the expected state. " +
            $"dirty={final.DirtyBacklog}, inFlight={final.InFlight}, submitted={final.SubmittedRebuilds}, " +
            $"published={final.PublishedFrames}, stale={final.StaleResults}, failures={final.EncodeFailures}.");
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
