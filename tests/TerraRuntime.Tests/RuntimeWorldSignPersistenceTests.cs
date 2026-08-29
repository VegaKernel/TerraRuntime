using System.Reflection;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSignPersistenceTests
{
    [Fact]
    public async Task Canonical_runtime_sign_mutation_round_trips_through_world_save()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var signStore = new RuntimeSignStore([
            new WorldSign(0, "before", 1, 2)
        ]);
        Assert.True(signStore.CanPersistMutations);

        var submitted = new TerrariaSignState(
            SignId: 0,
            TileX: 1,
            TileY: 2,
            Text: "after restart",
            Player: 7,
            Flags: 0);
        Assert.True(signStore.TryApply(in submitted, out WorldSign? committed, out bool changed));
        Assert.True(changed);
        Assert.NotNull(committed);
        Assert.Equal("after restart", committed!.Text);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-sign-save-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldTileChestSaveService(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            new RuntimeChestStore(source.Chests),
            synchronizationSectionsPerTick: 1,
            signStore: signStore);

        try
        {
            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            byte[] savedFile = File.ReadAllBytes(destinationPath);
            Assert.True(WorldFileLoader.TryLoad(savedFile, limits, out WorldFileData? savedWorld).IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);
            WorldSign savedSign = Assert.Single(loaded.Signs);
            Assert.Equal((short)0, savedSign.SlotId);
            Assert.Equal(1, savedSign.X);
            Assert.Equal(2, savedSign.Y);
            Assert.Equal("after restart", savedSign.Text);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Sparse_runtime_sign_slots_are_compacted_and_persisted_canonically()
    {
        byte[] sourceFile = LoaderFixture<byte[]>("CreateCompleteCurrentWorld");
        WorldFileLoadLimits limits = LoaderFixture<WorldFileLoadLimits>("CreateLimits");
        Assert.True(WorldFileLoader.TryLoad(sourceFile, limits, out WorldFileData? sourceWorld).IsLoaded);
        WorldFileData source = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(WorldFilePreservedSections.TryCapture(
            sourceFile,
            source.Envelope,
            out WorldFilePreservedSections? preserved));
        Assert.NotNull(preserved);

        var sparseStore = new RuntimeSignStore([
            new WorldSign(3, "sparse", 1, 2)
        ]);
        Assert.True(sparseStore.CanPersistMutations);
        var submitted = new TerrariaSignState(3, 1, 2, "persist after restart", 0, 0);
        Assert.True(sparseStore.TryApply(in submitted, out WorldSign? committed, out bool changed));
        Assert.True(changed);
        Assert.NotNull(committed);
        Assert.Equal((short)3, committed!.SlotId);

        Assert.True(sparseStore.TryCaptureCanonicalSnapshot(out WorldSign[] canonical));
        WorldSign canonicalSign = Assert.Single(canonical);
        Assert.Equal((short)0, canonicalSign.SlotId);
        Assert.Equal("persist after restart", canonicalSign.Text);

        string directory = Path.Combine(Path.GetTempPath(), $"terraruntime-sparse-sign-save-{Guid.NewGuid():N}");
        string destinationPath = Path.Combine(directory, "world.wld");
        Directory.CreateDirectory(directory);
        var service = new RuntimeWorldTileChestSaveService(
            destinationPath,
            source.Envelope,
            source.Header,
            preserved!,
            source.Tiles,
            new RuntimeChestStore(source.Chests),
            synchronizationSectionsPerTick: 1,
            signStore: sparseStore);

        try
        {
            service.CaptureFinalSaveAfterOwnerStopped();
            await service.CompleteAsync(TestContext.Current.CancellationToken);

            byte[] savedFile = File.ReadAllBytes(destinationPath);
            Assert.True(WorldFileLoader.TryLoad(savedFile, limits, out WorldFileData? savedWorld).IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(savedWorld);
            WorldSign savedSign = Assert.Single(loaded.Signs);
            Assert.Equal((short)0, savedSign.SlotId);
            Assert.Equal(1, savedSign.X);
            Assert.Equal(2, savedSign.Y);
            Assert.Equal("persist after restart", savedSign.Text);
        }
        finally
        {
            await service.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
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
