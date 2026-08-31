using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class EncodedSectionCacheContractTests
{
    [Fact]
    public void Dynamic_cached_frame_is_invalidated_by_committed_section_revision()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        WorldSectionId section = new(4, 4);
        Assert.False(packets.IsPinnedBaseSection(section));

        long before = world.Tiles.GetSectionVersion(section);
        Assert.True(packets.TryPublishSectionFrame(section, before, CreateFrame(4096, 0x61)));

        WorldTileBounds bounds = TerrariaSectionGeometry.GetBounds(world.Header.Dimensions, section);
        WorldTile unchanged = world.Tiles.Get(bounds.X, bounds.Y);
        world.Tiles.Set(bounds.X, bounds.Y, in unchanged);

        long after = world.Tiles.GetSectionVersion(section);
        Assert.NotEqual(before, after);
        Assert.True(world.Tiles.DirtySections.IsDirty(section));
        Assert.True(world.Tiles.PersistenceDirtySections.IsDirty(section));

        Assert.True(packets.TryGetOrRequestSectionFrame(section, out ReadOnlyMemory<byte> currentFrame));
        Assert.False(currentFrame.IsEmpty);
        Assert.True(packets.TryGetCachedSectionFrame(section, after, out _));

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal(1L, snapshot.Invalidations);
        Assert.Equal(1L, snapshot.StaleReads);
        Assert.True(snapshot.Misses >= 1);
    }

    [Fact]
    public void Pinned_bootstrap_frame_is_reclaimed_after_revision_changes_and_reencoded_before_delivery()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        Assert.NotEmpty(packets.BaseStreamingSections.ToArray());
        WorldSectionId section = packets.BaseStreamingSections[0];
        Assert.True(packets.IsPinnedBaseSection(section));

        long before = world.Tiles.GetSectionVersion(section);
        WorldTileBounds bounds = TerrariaSectionGeometry.GetBounds(world.Header.Dimensions, section);
        WorldTile unchanged = world.Tiles.Get(bounds.X, bounds.Y);
        world.Tiles.Set(bounds.X, bounds.Y, in unchanged);
        long after = world.Tiles.GetSectionVersion(section);
        Assert.NotEqual(before, after);

        Assert.True(packets.TryGetBaseSectionFrames(out ReadOnlyMemory<byte>[] frames));
        Assert.NotEmpty(frames);
        Assert.True(packets.TryGetCachedSectionFrame(section, after, out ReadOnlyMemory<byte> rebuilt));
        Assert.False(rebuilt.IsEmpty);

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal(1L, snapshot.Invalidations);
        Assert.Equal(1L, snapshot.StaleReads);
    }

    [Fact]
    public void Initial_population_bypasses_both_dirty_trackers_and_section_revisions()
    {
        var dimensions = new WorldDimensions(400, 300);
        var tiles = new WorldTileStore(dimensions);
        var section = new WorldSectionId(0, 0);
        long before = tiles.GetSectionVersion(section);
        WorldTile tile = tiles.Get(0, 0);

        tiles.SetInitialPopulationTile(0, 0, in tile);

        Assert.Equal(before, tiles.GetSectionVersion(section));
        Assert.False(tiles.DirtySections.IsDirty(section));
        Assert.False(tiles.PersistenceDirtySections.IsDirty(section));
    }

    [Fact]
    public void Streaming_state_does_not_claim_delivery_until_caller_marks_successful_queue()
    {
        var dimensions = new WorldDimensions(4200, 1200);
        var state = new PlayerSectionStreamingState(dimensions);
        Span<WorldSectionId> bootstrap = stackalloc WorldSectionId[InitialSectionBootstrapPlanner.MaximumBaseSectionCount];
        int bootstrapCount = InitialSectionBootstrapPlanner.PlanBaseSpawnSections(dimensions, 2100, 300, bootstrap);
        state.ObserveBootstrap(bootstrap[..bootstrapCount], -1, -1);

        WorldSectionId center = TerrariaSectionGeometry.FromTile(dimensions, 2100, 300);
        int nextCenterTileX = (center.X + 1) * TerrariaSectionGeometry.WidthTiles + 10;
        Span<WorldSectionId> firstAttempt = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        int firstCount = state.PlanUnsent(nextCenterTileX * 16f, 300 * 16f, firstAttempt);
        Assert.True(firstCount > 0);

        // Simulate an encode/outbound failure: the caller deliberately does not MarkSent.
        Span<WorldSectionId> retry = stackalloc WorldSectionId[PlayerSectionStreamingState.MaximumWindowSectionCount];
        int retryCount = state.PlanUnsent(nextCenterTileX * 16f, 300 * 16f, retry);
        Assert.Equal(firstCount, retryCount);
        Assert.Equal(firstAttempt[..firstCount].ToArray(), retry[..retryCount].ToArray());

        for (int i = 0; i < retryCount; i++)
            state.MarkSent(retry[i]);
        Assert.Equal(0, state.PlanUnsent(nextCenterTileX * 16f, 300 * 16f, retry));
    }

    [Fact]
    public void Cache_snapshot_exposes_bounded_memory_and_invalidation_counters()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        packets.SetDynamicSectionCacheByteBudgetForTesting(ushort.MaxValue);

        WorldSectionId section = new(4, 4);
        long revision = world.Tiles.GetSectionVersion(section);
        Assert.True(packets.TryPublishSectionFrame(section, revision, CreateFrame(40_000, 0x71)));

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal((long)ushort.MaxValue, snapshot.DynamicMaximumBytes);
        Assert.InRange(snapshot.DynamicBytes, 1L, snapshot.DynamicMaximumBytes);
        Assert.InRange(snapshot.Bytes, 1L, snapshot.MaximumBytes);
        Assert.Equal(0L, snapshot.WaitFailures);
        Assert.Equal(0L, snapshot.Invalidations);
    }

    private static byte[] CreateFrame(int length, byte marker)
    {
        Assert.InRange(length, TerrariaFrameDecoderOptions.MinimumFrameLength, ushort.MaxValue);
        var frame = new byte[length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, checked((ushort)length));
        frame[2] = (byte)TerrariaMessageId.TileSection;
        frame[^1] = marker;
        return frame;
    }

    private static WorldFileData CreateLargeSectionWorld()
    {
        WorldFileData source = LoadCompleteWorld();
        var dimensions = new WorldDimensions(widthTiles: 801, heightTiles: 601);
        WorldFileHeader header = source.Header with
        {
            RightWorld = dimensions.WidthTiles * 16,
            BottomWorld = dimensions.HeightTiles * 16,
            Dimensions = dimensions
        };
        return source with
        {
            Header = header,
            Tiles = new WorldTileStore(dimensions)
        };
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
