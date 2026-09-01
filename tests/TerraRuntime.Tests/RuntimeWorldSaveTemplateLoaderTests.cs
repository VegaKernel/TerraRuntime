using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSaveTemplateLoaderTests
{
    [Fact]
    public void Falls_back_to_sparse_canonical_world_when_runtime_cache_is_missing()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? world).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(world);

        string directory = Path.Combine(Path.GetTempPath(), $"tr-save-template-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = Path.Combine(directory, "missing.runtime-world");
        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp stamp));

            RuntimeWorldSaveTemplateLoadResult result = RuntimeWorldSaveTemplateLoader.TryLoad(
                worldPath,
                cachePath,
                stamp,
                loaded,
                out WorldFilePreservedSections? preserved);

            Assert.True(result.Success);
            Assert.Equal(RuntimeWorldSaveTemplateLoadSource.CanonicalWorld, result.Source);
            Assert.Equal(RuntimeWorldPreservedSectionsLoadResult.NotFound, result.CacheResult);
            Assert.NotNull(preserved);
            Assert.True(WorldFilePreservedSections.TryCapture(canonical, loaded.Envelope, out WorldFilePreservedSections? expected));
            Assert.NotNull(expected);
            WorldFilePreservedSectionNormalizationDiagnostic normalization =
                expected!.TryNormalizeSemanticSections(loaded, out WorldFilePreservedSections? normalizedExpected);
            Assert.True(normalization.IsNormalized);
            Assert.NotNull(normalizedExpected);
            AssertEqual(normalizedExpected!, preserved!);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Prefers_embedded_runtime_cache_template_when_cache_is_current()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? world).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(world);

        string directory = Path.Combine(Path.GetTempPath(), $"tr-save-template-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = Path.Combine(directory, "world.runtime-world");
        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp stamp));
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(cachePath, canonical, stamp, loaded).IsWritten);

            RuntimeWorldSaveTemplateLoadResult result = RuntimeWorldSaveTemplateLoader.TryLoad(
                worldPath,
                cachePath,
                stamp,
                loaded,
                out WorldFilePreservedSections? preserved);

            Assert.True(result.Success);
            Assert.Equal(RuntimeWorldSaveTemplateLoadSource.RuntimeCache, result.Source);
            Assert.Equal(RuntimeWorldPreservedSectionsLoadResult.Loaded, result.CacheResult);
            Assert.NotNull(preserved);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Canonical_template_normalizes_noncanonical_creative_power_order()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? originalWorld).IsLoaded);
        WorldFileData original = Assert.IsType<WorldFileData>(originalWorld);

        byte[] noncanonicalCreative = EncodeCreativePowersReverseOrder(original.CreativePowers);
        int creativeStart = original.Envelope.SectionOffsets[9];
        int creativeEnd = original.Envelope.SectionOffsets[10];
        Assert.Equal(creativeEnd - creativeStart, noncanonicalCreative.Length);
        noncanonicalCreative.CopyTo(canonical.AsSpan(creativeStart, noncanonicalCreative.Length));

        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? reorderedWorld).IsLoaded);
        WorldFileData reordered = Assert.IsType<WorldFileData>(reorderedWorld);
        Assert.Equal(original.CreativePowers, reordered.CreativePowers);

        string directory = Path.Combine(Path.GetTempPath(), $"tr-save-template-normalize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = Path.Combine(directory, "missing.runtime-world");
        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp stamp));

            RuntimeWorldSaveTemplateLoadResult result = RuntimeWorldSaveTemplateLoader.TryLoad(
                worldPath,
                cachePath,
                stamp,
                reordered,
                out WorldFilePreservedSections? preserved);

            Assert.True(result.Success);
            Assert.NotNull(preserved);
            byte[] canonicalCreative = EncodeCreativePowers(reordered.CreativePowers);
            Assert.NotEqual(noncanonicalCreative, canonicalCreative);
            Assert.Equal(canonicalCreative, preserved!.CreativePowers.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Semantic_normalization_fails_closed_instead_of_preserving_invalid_runtime_state()
    {
        byte[] canonical = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(canonical, limits, out WorldFileData? world).IsLoaded);
        WorldFileData loaded = Assert.IsType<WorldFileData>(world);
        WorldFileData invalid = loaded with
        {
            Bestiary = new WorldBestiaryData(
                [new WorldBestiaryKill("Duplicate", 1), new WorldBestiaryKill("Duplicate", 2)],
                [],
                [])
        };

        string directory = Path.Combine(Path.GetTempPath(), $"tr-save-template-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string worldPath = Path.Combine(directory, "world.wld");
        string cachePath = Path.Combine(directory, "missing.runtime-world");
        try
        {
            File.WriteAllBytes(worldPath, canonical);
            Assert.True(RuntimeWorldSnapshotCache.TryCaptureSourceStamp(worldPath, out RuntimeWorldSourceStamp stamp));

            RuntimeWorldSaveTemplateLoadResult result = RuntimeWorldSaveTemplateLoader.TryLoad(
                worldPath,
                cachePath,
                stamp,
                invalid,
                out WorldFilePreservedSections? preserved);

            Assert.False(result.Success);
            Assert.Null(preserved);
            Assert.Contains(nameof(WorldFilePreservedSectionNormalizationResult.BestiaryEncodeFailed), result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] EncodeCreativePowers(WorldCreativePowersData powers)
    {
        using var stream = new MemoryStream();
        Assert.Equal(
            WorldFileCreativePowersEncodeResult.Encoded,
            WorldFileCreativePowersEncoder.TryEncode(powers, stream, out long bytesWritten));
        Assert.Equal(stream.Length, bytesWritten);
        return stream.ToArray();
    }

    private static byte[] EncodeCreativePowersReverseOrder(WorldCreativePowersData powers)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        WriteBooleanPower(writer, 13, powers.StopBiomeSpread);
        WriteFloatPower(writer, 12, powers.DifficultySlider);
        WriteBooleanPower(writer, 10, powers.FreezeWind);
        WriteBooleanPower(writer, 9, powers.FreezeRain);
        WriteFloatPower(writer, 8, powers.TimeRateSlider);
        WriteBooleanPower(writer, 0, powers.FreezeTime);
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteBooleanPower(BinaryWriter writer, ushort id, bool value)
    {
        writer.Write((byte)1);
        writer.Write(id);
        writer.Write((byte)(value ? 1 : 0));
    }

    private static void WriteFloatPower(BinaryWriter writer, ushort id, float value)
    {
        writer.Write((byte)1);
        writer.Write(id);
        writer.Write(value);
    }

    private static void AssertEqual(WorldFilePreservedSections expected, WorldFilePreservedSections actual)
    {
        Assert.Equal(expected.Header.ToArray(), actual.Header.ToArray());
        Assert.Equal(expected.Signs.ToArray(), actual.Signs.ToArray());
        Assert.Equal(expected.Npcs.ToArray(), actual.Npcs.ToArray());
        Assert.Equal(expected.TileEntities.ToArray(), actual.TileEntities.ToArray());
        Assert.Equal(expected.PressurePlates.ToArray(), actual.PressurePlates.ToArray());
        Assert.Equal(expected.TownRooms.ToArray(), actual.TownRooms.ToArray());
        Assert.Equal(expected.Bestiary.ToArray(), actual.Bestiary.ToArray());
        Assert.Equal(expected.CreativePowers.ToArray(), actual.CreativePowers.ToArray());
    }

    private static T LoaderFixture<T>(string methodName)
    {
        MethodInfo? method = typeof(WorldFileLoaderTests).GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, null));
    }
}
