using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(4, 1)] [InlineData(5, 73)] [InlineData(7, 49)] [InlineData(8, 55)]
    [InlineData(9, 46)] [InlineData(10, 21)] [InlineData(13, 24)] [InlineData(18, 67)]
    [InlineData(27, 85)] [InlineData(30, 6)] [InlineData(35, 2)] [InlineData(42, 58)]
    public void Authenticated_statue_birth_retains_provenance_and_waits_for_the_existing_npc_sync(int style, int type)
    {
        var registry = new RuntimeNpcReplicationRegistry(); var npcs = new RuntimeNpcStore(commitSink: registry);
        var items = new TerraRuntime.Core.Worlds.RuntimeWorldItemStore();
        using var scene = new Scene(items: items, calendar: default(RuntimeTownCommerceWorldFacts1458), npcs: npcs, npcReplication: registry);
        Assert.True(registry.TryRegister(scene.Owner.Source, scene.OwnerOutbound));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(registry.TryRegister(peer.Source, scene.PeerOutbound));
        var spawn = new PlayerSpawnCommitRequest(scene.Owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0); registry.PlayerSpawned(scene.Owner, in spawn);
        spawn = spawn with { ClaimedSlot = peer.Player.Slot }; registry.PlayerSpawned(peer, in spawn);
        for (int x = 15; x <= 20; x++) scene.Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(15, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
            scene.Tiles.Set(20 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(15, 30); Assert.Equal(1, npcs.ActiveCount); Assert.True(npcs.TryGetActive(0, out var born));
        Assert.Equal(type, born.Type); Assert.True(born.Simulation.SpawnedFromStatue); Assert.True(born.Simulation.CanBeReplacedByOtherNpcs);
        Assert.Equal(0f, born.Simulation.MoneyValue); Assert.Equal(0f, born.Simulation.SpawnSlotCost); Assert.True(npcs.HasPendingBirth(born.Handle));
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.Press(15, 30); Assert.True(npcs.TryGet(born.Handle, out var same)); Assert.Equal(born, same);
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
        npcs.PublishPendingBirths(); Assert.False(npcs.HasPendingBirth(born.Handle));
        Assert.Equal(new byte[] { 23 }, scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 23 }, scene.DrainFrames(scene.PeerOutbound));
        if (type is 46 or 55)
        {
            scene.State.Apply(new ClientNpcCatchRuntimeCommand(scene.Owner, new(born.Handle.Slot)));
            Assert.False(npcs.TryGet(born.Handle, out _)); Assert.Equal(0, items.ActiveCount);
            Assert.Equal(new byte[] { 23, 106 }, scene.DrainFrames(scene.OwnerOutbound));
            Assert.Equal(new byte[] { 23, 106 }, scene.DrainFrames(scene.PeerOutbound));
        }

    }
}
