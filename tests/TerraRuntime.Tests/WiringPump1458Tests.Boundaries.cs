using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringPump1458Tests
{
    [Theory]
    [InlineData("floor")] [InlineData("frame")] [InlineData("actuator")] [InlineData("neighbor")]
    public void Unsupported_pump_context_cannot_partially_move_material_or_schedule_work(string fault)
    {
        var f = new Fixture();
        int x = fault == "neighbor" ? 39 : fault == "floor" ? 40 : 41, y = fault == "floor" ? 32 : fault == "neighbor" ? 31 : 30;
        var tile = f.Tiles.Get(x, y);
        if (fault == "floor") tile = default;
        if (fault == "frame") tile.FrameX++;
        if (fault == "actuator") tile.Flags |= WorldTileFlags.Actuator;
        if (fault == "neighbor") tile = new WorldTile { Type = 4, Flags = WorldTileFlags.Active };
        f.Tiles.Set(x, y, in tile);
        var input = f.Tiles.Get(24, 31); var output = f.Tiles.Get(40, 31);
        Assert.True(f.Circuit.HitSwitch(15, 30));
        Assert.Equal(input, f.Tiles.Get(24, 31)); Assert.Equal(output, f.Tiles.Get(40, 31));
        Assert.False(f.Tiles.LiquidUpdates.HasPendingWork); Assert.Equal(0, f.Registry.PendingLiquidUpdates);
    }

    [Fact]
    public void Pending_liquid_material_is_read_at_flush_even_if_a_pump_emits_no_new_offer_for_the_drained_cell()
    {
        var f = new Fixture();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(256, 262144, 8192));
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1458143), new(new PlayerSlotId(1), new(1)));
        Assert.True(f.Registry.TryRegister(connection.Source, queue));
        var spawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0); f.Registry.PlayerSpawned(connection, in spawn);
        var pending = new TerrariaLiquidState(24, 31, 100, 0); Assert.True(f.Registry.RetainSourceLiquid(in pending));
        Assert.True(f.Circuit.HitSwitch(15, 30)); Assert.Equal(0, f.Tiles.Get(24, 31).LiquidAmount);
        f.Registry.FlushPendingLiquids(); Assert.Equal(0, f.Registry.EncodeFailures);
        // Same original legacy48 bytes as the prequeued source trace's drained24,31 cell.
        Assert.Contains("09003018001F000000", Drain(queue));
    }

    private sealed class Fixture
    {
        internal readonly WorldTileStore Tiles = new(new(80, 100));
        internal readonly RuntimeTileManipulationReplicationRegistry Registry = new();
        internal readonly WorldWiringCircuit1458 Circuit;
        internal Fixture()
        {
            var state = new ServerRuntimeState(worldTiles: Tiles, tileManipulationReplication: Registry,
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(1458));
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(graph.WorldTileAuthority)!;
            for (int x = 15; x <= 40; x++) Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
            Tiles.Set(15, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            PutPump(Tiles, 24, 30, 142); PutPump(Tiles, 40, 30, 143);
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++)
            {
                var tile = Tiles.Get(24 + dx, 30 + dy); tile.LiquidAmount = 100; Tiles.Set(24 + dx, 30 + dy, in tile);
            }
        }
    }

    internal static void PutPump(WorldTileStore tiles, int left, int top, ushort type)
    {
        for (int dx = 0; dx < 2; dx++)
        {
            for (int dy = 0; dy < 2; dy++)
                tiles.Set(left + dx, top + dy, new WorldTile { Type = type, FrameX = (short)(dx * 18), FrameY = (short)(dy * 18),
                    Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            tiles.Set(left + dx, top + 2, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        }
    }
}
