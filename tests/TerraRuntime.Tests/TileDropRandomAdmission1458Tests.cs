using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Protocol;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class TileDropRandomAdmission1458Tests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Fixed_drop_rejects_unowned_reservation_but_known_network_sentinel_uses_source_owner_facts(bool wall, bool networkSentinel)
    {
        var random=new VanillaUnifiedRandom1458(1458);var tiles=new WorldTileStore(new WorldDimensions(200,150));
        var registry=networkSentinel?new RuntimeWorldItemReplicationRegistry():null;
        var items=networkSentinel?WorldItemSourceAllocation1458Tests.Arrange(10,registry):new RuntimeWorldItemStore();
        WorldItemDropReservation held=default;if(!networkSentinel)Assert.True(items.TryReserveDropSlot(out held));
        var state=new ServerRuntimeState(worldTiles:tiles,worldItems:items,worldItemSpawnRandom:new SystemWorldItemSpawnRandom(random),worldItemReplication:registry);
        var slots=new PlayerSlotPool(1);Assert.True(slots.TryAcquireConnection(out var lease));
        using var session=new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));session.ObserveWorldRequest();session.ObserveSectionRequest();
        var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(9817),session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,20,20,0,0,0,0,0)));
        state.Apply(new PlayerEquipmentRuntimeCommand(connection,new(session.Slot,0,1,0,
            checked((short)(wall?VanillaItemIds.CopperHammer:VanillaItemIds.CopperPickaxe).Value),0)));
        var tile=new WorldTile{Type=1,Flags=WorldTileFlags.Active};
        if(wall){tile.Flags=0;Assert.True(tile.TrySetWallType(new WallTypeId(1)));}
        tiles.Set(10,10,tile);var before=random.Clone();
        state.Apply(new ClientTileManipulationRuntimeCommand(connection,new((byte)(wall?TerrariaTileManipulationAction.KillWall:TerrariaTileManipulationAction.KillTile),10,10,0,0)));
        if(networkSentinel)
        {
            before.Next(-30,31);before.Next(-40,-15);
            Assert.True(random.HasSameState(before));Assert.Equal(1,state.AppliedClientTileManipulations);Assert.Equal(0,state.RejectedWorldItemAllocations);
            if(wall)Assert.Equal(0,tiles.Get(10,10).WallType.Value);else Assert.False(tiles.Get(10,10).IsActive);
            Assert.False(items.TryGetActive(400,out _)); // Source21/22 transient, never a physical runtime slot.
        }
        else
        {
            Assert.Equal(tile,tiles.Get(10,10));Assert.True(random.HasSameState(before));
            Assert.Equal(0,state.AppliedWorldItemAllocations);Assert.Equal(0,state.AppliedClientTileManipulations);Assert.Equal(1,state.RejectedWorldItemAllocations);
        }
        Assert.Equal(networkSentinel?400:0,items.ActiveCount);if(!networkSentinel)Assert.True(items.HasDropReservation(held));
    }

    [Fact]
    public void Accepted_fixed_tile_drop_adopts_exact_default_velocity_draws_once()
    {
        var random=new VanillaUnifiedRandom1458(1458);var tiles=new WorldTileStore(new WorldDimensions(200,150));var items=new RuntimeWorldItemStore();
        var state=new ServerRuntimeState(worldTiles:tiles,worldItems:items,worldItemSpawnRandom:new SystemWorldItemSpawnRandom(random));
        var slots=new PlayerSlotPool(1);Assert.True(slots.TryAcquireConnection(out var lease));
        using var session=new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));session.ObserveWorldRequest();session.ObserveSectionRequest();
        var connection=new ConnectionHandle(GameCommandSourceId.FromConnection(9818),session.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(connection,session,new(session.Slot,20,20,0,0,0,0,0)));
        state.Apply(new PlayerEquipmentRuntimeCommand(connection,new(session.Slot,0,1,0,checked((short)VanillaItemIds.CopperPickaxe.Value),0)));
        tiles.Set(10,10,new WorldTile{Type=1,Flags=WorldTileFlags.Active});
        var expected=random.Clone();float x=expected.Next(-30,31)*.1f,y=expected.Next(-40,-15)*.1f;
        state.Apply(new ClientTileManipulationRuntimeCommand(connection,new((byte)TerrariaTileManipulationAction.KillTile,10,10,0,0)));
        Assert.False(tiles.Get(10,10).IsActive);Assert.True(items.TryGetActive(0,out var drop));
        Assert.Equal(x,drop.VelocityX);Assert.Equal(y,drop.VelocityY);Assert.True(random.HasSameState(expected));
    }
}
