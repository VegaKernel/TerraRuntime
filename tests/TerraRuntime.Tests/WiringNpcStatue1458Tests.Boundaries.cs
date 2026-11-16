using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringNpcStatue1458Tests
{
    [Theory]
    [InlineData("clock")] [InlineData("frame")] [InlineData("terrain")]
    [InlineData("entropy")] [InlineData("npc")] [InlineData("wet-source")]
    public void Final_clock_query_cannot_adopt_a_stale_statue_birth_or_entropy(string mutation)
    {
        var f = new Fixture(); int reads = 0; var expected = f.Random.Clone();
        if (mutation == "entropy") expected.Next();
        typeof(WorldTileAuthority).GetField("statueTick", Access)!.SetValue(f.TilesOwner, (Func<long>)(() =>
        {
            if (++reads == 3)
            {
                if (mutation == "clock") return 1;
                if (mutation == "frame") { var tile = f.Tiles.Get(30, 30); tile.FrameX += 18; f.Tiles.Set(30, 30, in tile); }
                if (mutation == "terrain") { var tile = f.Tiles.Get(29, 29); tile.LiquidAmount = 255; f.Tiles.Set(29, 29, in tile); }
                if (mutation == "entropy") f.Random.Next();
                if (mutation == "wet-source") f.Npcs.SetVanillaSpawnWetCollisionSource((_, _, _, _) => true);
                if (mutation == "npc")
                {
                    var input = new NpcStateUpdate(5, 5, 0, 0, 0, 0, 255, default, NpcSimulationState.Initial);
                    Assert.True(f.Npcs.TrySpawn(199, in input, out _));
                }
            }
            return 0;
        }));
        Assert.True(f.Fire()); Assert.Equal(3, reads);
        Assert.Equal(mutation == "npc" ? 1 : 0, f.Npcs.ActiveCount); Assert.True(expected.HasSameState(f.Random));
        if (mutation == "npc") { Assert.True(f.Npcs.TryGetActive(199, out var foreign)); Assert.Equal(5, foreign.Type); }
        Assert.Equal(1, f.Circuit.RegisteredMechanismCount);
    }

    [Fact]
    public void Separate_host_random_streams_cannot_borrow_a_statue_source_capability()
    {
        var f = new Fixture(sharedRandom: false); var before = f.Random.Clone();
        Assert.True(f.Fire()); Assert.Equal(0, f.Npcs.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Partial_statue_images_cannot_register_or_allocate_a_creature(int cell)
    {
        var f = new Fixture(); int x = 30 + cell / 3, y = 30 + cell % 3; var tile = f.Tiles.Get(x, y);
        tile.FrameY++; f.Tiles.Set(x, y, in tile); var before = f.Random.Clone();
        Assert.True(f.Fire()); Assert.Equal(0, f.Npcs.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    [Fact]
    public void Stateless_updates_preserve_zero_statue_cost_in_the_existing_natural_spawn_census()
    {
        var f = new Fixture(); Assert.True(f.Fire()); Assert.True(f.Npcs.TryGetActive(0, out var born));
        var update = new NpcStateUpdate(born.Type, born.NetId, born.PositionX, born.PositionY, 0, 0, born.Target, born.Ai,
            born.Simulation with { SpawnSlotCost = null });
        Assert.True(f.Npcs.TryUpdate(born.Handle, in update, out var changed)); Assert.Equal(0f, changed.Simulation.SpawnSlotCost);
        Assert.Equal(0f, (float)typeof(NpcAuthority).GetMethod("GetNaturalSpawnSlots", Access)!.Invoke(f.Owner, [changed])!);
    }

    private sealed class Fixture
    {
        internal readonly WorldTileStore Tiles = new(new(80, 100));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly WorldTileAuthority TilesOwner;
        internal readonly NpcAuthority Owner;
        internal readonly WorldWiringCircuit1458 Circuit;
        internal Fixture(bool sharedRandom = true)
        {
            var state = new ServerRuntimeState(npcs: Npcs, worldTiles: Tiles, naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(sharedRandom ? Random : new(1458)), townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
            TilesOwner = graph.WorldTileAuthority; Owner = graph.Npcs;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(TilesOwner)!;
            for (int x = 20; x <= 30; x++) Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
            Tiles.Set(20, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                Tiles.Set(30 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(4 * 36 + dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        }
        internal bool Fire() => Circuit.HitSwitch(20, 30);
    }
}
