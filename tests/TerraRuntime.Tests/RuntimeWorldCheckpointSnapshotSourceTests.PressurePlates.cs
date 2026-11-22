using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWorldCheckpointSnapshotSourceTests
{
    [Fact]
    public void Checkpoint_detaches_live_pressure_keys_and_sensor_payload_from_future_releases()
    {
        var tiles = new WorldTileStore(new(80, 100));
        tiles.Set(30, 40, new() { Type = 428, Flags = WorldTileFlags.Active });
        Assert.True(tiles.TryAttachPressurePlates([], 16));
        var sensor = new WorldTileEntity(0, 31, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(4, true));
        tiles.Set(31, 40, new() { Type = 423, FrameX = 18, FrameY = 54, Flags = WorldTileFlags.Active });
        Assert.True(tiles.TryAttachTileEntities([sensor], 16));
        var player = new PlayerHandle(new PlayerSlotId(0), new(1));
        Assert.True(tiles.RuntimePressurePlates!.Update(player, 482, 608, 20, 42, (_, _) => { }));
        var source = new RuntimeWorldCheckpointSnapshotSource(tiles, new RuntimeChestStore([]), 1);
        source.CaptureTileBootstrap(1);
        Assert.True(source.TryCapture(out var snapshot));
        tiles.RuntimePressurePlates.ResetPlayer(player, (_, _) => { });
        var tile = tiles.Get(31, 40);
        Assert.True(tiles.RuntimeTileEntities!.TrySetLogicSensorState(sensor, in tile, false));
        Assert.Equal(new WorldPressurePlate[] { new(30, 40) }, snapshot!.PressurePlates);
        Assert.Equal(new WorldLogicSensorPayload(4, true), Assert.Single(snapshot.TileEntities!).Payload);
        Assert.Empty(tiles.RuntimePressurePlates.CaptureSnapshot());
        Assert.Equal(new WorldLogicSensorPayload(4, false), Assert.Single(tiles.RuntimeTileEntities.CaptureSnapshot()).Payload);
    }
}
