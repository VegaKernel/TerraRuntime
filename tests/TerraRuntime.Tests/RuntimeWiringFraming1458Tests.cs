using System.Runtime.CompilerServices;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Protocol.Multiplicity;
using TerraRuntime.Tests.Larva1458;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RuntimeWiringFraming1458Tests
{
    [Fact]
    public void Real_switch_command_actuates_support_and_adopts_attached_switch_drop_before_publication()
    {
        using var scene = new LarvaRuntimeFixture1458(windows: true);
        PutCircuit(scene.Tiles);
        scene.Tiles.Set(20, 20, Cell(136));
        int callbacks = 0;
        var previous = ItemSink(scene.Items);
        ItemSink(scene.Items) = new ItemObserver(previous, (_, item) =>
        {
            callbacks++;
            Assert.True(scene.Tiles.Get(20, 21).IsActuated);
            Assert.False(scene.Tiles.Get(20, 20).IsActive);
            Assert.Equal((short)538, item.ItemNetId);
        });
        scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
        Assert.True(scene.Tiles.Get(20, 21).IsActuated);
        Assert.False(scene.Tiles.Get(20, 20).IsActive);
        Assert.Equal(1, scene.Items.ActiveCount);
        var items = new WorldItemSnapshot[1];
        Assert.Equal(1, scene.Items.CopyActive(items));
        Assert.Equal((short)538, items[0].ItemNetId);
        Assert.Equal((short)1, items[0].Stack);
        Assert.True(callbacks > 0);
        Assert.False(VanillaWorldSolidCollision.Intersects(scene.Tiles, 320, 336, 16, 16));
        Assert.True(scene.Tiles.PersistenceDirtySections.DirtyCount > 0);
        // A second pulse reactivates the existing support without manufacturing another switch/drop.
        scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
        Assert.False(scene.Tiles.Get(20, 21).IsActuated);
        Assert.Equal(1, scene.Items.ActiveCount);
        Assert.True(VanillaWorldSolidCollision.Intersects(scene.Tiles, 320, 336, 16, 16));
    }

    [Fact]
    public void Source_torch_active_support_rule_keeps_torch_attached_without_drops_or_RNG_draws()
    {
        foreach (short torchFrame in new short[] { 0, 66 })
        {
            using var scene = new LarvaRuntimeFixture1458(windows: true);
            PutCircuit(scene.Tiles);
            var torch = Cell(4); torch.FrameX = torchFrame; torch.Flags = WorldTileFlags.Active;
            scene.Tiles.Set(20, 20, in torch);
            var beforeRandom = scene.Random.Clone();
            scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
            Assert.True(scene.Tiles.Get(20, 21).IsActuated);
            Assert.Equal(torch, scene.Tiles.Get(20, 20));
            Assert.Equal(0, scene.Items.ActiveCount);
            Assert.True(scene.Random.HasSameState(beforeRandom));
            scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
            Assert.False(scene.Tiles.Get(20, 21).IsActuated);
            Assert.Equal(torch, scene.Tiles.Get(20, 20));
            Assert.True(scene.Random.HasSameState(beforeRandom));
        }
    }

    [Fact]
    public void Unsupported_neighbor_framing_rejects_entire_actuation_before_tile_RNG_or_drop_adoption()
    {
        using var scene = new LarvaRuntimeFixture1458(windows: true);
        PutCircuit(scene.Tiles);
        var unsupported = Cell(34);
        scene.Tiles.Set(21, 20, in unsupported);
        var support = scene.Tiles.Get(20, 21);
        var beforeRandom = scene.Random.Clone();
        scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
        Assert.Equal(support, scene.Tiles.Get(20, 21));
        Assert.Equal(unsupported, scene.Tiles.Get(21, 20));
        Assert.Equal(0, scene.Items.ActiveCount);
        Assert.True(scene.Random.HasSameState(beforeRandom));
        Assert.True(VanillaWorldSolidCollision.Intersects(scene.Tiles, 320, 336, 16, 16));
    }

    [Fact]
    public void Torch_read_halo_liquid_outside_source_frame_visits_is_not_queued_by_cosmetic_changes()
    {
        using var scene = new LarvaRuntimeFixture1458(windows: true);
        var trigger = Cell(136); trigger.Wall = 1;
        scene.Tiles.Set(20, 22, in trigger);
        scene.Tiles.Set(21, 22, new WorldTile { Flags = WorldTileFlags.WireRed });
        scene.Tiles.Set(22, 22, new WorldTile { Flags = WorldTileFlags.WireRed });
        var support = Cell(1); support.Flags |= WorldTileFlags.Actuator;
        scene.Tiles.Set(22, 21, in support);
        var torch = Cell(4); torch.Flags = WorldTileFlags.Active;
        scene.Tiles.Set(21, 21, in torch);
        var halo = new WorldTile { LiquidAmount = 100, LiquidKind = WorldLiquidKind.Honey };
        scene.Tiles.Set(20, 20, in halo);
        var random = scene.Random.Clone();
        Assert.False(scene.Tiles.LiquidUpdates.HasPendingWork);
        scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(20, 22)));
        Assert.True(scene.Tiles.Get(22, 21).IsActuated);
        Assert.Equal((short)44, scene.Tiles.Get(21, 21).FrameX);
        Assert.Equal(halo, scene.Tiles.Get(20, 20));
        Assert.False(scene.Tiles.LiquidUpdates.HasPendingWork);
        Assert.Equal(0, scene.Items.ActiveCount);
        Assert.True(scene.Random.HasSameState(random));
    }

    [Fact]
    public void Wet_source_frame_visit_rejects_all_prepared_actuation_and_framing_without_liquid_or_RNG_adoption()
    {
        using var scene = new LarvaRuntimeFixture1458(windows: true);
        PutCircuit(scene.Tiles);
        var support = scene.Tiles.Get(20, 21);
        var torch = Cell(4); torch.Flags = WorldTileFlags.Active;
        scene.Tiles.Set(20, 20, in torch);
        // This inactive painted neighbor is framed before the wet cell in source column-major order.
        // Its staged cleanup must also roll back when the later source Liquid.AddWater is unadmitted.
        var painted = new WorldTile { TileColor = 7, Flags = WorldTileFlags.InvisibleBlock };
        scene.Tiles.Set(19, 20, in painted);
        var wet = new WorldTile { LiquidAmount = 100, LiquidKind = WorldLiquidKind.Honey };
        scene.Tiles.Set(21, 20, in wet);
        var random = scene.Random.Clone();
        scene.Apply(new ClientHitSwitchRuntimeCommand1458(scene.Connection, new(18, 21)));
        Assert.Equal(support, scene.Tiles.Get(20, 21));
        Assert.Equal(torch, scene.Tiles.Get(20, 20));
        Assert.Equal(painted, scene.Tiles.Get(19, 20));
        Assert.Equal(wet, scene.Tiles.Get(21, 20));
        Assert.False(scene.Tiles.LiquidUpdates.HasPendingWork);
        Assert.Equal(0, scene.Items.ActiveCount);
        Assert.True(scene.Random.HasSameState(random));
        Assert.Equal((short)18, scene.Tiles.Get(18, 21).FrameY); // The separate permitted press still occurs.
    }

    private static void PutCircuit(WorldTileStore tiles)
    {
        tiles.Set(18, 21, Cell(136));
        tiles.Set(19, 21, new WorldTile { Flags = WorldTileFlags.WireRed });
        var support = Cell(1); support.Flags |= WorldTileFlags.Actuator;
        tiles.Set(20, 21, in support);
    }

    private static WorldTile Cell(ushort type) => new() { Type = type, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed };
    private sealed class ItemObserver(IWorldItemStateCommitSink? previous,
        Action<WorldItemStateCommitKind, WorldItemSnapshot> observer) : IWorldItemStateCommitSink
    {
        public void WorldItemStateCommitted(WorldItemStateCommitKind kind, in WorldItemSnapshot snapshot)
        { observer(kind, snapshot); previous?.WorldItemStateCommitted(kind, in snapshot); }
    }
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_commitSink")]
    private static extern ref IWorldItemStateCommitSink? ItemSink(RuntimeWorldItemStore owner);
}
