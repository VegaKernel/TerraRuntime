using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(40, 22)] [InlineData(41, 18)]
    public void Authenticated_town_statue_sends65_to_both_peers_without_forcing23(int style, int type)
    {
        var registry = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: registry);
        var items = new TerraRuntime.Core.Worlds.RuntimeWorldItemStore();
        using var scene = new Scene(items: items, calendar: default(RuntimeTownCommerceWorldFacts1458), npcs: npcs, npcReplication: registry);
        Assert.True(registry.TryRegister(scene.Owner.Source, scene.OwnerOutbound));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(peer.Source, scene.PeerOutbound));
        var spawn = new PlayerSpawnCommitRequest(scene.Owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(scene.Owner, in spawn);
        spawn = spawn with { ClaimedSlot = peer.Player.Slot }; registry.PlayerSpawned(peer, in spawn);
        var initial = new NpcStateUpdate(type, (short)type, 100, 100, 1, -2, 255, default, NpcSimulationState.Initial);
        Assert.True(npcs.TrySpawn(0, in initial, out var before));
        for (int x = 15; x <= 20; x++) scene.Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(15, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
            scene.Tiles.Set(20 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(15, 30); Assert.True(npcs.TryGet(before.Handle, out var after));
        Assert.Equal(1, npcs.ActiveCount); Assert.Equal(14, after.Simulation.TeleportStyle); Assert.Equal(1f, after.Simulation.TeleportTime);
        Assert.False(after.Simulation.SpawnedFromStatue); Assert.Equal(before.VelocityX, after.VelocityX); Assert.Equal(before.VelocityY, after.VelocityY);
        Assert.Equal(new byte[] { 65 }, scene.DrainFrames(scene.OwnerOutbound));
        Assert.Equal(new byte[] { 65, 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.Press(15, 30); Assert.True(npcs.TryGet(before.Handle, out var same)); Assert.Equal(after, same);
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
    }
}
