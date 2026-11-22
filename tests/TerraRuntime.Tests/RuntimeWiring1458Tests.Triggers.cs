using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Fact]
    public void Writer_tick_presses_weighted_plate_and_disconnect_releases_immediately()
    {
        using var scene = new Scene();
        Assert.True(scene.Tiles.TryAttachPressurePlates([], 16));
        scene.Put(30, 40, 428); scene.Put(31, 40, 4);
        var graph = Graph(scene);
        Assert.True(graph.Players.TryGet(scene.Owner.Player, out var member));
        Assert.True(member.TryAdvanceRevision()); member.PositionX = 482; member.PositionY = 608;
        scene.State.Tick();
        Assert.Equal(new WorldPressurePlate[] { new(30, 40) }, scene.Tiles.RuntimePressurePlates!.CaptureSnapshot());
        Assert.Equal((short)66, scene.Tiles.Get(31, 40).FrameX);
        Assert.Equal(new byte[] { 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.State.Apply(new PlayerDisconnectRuntimeCommand(scene.Owner));
        Assert.Empty(scene.Tiles.RuntimePressurePlates.CaptureSnapshot());
        Assert.Equal((short)0, scene.Tiles.Get(31, 40).FrameX);
        Assert.Equal(new byte[] { 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
    }

    [Fact]
    public void Writer_tick_adopts_all_sensor_frames_before_any_deferred_pulse()
    {
        using var scene = new Scene();
        scene.Put(30, 40, 423); scene.Put(31, 40, 423); scene.Put(32, 40, 4);
        Assert.True(scene.Tiles.TryAttachTileEntities([
            new(0, 30, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(1, false)),
            new(1, 31, 40, WorldTileEntityKind.LogicSensor, new WorldLogicSensorPayload(1, false))], 16));
        var graph = Graph(scene);
        graph.WorldTileAuthority.BindWiringTriggers(graph.PlayerSnapshots,
            new RuntimeWorldClock(0, true, VanillaMoonPhase.Full, 0, 1));
        scene.State.Tick();
        Assert.All(scene.Tiles.RuntimeTileEntities!.CaptureSnapshot(), sensor =>
            Assert.Equal(new WorldLogicSensorPayload(1, true), sensor.Payload));
        Assert.Equal((short)18, scene.Tiles.Get(30, 40).FrameX);
        Assert.Equal((short)18, scene.Tiles.Get(31, 40).FrameX);
        Assert.Equal((short)0, scene.Tiles.Get(32, 40).FrameX);
        Assert.Equal(new byte[] { 20, 20, 20, 59, 20, 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.State.Tick();
        Assert.Empty(scene.DrainFrames(scene.PeerOutbound));
    }

    private static ServerRuntimeComposition Graph(Scene scene) =>
        (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.State)!;
}
