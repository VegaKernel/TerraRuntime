using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class UnforcedWorldDoorAndTallGate1458Tests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Actual_gate94_row_geometry_handles_open_close_and_destroy(int touchedRow)
    {
        var tiles = new WorldTileStore(new WorldDimensions(50, 50));
        int[] frames = [0, 20, 38, 56, 74];
        for (int row = 0; row < 5; row++) tiles.Set(20, 20 + row, new WorldTile { Type = 388,
            FrameX = 36, FrameY = (short)(94 + frames[row]), TileColor = (byte)(row + 1), Flags = WorldTileFlags.Active | WorldTileFlags.WireGreen });
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Probe(true));
        var intent = new VanillaGroundFighterDoorOpeningIntent(20, 20 + touchedRow, 1, VanillaTileIds.TallGateClosed);
        Assert.True(service.TryOpen(in intent, out _));
        for (int row = 0; row < 5; row++) { Assert.Equal((ushort)389, tiles.Get(20, 20 + row).Type); Assert.Equal((short)(94 + frames[row]), tiles.Get(20, 20 + row).FrameY); }
        Assert.True(service.TryShiftTallGate(20, 20 + touchedRow, closing: true, forced: false, out _));
        intent = intent with { Operation = VanillaGroundFighterDoorOperation.Destroy };
        Assert.True(service.TryOpen(in intent, out var mutation)); Assert.Equal(5, mutation.ChangedTiles);
        for (int row = 0; row < 5; row++) Assert.False(tiles.Get(20, 20 + row).IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unforced_gate_checks_all_rows_before_mutation_and_forced_network_path_skips_actors(bool closing)
    {
        var tiles = new WorldTileStore(new WorldDimensions(50, 50)); int[] frames = [0, 20, 38, 56, 74];
        for (int row = 0; row < 5; row++) tiles.Set(20, 20 + row, new WorldTile { Type = closing ? (ushort)389 : (ushort)388,
            FrameY = (short)frames[row], Flags = WorldTileFlags.Active });
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles, new Probe(false));
        Assert.False(service.TryShiftTallGate(20, 22, closing, forced: false, out _));
        Assert.Equal(closing ? (ushort)389 : (ushort)388, tiles.Get(20, 20).Type);
        Assert.True(service.TryShiftTallGate(20, 22, closing, out _));
        Assert.Equal(closing ? (ushort)388 : (ushort)389, tiles.Get(20, 20).Type);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Unforced_close_checks_retained_column_only_and_rejection_consumes_no_frame_rng(bool retainedBlocked)
    {
        var tiles = new WorldTileStore(new WorldDimensions(50, 50));
        for (int row = 0; row < 3; row++)
        for (int col = 0; col < 2; col++) tiles.Set(20 + col, 20 + row, new WorldTile { Type = 11,
            FrameX = (short)(col * 18), FrameY = (short)(row * 18), Flags = WorldTileFlags.Active });
        var rng = new Random(); var occupancy = new ColumnProbe(retainedBlocked ? 20 : 21);
        var service = new VanillaWorldGroundFighterDoorOpeningService(tiles, occupancy, rng);
        Assert.Equal(!retainedBlocked, service.TryCloseDoor(20, 21, forced: false, out _));
        Assert.Equal(retainedBlocked ? 0 : 3, rng.Calls);
        Assert.All(occupancy.X, x => Assert.Equal(20, x));
        if (retainedBlocked) Assert.Equal((ushort)11, tiles.Get(20, 20).Type);
    }
    private sealed class Probe(bool free) : IVanillaTallGateOccupancyProbe { public bool IsActorFree(int x, int y) => free; }
    private sealed class ColumnProbe(int blocked) : IVanillaTallGateOccupancyProbe
    { public List<int> X { get; } = []; public bool IsActorFree(int x, int y) { X.Add(x); return x != blocked; } }
    private sealed class Random : IVanillaDoorCloseRandom1458
    { public int Calls { get; private set; } public int NextClosedDoorFrameColumn() { Calls++; return 1; } }
}
