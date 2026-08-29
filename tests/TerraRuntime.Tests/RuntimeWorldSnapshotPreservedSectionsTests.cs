using System.Buffers.Binary;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSnapshotPreservedSectionsTests
{
    [Fact]
    public void Loads_only_preserved_sections_from_embedded_canonical_world()
    {
        int[] offsets = [16, 24, 40, 48, 56, 64, 72, 80, 88, 96, 104];
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 7,
            favoriteFlags: 0,
            offsets,
            VanillaWorldFormat326.TileTypeCount,
            new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var dimensions = new WorldDimensions(20, 20);

        byte[] canonical = new byte[128];
        for (int index = 0; index < canonical.Length; index++)
            canonical[index] = checked((byte)(index + 1));

        const long sourceWriteTicks = 123_456;
        byte[] cache = new byte[128 + canonical.Length + 32];
        "TRWCACHE"u8.CopyTo(cache);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(8), 128);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(16), canonical.Length);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(24), sourceWriteTicks);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(32), canonical.Length);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(72), WorldFileFormatPolicy.CurrentVersion);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(76), dimensions.WidthTiles);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(80), dimensions.HeightTiles);
        canonical.CopyTo(cache.AsSpan(128));

        string path = Path.Combine(Path.GetTempPath(), $"tr-preserved-{Guid.NewGuid():N}.runtime-world");
        try
        {
            File.WriteAllBytes(path, cache);
            var sourceStamp = new RuntimeWorldSourceStamp(canonical.Length, sourceWriteTicks);

            RuntimeWorldPreservedSectionsLoadDiagnostic diagnostic =
                RuntimeWorldSnapshotPreservedSections.TryLoad(
                    path,
                    sourceStamp,
                    envelope,
                    dimensions,
                    out WorldFilePreservedSections? preserved);

            Assert.True(diagnostic.IsLoaded);
            Assert.NotNull(preserved);
            AssertSection(canonical, offsets, 0, preserved!.Header);
            AssertSection(canonical, offsets, 3, preserved.Signs);
            AssertSection(canonical, offsets, 4, preserved.Npcs);
            AssertSection(canonical, offsets, 5, preserved.TileEntities);
            AssertSection(canonical, offsets, 6, preserved.PressurePlates);
            AssertSection(canonical, offsets, 7, preserved.TownRooms);
            AssertSection(canonical, offsets, 8, preserved.Bestiary);
            AssertSection(canonical, offsets, 9, preserved.CreativePowers);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rejects_cache_when_source_is_newer()
    {
        int[] offsets = [16, 24, 40, 48, 56, 64, 72, 80, 88, 96, 104];
        var envelope = new WorldFileEnvelope(
            WorldFileFormatPolicy.CurrentVersion,
            revision: 0,
            favoriteFlags: 0,
            offsets,
            VanillaWorldFormat326.TileTypeCount,
            new byte[(VanillaWorldFormat326.TileTypeCount + 7) >> 3]);
        var dimensions = new WorldDimensions(20, 20);
        byte[] canonical = new byte[128];
        byte[] cache = new byte[128 + canonical.Length];
        "TRWCACHE"u8.CopyTo(cache);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(8), 128);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(16), canonical.Length);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(24), 10);
        BinaryPrimitives.WriteInt64LittleEndian(cache.AsSpan(32), canonical.Length);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(72), WorldFileFormatPolicy.CurrentVersion);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(76), dimensions.WidthTiles);
        BinaryPrimitives.WriteInt32LittleEndian(cache.AsSpan(80), dimensions.HeightTiles);

        string path = Path.Combine(Path.GetTempPath(), $"tr-preserved-{Guid.NewGuid():N}.runtime-world");
        try
        {
            File.WriteAllBytes(path, cache);
            RuntimeWorldPreservedSectionsLoadDiagnostic diagnostic =
                RuntimeWorldSnapshotPreservedSections.TryLoad(
                    path,
                    new RuntimeWorldSourceStamp(canonical.Length, 11),
                    envelope,
                    dimensions,
                    out WorldFilePreservedSections? preserved);

            Assert.Equal(RuntimeWorldPreservedSectionsLoadResult.SourceNewer, diagnostic.Result);
            Assert.Null(preserved);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertSection(
        byte[] canonical,
        IReadOnlyList<int> offsets,
        int sectionIndex,
        ReadOnlyMemory<byte> actual)
    {
        byte[] expected = canonical
            .AsSpan(offsets[sectionIndex], offsets[sectionIndex + 1] - offsets[sectionIndex])
            .ToArray();
        Assert.Equal(expected, actual.ToArray());
    }
}
