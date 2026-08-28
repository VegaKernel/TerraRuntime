using System.Reflection;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWorldSnapshotLiquidTests
{
    [Fact]
    public void Runtime_snapshot_restores_active_and_buffered_liquid_work_without_tile_scan()
    {
        byte[] file = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoadDiagnostic sourceDiagnostic = WorldFileLoader.TryLoad(file, limits, out WorldFileData? sourceWorld);
        Assert.True(sourceDiagnostic.IsLoaded);
        WorldFileData expected = Assert.IsType<WorldFileData>(sourceWorld);

        Assert.True(expected.Tiles.LiquidUpdates.TryEnqueue(0, 1, delay: 7, kill: 3));
        Assert.True(expected.Tiles.LiquidUpdates.TryEnqueue(1, 2, delay: 2, kill: 1));
        Assert.True(expected.Tiles.LiquidUpdates.TryBuffer(1, 0));

        string cachePath = Path.Combine(Path.GetTempPath(), $"terraruntime-liquid-{Guid.NewGuid():N}.runtime-world");
        var stamp = new RuntimeWorldSourceStamp(file.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(cachePath, file, stamp, expected).IsWritten);

            RuntimeWorldSnapshotLoadDiagnostic diagnostic = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                stamp,
                limits,
                out WorldFileData? loadedWorld);

            Assert.True(diagnostic.IsLoaded);
            WorldFileData loaded = Assert.IsType<WorldFileData>(loadedWorld);
            Assert.Equal(expected.Header, loaded.Header);
            Assert.Equal(expected.RuntimeMetadata.GameMode, loaded.RuntimeMetadata.GameMode);
            Assert.Equal(expected.RuntimeMetadata.WorldSurface, loaded.RuntimeMetadata.WorldSurface);
            Assert.Equal(expected.Chests.Length, loaded.Chests.Length);
            Assert.Equal(expected.Signs.Length, loaded.Signs.Length);
            Assert.Equal(expected.Npcs.TownNpcs.Length, loaded.Npcs.TownNpcs.Length);
            Assert.Equal(expected.TileEntities.Length, loaded.TileEntities.Length);
            Assert.Equal(expected.PressurePlates.Length, loaded.PressurePlates.Length);
            Assert.Equal(expected.TownRooms.Length, loaded.TownRooms.Length);
            Assert.Equal(expected.Bestiary.Kills.Length, loaded.Bestiary.Kills.Length);
            Assert.Equal(expected.CreativePowers, loaded.CreativePowers);
            Assert.Equal(2, loaded.Tiles.LiquidUpdates.ActiveCount);
            Assert.Equal(1, loaded.Tiles.LiquidUpdates.BufferedCount);

            Assert.True(loaded.Tiles.LiquidUpdates.TryDequeue(out WorldLiquidUpdate first));
            Assert.Equal(new WorldLiquidUpdate(0, 1, 7, 3), first);
            Assert.True(loaded.Tiles.LiquidUpdates.TryDequeue(out WorldLiquidUpdate second));
            Assert.Equal(new WorldLiquidUpdate(1, 2, 2, 1), second);
            Assert.True(loaded.Tiles.LiquidUpdates.TryDequeueBuffered(out int bufferX, out int bufferY));
            Assert.Equal((1, 0), (bufferX, bufferY));
        }
        finally
        {
            File.Delete(cachePath);
            File.Delete(cachePath + ".tmp");
        }
    }

    [Fact]
    public void Runtime_snapshot_rejects_corrupted_liquid_runtime_payload()
    {
        byte[] file = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoader.TryLoad(file, limits, out WorldFileData? sourceWorld);
        WorldFileData world = Assert.IsType<WorldFileData>(sourceWorld);
        Assert.True(world.Tiles.LiquidUpdates.TryEnqueue(1, 1, delay: 5, kill: 2));
        Assert.True(world.Tiles.LiquidUpdates.TryBuffer(0, 2));

        string cachePath = Path.Combine(Path.GetTempPath(), $"terraruntime-liquid-{Guid.NewGuid():N}.runtime-world");
        var stamp = new RuntimeWorldSourceStamp(file.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(cachePath, file, stamp, world).IsWritten);
            byte[] bytes = File.ReadAllBytes(cachePath);
            int liquidHeaderOffset = bytes.AsSpan().IndexOf("LIQSTATE"u8);
            Assert.True(liquidHeaderOffset >= 0);
            bytes[liquidHeaderOffset + 64] ^= 0x01;
            File.WriteAllBytes(cachePath, bytes);

            RuntimeWorldSnapshotLoadDiagnostic diagnostic = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                stamp,
                limits,
                out WorldFileData? loadedWorld);

            Assert.Equal(RuntimeWorldSnapshotLoadResult.LiquidQueueHashMismatch, diagnostic.Result);
            Assert.Null(loadedWorld);
        }
        finally
        {
            File.Delete(cachePath);
            File.Delete(cachePath + ".tmp");
        }
    }

    [Fact]
    public void Runtime_snapshot_rejects_corrupted_prepared_runtime_payload()
    {
        byte[] file = CreateCompleteWorld();
        WorldFileLoadLimits limits = CreateLimits();
        WorldFileLoader.TryLoad(file, limits, out WorldFileData? sourceWorld);
        WorldFileData world = Assert.IsType<WorldFileData>(sourceWorld);

        string cachePath = Path.Combine(Path.GetTempPath(), $"terraruntime-prepared-{Guid.NewGuid():N}.runtime-world");
        var stamp = new RuntimeWorldSourceStamp(file.LongLength, DateTime.UtcNow.Ticks);
        try
        {
            Assert.True(RuntimeWorldSnapshotCache.TryWriteAtomic(cachePath, file, stamp, world).IsWritten);
            byte[] bytes = File.ReadAllBytes(cachePath);
            int preparedHeaderOffset = bytes.AsSpan().IndexOf("PREPARED"u8);
            Assert.True(preparedHeaderOffset >= 0);
            bytes[preparedHeaderOffset + 32] ^= 0x01;
            File.WriteAllBytes(cachePath, bytes);

            RuntimeWorldSnapshotLoadDiagnostic diagnostic = RuntimeWorldSnapshotCache.TryLoad(
                cachePath,
                stamp,
                limits,
                out WorldFileData? loadedWorld);

            Assert.Equal(RuntimeWorldSnapshotLoadResult.PreparedPayloadHashMismatch, diagnostic.Result);
            Assert.Null(loadedWorld);
        }
        finally
        {
            File.Delete(cachePath);
            File.Delete(cachePath + ".tmp");
        }
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
