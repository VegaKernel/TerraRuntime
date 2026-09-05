using System.Buffers.Binary;
using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SectionCacheMemoryBudgetTests
{
    [Fact]
    public void Dynamic_cache_evicts_oldest_frame_to_enforce_byte_budget()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        packets.SetDynamicSectionCacheByteBudgetForTesting(ushort.MaxValue);

        WorldSectionId first = new(4, 4);
        WorldSectionId second = new(3, 4);
        Assert.False(packets.IsPinnedBaseSection(first));
        Assert.False(packets.IsPinnedBaseSection(second));

        long firstRevision = world.Tiles.GetSectionVersion(first);
        long secondRevision = world.Tiles.GetSectionVersion(second);
        Assert.True(packets.TryPublishSectionFrame(first, firstRevision, CreateFrame(40_000, 0x41)));
        Assert.True(packets.TryPublishSectionFrame(second, secondRevision, CreateFrame(40_000, 0x42)));

        Assert.False(packets.TryGetCachedSectionFrame(first, firstRevision, out _));
        Assert.True(packets.TryGetCachedSectionFrame(second, secondRevision, out ReadOnlyMemory<byte> retained));
        Assert.Equal((byte)0x42, retained.Span[^1]);

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal(40_000L, snapshot.DynamicBytes);
        Assert.Equal((long)ushort.MaxValue, snapshot.DynamicMaximumBytes);
        Assert.Equal(1L, snapshot.Evictions);
        Assert.InRange(snapshot.Bytes, 1L, snapshot.MaximumBytes);
    }

    [Fact]
    public void Dynamic_cache_hit_refreshes_deterministic_lru_order()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        packets.SetDynamicSectionCacheByteBudgetForTesting(ushort.MaxValue);

        WorldSectionId first = new(4, 4);
        WorldSectionId second = new(3, 4);
        WorldSectionId third = new(4, 3);
        Assert.False(packets.IsPinnedBaseSection(first));
        Assert.False(packets.IsPinnedBaseSection(second));
        Assert.False(packets.IsPinnedBaseSection(third));

        long firstRevision = world.Tiles.GetSectionVersion(first);
        long secondRevision = world.Tiles.GetSectionVersion(second);
        long thirdRevision = world.Tiles.GetSectionVersion(third);
        Assert.True(packets.TryPublishSectionFrame(first, firstRevision, CreateFrame(30_000, 0x51)));
        Assert.True(packets.TryPublishSectionFrame(second, secondRevision, CreateFrame(30_000, 0x52)));

        Assert.True(packets.TryGetCachedSectionFrame(first, firstRevision, out _));
        Assert.True(packets.TryPublishSectionFrame(third, thirdRevision, CreateFrame(30_000, 0x53)));

        Assert.True(packets.TryGetCachedSectionFrame(first, firstRevision, out _));
        Assert.False(packets.TryGetCachedSectionFrame(second, secondRevision, out _));
        Assert.True(packets.TryGetCachedSectionFrame(third, thirdRevision, out _));

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal(60_000L, snapshot.DynamicBytes);
        Assert.Equal(1L, snapshot.Evictions);
        Assert.True(snapshot.Bytes <= snapshot.MaximumBytes);
    }

    [Fact]
    public void Bootstrap_sections_are_pinned_outside_dynamic_budget_and_total_ceiling_is_observable()
    {
        WorldFileData world = CreateLargeSectionWorld();
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);
        packets.SetDynamicSectionCacheByteBudgetForTesting(ushort.MaxValue);

        Assert.True(packets.TryGetBaseSectionFrames(out ReadOnlyMemory<byte>[] baseFrames));
        Assert.NotEmpty(baseFrames);

        SectionPacketCacheSnapshot snapshot = packets.CaptureSectionCacheSnapshot();
        Assert.Equal(0L, snapshot.DynamicBytes);
        Assert.Equal((long)ushort.MaxValue, snapshot.DynamicMaximumBytes);
        Assert.True(snapshot.MaximumBytes >= snapshot.Bytes);
        Assert.True(snapshot.MaximumBytes > snapshot.DynamicMaximumBytes);
        Assert.Equal(0L, snapshot.Evictions);
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
