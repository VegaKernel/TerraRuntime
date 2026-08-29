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
            AssertEqual(expected!, preserved!);
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
