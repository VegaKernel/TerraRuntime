using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringItemStatue1458Tests
{
    [Theory]
    [InlineData("clock")]
    [InlineData("frame")]
    [InlineData("entropy")]
    [InlineData("item")]
    public void Last_clock_query_cannot_escape_statue_item_or_entropy_guards(string mutation)
    {
        var f = new StatueFixture(); int reads = 0; var expected = f.Random.Clone();
        if (mutation == "entropy") expected.Next();
        typeof(WorldTileAuthority).GetField("statueTick", Access)!.SetValue(f.Authority, (Func<long>)(() =>
        {
            if (++reads == 3)
            {
                if (mutation == "clock") return 1;
                if (mutation == "frame") { var tile = f.Tiles.Get(30, 30); tile.FrameX += 18; f.Tiles.Set(30, 30, in tile); }
                if (mutation == "entropy") f.Random.Next();
                if (mutation == "item")
                {
                    var drop = new WorldItemDropStateUpdate(100, 100, 0, 0, 1, 0, WorldItemOwnershipMode.None, 2, false, 0, 0);
                    Assert.True(f.Items.TryApplyDrop(399, in drop, out _));
                }
            }
            return 0;
        }));
        Assert.True(f.Fire()); Assert.Equal(3, reads);
        Assert.Equal(mutation == "item" ? 1 : 0, f.Items.ActiveCount);
        if (mutation == "item") { Assert.True(f.Items.TryGetActive(399, out var foreign)); Assert.Equal(2, foreign.ItemNetId); }
        Assert.True(expected.HasSameState(f.Random)); Assert.Equal(1, f.Circuit.RegisteredMechanismCount);
    }

    [Fact]
    public void A_changed_calendar_rejects_the_prepared_birth_without_refunding_the_source_cooldown()
    {
        var f = new StatueFixture(); int reads = 0; var expected = f.Random.Clone();
        typeof(WorldItemAuthority).GetField("seasonalContext", Access)!.SetValue(f.Owner,
            (Func<VanillaSeasonalItemDropContext1458>)(() => new(++reads > 1, false, false)));
        Assert.True(f.Fire()); Assert.Equal(2, reads); Assert.Equal(0, f.Items.ActiveCount);
        Assert.Equal(1, f.Circuit.RegisteredMechanismCount); Assert.True(expected.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_partial_statue_footprint_cannot_register_or_publish_an_item(int cell)
    {
        var f = new StatueFixture(); int x = 30 + cell / 3, y = 30 + cell % 3;
        var tile = f.Tiles.Get(x, y); tile.FrameY++; f.Tiles.Set(x, y, in tile);
        var before = f.Random.Clone(); Assert.True(f.Fire());
        Assert.Equal(0, f.Items.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_calendar_or_wet_statue_does_not_speculate_a_birth(bool wet)
    {
        var f = new StatueFixture(calendar: wet);
        if (wet) { var tile = f.Tiles.Get(31, 32); tile.LiquidAmount = 1; f.Tiles.Set(31, 32, in tile); }
        var before = f.Random.Clone(); Assert.True(f.Fire());
        Assert.Equal(0, f.Items.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(15, 4)]
    public void Each_wire_color_skips_all_six_cells_even_when_the_item_action_refuses(int colors, int expected)
    {
        var f = new StatueFixture(colors: colors); int calls = 0;
        var device = new WorldWiringDeviceExecutor1458(f.Tiles, fireStatue: (_, _, style) => { Assert.Equal(37, style); calls++; return false; });
        var circuit = new WorldWiringCircuit1458(f.Tiles, device);
        Assert.True(circuit.HitSwitch(20, 30)); Assert.Equal(expected, calls);
    }

    private const BindingFlags Access = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class StatueFixture
    {
        internal readonly WorldTileStore Tiles = new(new(80, 100));
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly WorldTileAuthority Authority;
        internal readonly WorldItemAuthority Owner;
        internal readonly WorldWiringCircuit1458 Circuit;
        internal StatueFixture(bool calendar = true, int colors = 1)
        {
            RuntimeTownCommerceWorldFacts1458? facts = calendar ? default(RuntimeTownCommerceWorldFacts1458) : null;
            var state = new ServerRuntimeState(worldTiles: Tiles, worldItems: Items,
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(Random), townCommerceWorldFacts: facts);
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
            Authority = graph.WorldTileAuthority; Owner = graph.WorldItems;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(Authority)!;
            WorldTileFlags wire = default;
            for (int color = 1; color <= 4; color++) if ((colors & (1 << (color - 1))) != 0) wire |= (WorldTileFlags)(1 << color);
            for (int x = 20; x <= 30; x++) Tiles.Set(x, 30, new WorldTile { Flags = wire });
            Tiles.Set(20, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | wire });
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                Tiles.Set(30 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(37 * 36 + dx * 18),
                    FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | wire });
        }
        internal bool Fire() => Circuit.HitSwitch(20, 30);
    }
}
