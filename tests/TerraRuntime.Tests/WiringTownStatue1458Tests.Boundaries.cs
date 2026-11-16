using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class WiringTownStatue1458Tests
{
    [Fact]
    public void Teleported_position_is_available_to_a_checkpoint_before_the_next_town_tick()
    {
        var f = new Fixture(persisted: true); Assert.True(f.Npcs.TryGetActive(0, out var before));
        var saved = Assert.Single(f.Town!.CaptureNpcPersistence().TownNpcs);
        Assert.True(f.Fire()); Assert.True(f.Npcs.TryGet(before.Handle, out var committed));
        var retained = Assert.Single(f.Town.CaptureNpcPersistence().TownNpcs);
        Assert.Equal(saved with { X = committed.PositionX, Y = committed.PositionY }, retained);
        Assert.NotEqual(saved.X, retained.X); Assert.NotEqual(saved.Y, retained.Y);
    }

    [Fact]
    public void Changed_resident_save_preimage_refuses_the_teleport_and_speculative_random_draw()
    {
        var f = new Fixture(persisted: true); Assert.True(f.Npcs.TryGetActive(0, out var before));
        var random = f.Random.Clone(); int reads = 0;
        typeof(WorldTileAuthority).GetField("statueTick", Access)!.SetValue(f.TilesOwner, (Func<long>)(() =>
        {
            if (++reads == 3)
            {
                var foreign = before with { PositionX = before.PositionX + 1f };
                Assert.True(f.Town!.TryUpdatePosition(before.Handle.Slot, in foreign));
            }
            return 0;
        }));
        Assert.True(f.Fire()); Assert.True(f.Npcs.TryGet(before.Handle, out var after));
        Assert.Equal(before, after); Assert.True(random.HasSameState(f.Random));
        Assert.Equal(before.PositionX + 1f, Assert.Single(f.Town!.CaptureNpcPersistence().TownNpcs).X);
    }

    [Theory]
    [InlineData("clock")] [InlineData("frame")] [InlineData("terrain")]
    [InlineData("entropy")] [InlineData("npc")]
    public void Final_clock_query_cannot_adopt_a_stale_town_teleport_or_entropy(string mutation)
    {
        var f = new Fixture(); Assert.True(f.Npcs.TryGetActive(0, out var before)); int reads = 0; var expected = f.Random.Clone();
        if (mutation == "entropy") expected.Next();
        typeof(WorldTileAuthority).GetField("statueTick", Access)!.SetValue(f.TilesOwner, (Func<long>)(() =>
        {
            if (++reads == 3)
            {
                if (mutation == "clock") return 1;
                if (mutation == "frame") { var tile = f.Tiles.Get(30, 30); tile.FrameX += 18; f.Tiles.Set(30, 30, in tile); }
                if (mutation == "terrain") { var tile = f.Tiles.Get(29, 29); tile.LiquidAmount = 255; f.Tiles.Set(29, 29, in tile); }
                if (mutation == "entropy") f.Random.Next();
                if (mutation == "npc")
                {
                    var input = new NpcStateUpdate(5, 5, 0, 0, 0, 0, 255, default, NpcSimulationState.Initial);
                    Assert.True(f.Npcs.TrySpawn(199, in input, out _));
                }
            }
            return 0;
        }));
        Assert.True(f.Fire()); Assert.Equal(3, reads);
        Assert.Equal(mutation == "npc" ? 2 : 1, f.Npcs.ActiveCount); Assert.True(expected.HasSameState(f.Random));
        if (mutation == "npc") { Assert.True(f.Npcs.TryGetActive(199, out var foreign)); Assert.Equal(5, foreign.Type); }
        Assert.True(f.Npcs.TryGet(before.Handle, out var after)); Assert.Equal(before, after);
        Assert.Equal(1, f.Circuit.RegisteredMechanismCount);
    }

    [Fact]
    public void Separate_host_random_streams_cannot_borrow_a_statue_source_capability()
    {
        var f = new Fixture(sharedRandom: false); var before = f.Random.Clone();
        Assert.True(f.Fire()); Assert.Equal(1, f.Npcs.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Partial_statue_images_cannot_register_or_teleport_a_resident(int cell)
    {
        var f = new Fixture(); int x = 30 + cell / 3, y = 30 + cell % 3; var tile = f.Tiles.Get(x, y);
        tile.FrameY++; f.Tiles.Set(x, y, in tile); var before = f.Random.Clone();
        Assert.True(f.Fire()); Assert.Equal(1, f.Npcs.ActiveCount); Assert.Equal(0, f.Circuit.RegisteredMechanismCount); Assert.True(before.HasSameState(f.Random));
    }

    private sealed class Fixture
    {
        internal readonly WorldTileStore Tiles = new(new(80, 100));
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly WorldTileAuthority TilesOwner;
        internal readonly NpcAuthority Owner;
        internal readonly WorldWiringCircuit1458 Circuit;
        internal readonly RuntimeTownNpcStateStore? Town;
        internal Fixture(bool sharedRandom = true, bool persisted = false)
        {
            if (persisted)
            {
                Town = new(new WorldNpcPersistence([], [new WorldTownNpc(22, "Oracle", 100, 100, false, 10, 20, 2, false)], []), [], Tiles.Dimensions);
                Assert.True(Town.TryReserveRuntimeSlots(Npcs)); Town.BindRuntimeNames(Npcs);
            }
            else
            {
                var resident = new NpcStateUpdate(22, 22, 100, 100, 1, -2, 255, default, NpcSimulationState.Initial);
                Assert.True(Npcs.TrySpawn(0, in resident, out _));
            }
            var state = new ServerRuntimeState(npcs: Npcs, worldTiles: Tiles, naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                worldItemSpawnRandom: new SystemWorldItemSpawnRandom(sharedRandom ? Random : new(1458)), townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458), townNpcs: Town);
            var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", Access)!.GetValue(state)!;
            TilesOwner = graph.WorldTileAuthority; Owner = graph.Npcs;
            Circuit = (WorldWiringCircuit1458)typeof(WorldTileAuthority).GetField("wiring", Access)!.GetValue(TilesOwner)!;
            for (int x = 20; x <= 30; x++) Tiles.Set(x, 30, new WorldTile { Flags = WorldTileFlags.WireRed });
            Tiles.Set(20, 30, new WorldTile { Type = 136, Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
            for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 3; dy++)
                Tiles.Set(30 + dx, 30 + dy, new WorldTile { Type = 105, FrameX = (short)(40 * 36 + dx * 18), FrameY = (short)(dy * 18), Flags = WorldTileFlags.Active | WorldTileFlags.WireRed });
        }
        internal bool Fire() => Circuit.HitSwitch(20, 30);
    }
}
