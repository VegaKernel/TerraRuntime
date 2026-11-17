using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(105)] [InlineData(349)] [InlineData(593)] [InlineData(594)]
    public void Authenticated_animated_devices_publish_original_events_to_playing_peers(ushort type)
    {
        using var scene = new Scene();
        WiringAnimatedDevice1458Tests.PlaceDevice(scene.Tiles, type);
        for (int x = 20; x < 30; x++) scene.Put(x, 30, x == 20 ? (ushort)136 : (ushort)0);
        var hidden = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(256, 262144, 8192));
        var spectator = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(256, 262144, 8192));
        var remote = new ConnectionHandle(GameCommandSourceId.FromConnection(8800), new(new PlayerSlotId(2), new(1)));
        Assert.True(scene.Replication.TryRegister(remote.Source, hidden, new NoAnimatedSections()));
        var spawn = new PlayerSpawnCommitRequest(remote.Player.Slot, 20, 20, 0, 0, 0, 0, 0); scene.Replication.PlayerSpawned(remote, in spawn);
        Assert.True(scene.Replication.TryRegister(GameCommandSourceId.FromConnection(8801), spectator));
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        for (int pulse = 0; pulse < 4; pulse++)
        {
            scene.Press(20, 30);
            byte[] own = pulse == 0 && type == 105 ? [77, 20] : type is 105 or 349 ? [20, 77] : [20, 77, 77];
            Assert.Equal(own, scene.DrainFrames(scene.OwnerOutbound));
            Assert.Equal(own.Concat(new byte[] { 59 }), scene.DrainFrames(scene.PeerOutbound));
            Assert.Equal(type is 593 or 594 ? new byte[] { 77, 77, 59 } : new byte[] { 77, 59 }, scene.DrainFrames(hidden));
            Assert.Empty(scene.DrainFrames(spectator));
        }
        Assert.Equal(type == 105 ? (ushort)349 : type, scene.Tiles.Get(30, 30).Type);
        Assert.Equal((short)0, scene.Tiles.Get(30, 30).FrameX);
        Assert.True(TerrariaTemporaryAnimationCodec1458.TryEncode(0, 349, 30, 30, out var forged));
        var input = Decode(forged); scene.Sink.OnFrame(in input);
        Assert.Empty(scene.Commands.Commands); Assert.Equal((short)0, scene.Tiles.Get(30, 30).FrameX);
    }

    private sealed class NoAnimatedSections : IPlayerSectionVisibility
    {
        public bool OwnsSectionAtTile(int x, int y) => false;
    }
}
