using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class WorldTileSaveShadowSynchronizerTests
{
    [Fact]
    public void Bootstrap_is_bounded_and_completes_over_multiple_calls()
    {
        var live = new WorldTileStore(new WorldDimensions(201, 151));
        var synchronizer = new WorldTileSaveShadowSynchronizer(live, dirtyBatchCapacity: 2);

        Assert.Equal(4, synchronizer.RemainingBootstrapSections);
        Assert.Equal(1, synchronizer.CaptureBootstrap(1));
        Assert.Equal(3, synchronizer.RemainingBootstrapSections);
        Assert.False(synchronizer.IsBootstrapped);
        Assert.False(synchronizer.TryCaptureImage(out _));

        Assert.Equal(2, synchronizer.CaptureBootstrap(2));
        Assert.Equal(1, synchronizer.RemainingBootstrapSections);
        Assert.False(synchronizer.IsBootstrapped);

        Assert.Equal(1, synchronizer.CaptureBootstrap(8));
        Assert.Equal(0, synchronizer.RemainingBootstrapSections);
        Assert.True(synchronizer.IsBootstrapped);
        Assert.True(synchronizer.TryCaptureImage(out WorldTileSaveImage? image));
        Assert.NotNull(image);
        Assert.Equal(live.Count, image!.Count);
    }

    [Fact]
    public void Dirty_mutation_during_bootstrap_is_retained_for_first_steady_state_sync()
    {
        var live = new WorldTileStore(new WorldDimensions(201, 150));
        var synchronizer = new WorldTileSaveShadowSynchronizer(live, dirtyBatchCapacity: 2);
        var initial = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in initial);

        Assert.Equal(1, synchronizer.CaptureBootstrap(1));
        Assert.False(synchronizer.IsBootstrapped);

        var updated = new WorldTile { Type = 2, Flags = WorldTileFlags.Active };
        live.Set(5, 6, in updated);

        Assert.Equal(0, synchronizer.CaptureDirty(2));
        Assert.Equal(1, synchronizer.CaptureBootstrap(1));
        Assert.True(synchronizer.IsBootstrapped);
        Assert.True(synchronizer.TryCaptureImage(out WorldTileSaveImage? beforeDirty));
        Assert.NotNull(beforeDirty);
        Assert.Equal((ushort)1, beforeDirty!.Get(5, 6).Type);

        Assert.Equal(1, synchronizer.CaptureDirty(2));
        Assert.True(synchronizer.TryCaptureImage(out WorldTileSaveImage? afterDirty));
        Assert.NotNull(afterDirty);
        Assert.Equal((ushort)2, afterDirty!.Get(5, 6).Type);
    }

    [Fact]
    public void Dirty_capture_respects_requested_and_configured_bounds()
    {
        var live = new WorldTileStore(new WorldDimensions(401, 150));
        var synchronizer = new WorldTileSaveShadowSynchronizer(live, dirtyBatchCapacity: 2);

        Assert.Equal(3, synchronizer.CaptureBootstrap(3));
        Assert.True(synchronizer.IsBootstrapped);

        var tile = new WorldTile { Type = 1, Flags = WorldTileFlags.Active };
        live.Set(1, 1, in tile);
        live.Set(201, 1, in tile);
        live.Set(400, 1, in tile);

        Assert.Equal(1, synchronizer.CaptureDirty(1));
        Assert.Equal(2, synchronizer.CaptureDirty(10));
        Assert.Equal(0, synchronizer.CaptureDirty(10));
    }
}
