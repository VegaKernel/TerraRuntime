using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class RuntimeWiring1458Tests
{
    [Theory]
    [InlineData(2, 184)]
    [InlineData(17, 166)]
    [InlineData(37, 58)]
    public void Authenticated_item_statue_birth_is_funded_and_reaches_both_clients_before_the_switch(int style, int type)
    {
        var itemsReplication = new RuntimeWorldItemReplicationRegistry();
        var items = new RuntimeWorldItemStore(itemsReplication);
        var world = new RuntimePlayerUpdateWorld1458(100, 100, 60, false, false)
        {
            GetGoodWorld = false, InfectedSeed = false, NotTheBeesWorld = false, NoTrapsWorld = false,
            TenthAnniversaryWorld = false, DontStarveWorld = false, DrunkWorld = false, DualDungeonsSeed = false,
            JourneyMode = false, HallowBackground = 0, OnlyShimmerOceanWorlds = false
        };
        using var scene = new Scene(sourceWorld: world, calendar: default(RuntimeTownCommerceWorldFacts1458) with { WorldSurface = 60 }, items: items, itemReplication: itemsReplication);
        scene.State.Apply(new PlayerHealthRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 80, 100)));
        scene.State.Apply(new PlayerManaRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 0, 20)));
        scene.State.Apply(new PlayerEquipmentRuntimeCommand(scene.Owner, new(scene.Owner.Player.Slot, 0, 0, 0, 0, 0)));
        Assert.True(itemsReplication.TryRegister(scene.Owner.Source, scene.OwnerOutbound));
        var peer = new ConnectionHandle(GameCommandSourceId.FromConnection(8001), new(new PlayerSlotId(1), new(1)));
        Assert.True(itemsReplication.TryRegister(peer.Source, scene.PeerOutbound));
        var request = new PlayerSpawnCommitRequest(scene.Owner.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        itemsReplication.PlayerSpawned(scene.Owner, in request);
        request = request with { ClaimedSlot = peer.Player.Slot }; itemsReplication.PlayerSpawned(peer, in request);
        for (int x = 15; x <= 20; x++) scene.Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(15, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
            scene.Tiles.Set(20 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(style * 36 + dx * 18),
                FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        scene.DrainFrames(scene.OwnerOutbound); scene.DrainFrames(scene.PeerOutbound);
        scene.Press(15, 30);
        Assert.Equal(1, items.ActiveCount); Assert.True(items.TryGetActive(0, out var item)); Assert.Equal(type, item.ItemNetId);
        using (var custody = items.CreateAllocationPreview()) Assert.True(custody.TryTakeSource(in item, 1, requireFundedQuantity: true));
        var ownerFrames = scene.DrainFrames(scene.OwnerOutbound); var peerFrames = scene.DrainFrames(scene.PeerOutbound);
        Assert.Contains((byte)21, ownerFrames); Assert.Equal(ownerFrames.Append((byte)59), peerFrames);
        scene.Press(15, 30); Assert.Equal(1, items.ActiveCount);
        Assert.Empty(scene.DrainFrames(scene.OwnerOutbound)); Assert.Equal(new byte[] { 59 }, scene.DrainFrames(scene.PeerOutbound));
    }
}
