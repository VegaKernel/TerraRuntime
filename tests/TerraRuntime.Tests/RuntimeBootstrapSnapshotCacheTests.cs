using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeBootstrapSnapshotCacheTests
{
    private const int SnapshotHeaderSize = 128;
    private const int SnapshotHashOffset = 88;
    private const int SnapshotHashSize = 32;

    [Fact]
    public void Bootstrap_snapshot_round_trips_encoded_join_frames()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet expectedPackets = PlayerBootstrapPacketSet.Create(world);
        PlayerBootstrapPacketSnapshot expected = expectedPackets.CaptureSnapshot();
        Assert.All(expected.BaseSectionPostFrames, Assert.Empty);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, expectedPackets).IsWritten);

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                stamp,
                world,
                out PlayerBootstrapPacketSet? restoredPackets);

            Assert.True(diagnostic.IsLoaded);
            PlayerBootstrapPacketSnapshot actual = Assert.IsType<PlayerBootstrapPacketSet>(restoredPackets).CaptureSnapshot();
            AssertSnapshotsEqual(expected, actual);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Bootstrap_snapshot_rejects_legacy_section_post_frames()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSnapshot current = PlayerBootstrapPacketSet.Create(world).CaptureSnapshot();
        Assert.NotEmpty(current.BaseSectionPostFrames);

        var legacyPostFrames = (ReadOnlyMemory<byte>[][])current.BaseSectionPostFrames.Clone();
        legacyPostFrames[0] = [new byte[] { 3, 0, 32 }];
        PlayerBootstrapPacketSnapshot legacy = current with { BaseSectionPostFrames = legacyPostFrames };

        Assert.False(PlayerBootstrapPacketSet.TryCreateFromSnapshot(world, legacy, out PlayerBootstrapPacketSet? restored));
        Assert.Null(restored);
    }

    [Fact]
    public void Bootstrap_snapshot_decoder_rejects_section_post_frames()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, packets).IsWritten);
            byte[] bytes = File.ReadAllBytes(path);
            (int firstPostCountOffset, _) = LocateSnapshotFrameCounts(bytes);
            Assert.True(firstPostCountOffset >= SnapshotHeaderSize);
            Assert.True(BitConverter.TryWriteBytes(bytes.AsSpan(firstPostCountOffset, sizeof(int)), 1));
            RewritePayloadHash(bytes);
            File.WriteAllBytes(path, bytes);

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                stamp,
                world,
                out PlayerBootstrapPacketSet? restored);

            Assert.Equal(RuntimeBootstrapSnapshotLoadResult.InvalidPayload, diagnostic.Result);
            Assert.Null(restored);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Bootstrap_snapshot_decoder_rejects_global_frame_count_above_runtime_budget()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        Assert.True(WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded).IsLoaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, packets).IsWritten);
            byte[] bytes = File.ReadAllBytes(path);
            (_, int globalCountOffset) = LocateSnapshotFrameCounts(bytes);
            int invalidCount = checked(PlayerBootstrapFrameBudget.MaximumGlobalPostSectionFrames + 1);
            Assert.True(BitConverter.TryWriteBytes(bytes.AsSpan(globalCountOffset, sizeof(int)), invalidCount));
            RewritePayloadHash(bytes);
            File.WriteAllBytes(path, bytes);

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                stamp,
                world,
                out PlayerBootstrapPacketSet? restored);

            Assert.Equal(RuntimeBootstrapSnapshotLoadResult.InvalidPayload, diagnostic.Result);
            Assert.Null(restored);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Bootstrap_snapshot_rejects_corrupted_payload()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, packets).IsWritten);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[SnapshotHeaderSize] ^= 0x01;
            File.WriteAllBytes(path, bytes);

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                stamp,
                world,
                out PlayerBootstrapPacketSet? restored);

            Assert.Equal(RuntimeBootstrapSnapshotLoadResult.PayloadHashMismatch, diagnostic.Result);
            Assert.Null(restored);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Bootstrap_snapshot_is_invalidated_by_source_stamp_change()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, packets).IsWritten);
            RuntimeWorldSourceStamp changed = stamp with { LastWriteTimeUtcTicks = stamp.LastWriteTimeUtcTicks + 1 };

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                changed,
                world,
                out PlayerBootstrapPacketSet? restored);

            Assert.Equal(RuntimeBootstrapSnapshotLoadResult.SourceMismatch, diagnostic.Result);
            Assert.Null(restored);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Bootstrap_snapshot_is_invalidated_by_build_identity_change()
    {
        byte[] source = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoader.TryLoad(source, limits, out WorldFileData? loaded);
        WorldFileData world = Assert.IsType<WorldFileData>(loaded);
        PlayerBootstrapPacketSet packets = PlayerBootstrapPacketSet.Create(world);

        string path = TempPath();
        var stamp = new RuntimeWorldSourceStamp(source.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeBootstrapSnapshotCache.TryWriteAtomic(path, stamp, world, packets).IsWritten);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[32] ^= 0x01;
            File.WriteAllBytes(path, bytes);

            RuntimeBootstrapSnapshotLoadDiagnostic diagnostic = RuntimeBootstrapSnapshotCache.TryLoad(
                path,
                stamp,
                world,
                out PlayerBootstrapPacketSet? restored);

            Assert.Equal(RuntimeBootstrapSnapshotLoadResult.BuildMismatch, diagnostic.Result);
            Assert.Null(restored);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    private static (int FirstPostCountOffset, int GlobalCountOffset) LocateSnapshotFrameCounts(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        stream.Position = SnapshotHeaderSize;

        SkipSnapshotFrame(reader);
        SkipSnapshotFrame(reader);
        int sectionCount = reader.ReadInt32();
        Assert.True(sectionCount > 0);

        int firstPostCountOffset = -1;
        for (int i = 0; i < sectionCount; i++)
        {
            _ = reader.ReadInt32();
            _ = reader.ReadInt32();
            SkipSnapshotFrame(reader);

            int postCountOffset = checked((int)stream.Position);
            int postCount = reader.ReadInt32();
            if (i == 0)
                firstPostCountOffset = postCountOffset;

            Assert.True(postCount >= 0);
            for (int frameIndex = 0; frameIndex < postCount; frameIndex++)
                SkipSnapshotFrame(reader);
        }

        return (firstPostCountOffset, checked((int)stream.Position));
    }

    private static void SkipSnapshotFrame(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        Assert.InRange(length, 3, ushort.MaxValue);
        reader.BaseStream.Seek(length, SeekOrigin.Current);
    }

    private static void RewritePayloadHash(byte[] bytes) =>
        SHA256.HashData(
            bytes.AsSpan(SnapshotHeaderSize),
            bytes.AsSpan(SnapshotHashOffset, SnapshotHashSize));

    private static void AssertSnapshotsEqual(
        PlayerBootstrapPacketSnapshot expected,
        PlayerBootstrapPacketSnapshot actual)
    {
        Assert.Equal(expected.BaseSections, actual.BaseSections);
        AssertFrameEqual(expected.WorldInfoFrame, actual.WorldInfoFrame);
        AssertFrameEqual(expected.StatusFrame, actual.StatusFrame);
        Assert.Equal(expected.BaseSectionFrames.Length, actual.BaseSectionFrames.Length);
        Assert.Equal(expected.BaseSectionPostFrames.Length, actual.BaseSectionPostFrames.Length);
        for (int i = 0; i < expected.BaseSectionFrames.Length; i++)
        {
            AssertFrameEqual(expected.BaseSectionFrames[i], actual.BaseSectionFrames[i]);
            Assert.Equal(expected.BaseSectionPostFrames[i].Length, actual.BaseSectionPostFrames[i].Length);
            for (int frame = 0; frame < expected.BaseSectionPostFrames[i].Length; frame++)
                AssertFrameEqual(expected.BaseSectionPostFrames[i][frame], actual.BaseSectionPostFrames[i][frame]);
        }

        Assert.Equal(expected.GlobalPostSectionFrames.Length, actual.GlobalPostSectionFrames.Length);
        for (int i = 0; i < expected.GlobalPostSectionFrames.Length; i++)
            AssertFrameEqual(expected.GlobalPostSectionFrames[i], actual.GlobalPostSectionFrames[i]);
        AssertFrameEqual(expected.EnterWorldFrame, actual.EnterWorldFrame);
    }

    private static void AssertFrameEqual(ReadOnlyMemory<byte> expected, ReadOnlyMemory<byte> actual) =>
        Assert.True(expected.Span.SequenceEqual(actual.Span));

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"terraruntime-bootstrap-{Guid.NewGuid():N}.runtime-bootstrap");

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
