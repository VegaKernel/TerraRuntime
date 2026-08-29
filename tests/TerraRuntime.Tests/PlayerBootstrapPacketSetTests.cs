using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerBootstrapPacketSetTests
{
    [Fact]
    public void Testing_packet_set_includes_status_packet_for_base_sections()
    {
        ReadOnlyMemory<byte>[] sections =
        [
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection },
            new byte[] { 3, 0, (byte)TerrariaMessageId.TileSection }
        ];

        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.CreateForTesting(
            worldInfoFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.WorldData },
            baseSectionFrames: sections,
            enterWorldFrame: new byte[] { 3, 0, (byte)TerrariaMessageId.PlayerSpawnSelf });

        Assert.Equal((byte)TerrariaMessageId.StatusTextSize, packets.StatusFrame.Span[2]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(packets.StatusFrame.Span[3..7]));
    }

    [Fact]
    public void Section_response_refreshes_mutated_base_section_without_rewriting_initial_cache_array()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        Assert.True(packets.TryCreateSectionResponse(
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            team: 0,
            out PlayerBootstrapSectionResponse before));

        var initialSharedFrames = new ReadOnlyMemory<byte>[packets.BaseSectionFrames.Count];
        for (int i = 0; i < initialSharedFrames.Length; i++)
            initialSharedFrames[i] = packets.BaseSectionFrames[i];

        WorldTile tile = world.Tiles.Get(world.RuntimeMetadata.SpawnX, world.RuntimeMetadata.SpawnY);
        tile.Flags ^= WorldTileFlags.WireRed;
        world.Tiles.Set(world.RuntimeMetadata.SpawnX, world.RuntimeMetadata.SpawnY, tile);

        Assert.True(packets.TryCreateSectionResponse(
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            team: 0,
            out PlayerBootstrapSectionResponse after));

        Assert.Equal(before.BaseSectionFrames.Length, after.BaseSectionFrames.Length);
        Assert.True(
            before.BaseSectionFrames
                .Where((frame, index) => !frame.Span.SequenceEqual(after.BaseSectionFrames[index].Span))
                .Any(),
            "At least the mutated spawn section must be re-encoded for the next join transfer.");

        Assert.Equal(initialSharedFrames.Length, packets.BaseSectionFrames.Count);
        for (int i = 0; i < initialSharedFrames.Length; i++)
        {
            Assert.True(initialSharedFrames[i].Span.SequenceEqual(packets.BaseSectionFrames[i].Span));
        }
    }

    [Fact]
    public async Task Section_response_waits_for_worker_publication_after_base_section_mutation()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        Assert.True(packets.TryCreateSectionResponse(
            world.RuntimeMetadata.SpawnX,
            world.RuntimeMetadata.SpawnY,
            team: 0,
            out PlayerBootstrapSectionResponse before));

        using var pipeline = new SectionCacheRebuildPipeline(
            world,
            packets,
            workerCount: 1,
            workCapacity: 1,
            completionCapacity: 1);
        pipeline.Start();

        WorldTile tile = world.Tiles.Get(world.RuntimeMetadata.SpawnX, world.RuntimeMetadata.SpawnY);
        tile.Flags ^= WorldTileFlags.WireBlue;
        world.Tiles.Set(world.RuntimeMetadata.SpawnX, world.RuntimeMetadata.SpawnY, tile);

        Task<(bool Success, PlayerBootstrapSectionResponse Response)> responseTask = Task.Run(
            () =>
            {
                bool success = packets.TryCreateSectionResponse(
                    world.RuntimeMetadata.SpawnX,
                    world.RuntimeMetadata.SpawnY,
                    team: 0,
                    out PlayerBootstrapSectionResponse response);
                return (success, response);
            },
            TestContext.Current.CancellationToken);

        SectionCacheRebuildPipelineSnapshot waiting = await WaitForObservedRequestAsync(pipeline);
        Assert.False(responseTask.IsCompleted);
        Assert.True(waiting.CacheMisses >= 1);
        Assert.True(waiting.CacheStaleReads >= 1);
        Assert.True(waiting.CacheWaits >= 1);
        Assert.True(waiting.OnDemandRequests >= 1);

        for (int attempt = 0; attempt < 500 && !responseTask.IsCompleted; attempt++)
        {
            pipeline.Tick();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        (bool success, PlayerBootstrapSectionResponse after) = await responseTask;
        Assert.True(success);

        SectionCacheRebuildPipelineSnapshot completed = pipeline.Snapshot;
        Assert.True(completed.SubmittedRebuilds >= 1);
        Assert.True(completed.PublishedFrames >= 1);
        Assert.True(completed.CacheWaitCompletions >= 1);
        Assert.Equal(0, completed.CacheWaitTimeouts);
        Assert.Equal(0, completed.OnDemandPendingRequests);

        Assert.Equal(before.BaseSectionFrames.Length, after.BaseSectionFrames.Length);
        Assert.True(
            before.BaseSectionFrames
                .Where((frame, index) => !frame.Span.SequenceEqual(after.BaseSectionFrames[index].Span))
                .Any(),
            "The join response must consume the worker-published frame for the mutated base section.");
    }

    private static async Task<SectionCacheRebuildPipelineSnapshot> WaitForObservedRequestAsync(
        SectionCacheRebuildPipeline pipeline)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            SectionCacheRebuildPipelineSnapshot snapshot = pipeline.Snapshot;
            if (snapshot.OnDemandRequests >= 1 && snapshot.CacheWaits >= 1)
                return snapshot;

            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("Bootstrap section response did not enter the worker-backed cache wait path.");
    }

    private static byte[] CreateCompleteWorld() =>
        (byte[])InvokeWorldLoaderTestHelper("CreateCompleteCurrentWorld")!;

    private static WorldFileLoadLimits CreateLimits() =>
        (WorldFileLoadLimits)InvokeWorldLoaderTestHelper("CreateLimits")!;

    private static object? InvokeWorldLoaderTestHelper(string name)
    {
        MethodInfo method = typeof(WorldFileLoaderTests).GetMethod(
            name,
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"World loader test helper '{name}' was not found.");
        return method.Invoke(null, null);
    }
}
