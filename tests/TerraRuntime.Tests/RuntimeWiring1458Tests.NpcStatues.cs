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
    [InlineData(16, -1)]
    [InlineData(23, -1)]
    [InlineData(28, -1)]
    [InlineData(50, -1)]
    [InlineData(51, -1)]
    [InlineData(52, -1)]
    [InlineData(53, -1)]
    [InlineData(54, -1)]
    [InlineData(55, -1)]
    [InlineData(56, -1)]
    [InlineData(57, -1)]
    [InlineData(58, -1)]
    [InlineData(59, -1)]
    [InlineData(60, -1)]
    [InlineData(61, -1)]
    [InlineData(62, -1)]
    [InlineData(63, -1)]
    [InlineData(64, -1)]
    [InlineData(65, -1)]
    [InlineData(66, -1)]
    [InlineData(67, -1)]
    [InlineData(68, -1)]
    [InlineData(69, -1)]
    [InlineData(70, -1)]
    [InlineData(71, -1)]
    [InlineData(72, -1)]
    [InlineData(73, -1)]
    [InlineData(74, -1)]
    [InlineData(75, -1)]
    [InlineData(76, -1)]
    [InlineData(77, -1)]
    [InlineData(78, -1)]
    [InlineData(79, -1)]
    [InlineData(80, -1)]
    [InlineData(81, -1)]
    [InlineData(82, -1)]
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
            scene.Tiles.Set(20 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(style % 55 * 36 + dx * 18),
                FrameY = (short)(style / 55 * 54 + dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(15, 30); Assert.Equal(1, npcs.ActiveCount); Assert.True(npcs.TryGetActive(0, out var born));
        if (type > 0) Assert.Equal(type, born.Type); Assert.True(born.Simulation.SpawnedFromStatue); Assert.True(born.Simulation.CanBeReplacedByOtherNpcs);
        Assert.Equal(0f, born.Simulation.MoneyValue); Assert.Equal(0f, born.Simulation.SpawnSlotCost); Assert.True(npcs.HasPendingBirth(born.Handle));
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
        scene.Press(15, 30); Assert.True(npcs.TryGet(born.Handle, out var same)); Assert.Equal(born, same);
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
        npcs.PublishPendingBirths(); Assert.False(npcs.HasPendingBirth(born.Handle));
        Assert.Equal(new byte[] { 23 }, scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 23 }, scene.DrainFrames(scene.PeerOutbound));
        if (TerraRuntime.Gameplay.Npcs.VanillaNpcCatchCatalog1458.TryGetCatchItem(born.TypeIdentity, out _))
        {
            scene.State.Apply(new ClientNpcCatchRuntimeCommand(scene.Owner, new(born.Handle.Slot)));
            Assert.False(npcs.TryGet(born.Handle, out _)); Assert.Equal(0, items.ActiveCount);
            Assert.Equal(new byte[] { 23, 106 }, scene.DrainFrames(scene.OwnerOutbound));
            Assert.Equal(new byte[] { 23, 106 }, scene.DrainFrames(scene.PeerOutbound));
        }

    }
}
