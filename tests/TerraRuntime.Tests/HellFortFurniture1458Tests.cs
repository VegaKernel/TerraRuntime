using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HellFortFurniture1458Tests
{
    [Theory]
    [InlineData(4200, 1000)]
    [InlineData(6400, 657)]
    [InlineData(8400, 500)]
    public void Source_inverse_width_budget_rounds_up(int width, int expected) =>
        Assert.Equal(expected, HellFortFurniture1458.AttemptCount(width));

    // Independently pinned source object atlases; do not derive expectations from the production switch.
    [Theory]
    [InlineData(14, -1, 3, 2, 702, 0)]
    [InlineData(15, 0, 1, 2, 0, 640)]
    [InlineData(18, 0, 2, 1, 504, 0)]
    [InlineData(79, -1, 4, 2, 0, 288)]
    [InlineData(87, -1, 3, 2, 810, 0)]
    [InlineData(88, -1, 3, 2, 486, 0)]
    [InlineData(89, -1, 3, 2, 540, 0)]
    [InlineData(90, -1, 4, 2, 0, 900)]
    [InlineData(93, 0, 1, 3, 0, 1242)]
    [InlineData(100, -1, 2, 2, 0, 900)]
    [InlineData(101, -1, 3, 4, 216, 0)]
    [InlineData(104, 0, 2, 5, 612, 0)]
    [InlineData(105, 0, 2, 3, 1764, 0)]
    public void Object_frames_supports_and_anchor_cleanup(ushort type, int offset, int width, int height, int frameX, int frameY)
    {
        Workspace workspace = Room(); WorldTileStore store = workspace.TileStore;
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
            At(store, 300 + offset + dx, 321 - height + dy).TileColor = 9;
        Assert.True(HellFortFurniture1458.Place(workspace, 300, 320, type));
        for (int dx = 0; dx < width; dx++)
        for (int dy = 0; dy < height; dy++)
        {
            int x = 300 + offset + dx, y = 321 - height + dy;
            WorldTile cell = store.Get(x, y);
            Assert.True(cell.IsActive); Assert.Equal(type, cell.Type);
            Assert.Equal(frameX + dx * 18, cell.FrameX); Assert.Equal(frameY + dy * 18, cell.FrameY);
            Assert.Equal(type is not (79 or 90) && x == 300 && y == 320 ? 0 : 9, cell.TileColor);
            Assert.Equal(14, cell.Wall);
        }
        WorldChest[] chests = workspace.CaptureGeneratedChests();
        if (type != 88) { Assert.Empty(chests); return; }
        WorldChest chest = Assert.Single(chests);
        Assert.Equal((299, 319), (chest.X, chest.Y)); Assert.Equal(string.Empty, chest.Name);
        Assert.Equal(40, chest.Items.Length); Assert.All(chest.Items, item => Assert.True(item.IsEmpty));
        Assert.True(GeneratedContainerFootprint.IsValid(store, chest.X, chest.Y));
    }

    [Theory]
    [InlineData(0, 14, 6, 5)]
    [InlineData(1, 18, 4, 3)]
    [InlineData(2, 105, 0, 0)]
    [InlineData(3, 101, 0, 0)]
    [InlineData(4, 15, 2, 0)]
    [InlineData(5, 79, 2, 0)]
    [InlineData(6, 87, 0, 0)]
    [InlineData(7, 88, 0, 0)]
    [InlineData(8, 89, 0, 0)]
    [InlineData(9, 104, 0, 0)]
    [InlineData(10, 90, 2, 0)]
    [InlineData(11, 93, 0, 0)]
    [InlineData(12, 100, 0, 0)]
    public void All_arrangements_follow_the_floor_to_its_midpoint_and_consume_exact_draws(int choice, ushort type, int bound, int value)
    {
        Workspace workspace = Room();
        var draws = new List<(int, int, int)> { (0, 13, choice) };
        if (bound > 0) draws.Add((0, bound, value));
        if (choice == 1) draws.Add((0, 2, 1));
        var random = new ScriptedRandom(draws.ToArray());
        Assert.True(HellFortFurniture1458.TryPlaceCandidate(workspace, 289, 295, random));
        random.AssertConsumed(); Assert.Equal(type, workspace.TileStore.Get(300, 320).Type);
        if (choice is 5 or 10) Assert.Equal(90, workspace.TileStore.Get(300, 320).FrameX);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, true)]
    [InlineData(0, 2, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 1, true)]
    public void Table_and_bench_candles_keep_source_offset_and_chair_direction(int choice, int offset, bool candle)
    {
        Workspace workspace = Room();
        var draws = new List<(int, int, int)> { (0, choice == 0 ? 6 : 4, offset) };
        if (choice == 1) draws.Add((0, 2, 0));
        var random = new ScriptedRandom(draws.ToArray());
        HellFortFurniture1458.PlaceArrangement(workspace, 300, 320, choice, random); random.AssertConsumed();
        WorldTile cell = workspace.TileStore.Get(300 + offset, 320 - (choice == 0 ? 2 : 1));
        Assert.Equal(candle, cell.IsActive);
        if (candle) { Assert.Equal(33, cell.Type); Assert.Equal(0, cell.FrameX); Assert.Equal(550, cell.FrameY); }
        Assert.Equal(18, workspace.TileStore.Get(choice == 0 ? 298 : 299, 320).FrameX);
        if (choice == 0) Assert.Equal(0, workspace.TileStore.Get(302, 320).FrameX);
    }

    [Theory]
    [InlineData(0, 5, 4)]
    [InlineData(1, 4, 3)]
    [InlineData(2, 3, 5)]
    [InlineData(3, 4, 6)]
    [InlineData(4, 3, 3)]
    [InlineData(5, 5, 3)]
    [InlineData(6, 5, 4)]
    [InlineData(7, 5, 4)]
    [InlineData(8, 5, 4)]
    [InlineData(9, 3, 5)]
    [InlineData(10, 5, 3)]
    [InlineData(11, 2, 4)]
    [InlineData(12, 3, 3)]
    public void Clearance_includes_outer_top_corner_before_extra_rng(int choice, int halfWidth, int above)
    {
        Workspace workspace = Room();
        At(workspace.TileStore, 300 + halfWidth, 320 - above).Flags |= WorldTileFlags.Active;
        var random = new ScriptedRandom((0, 13, choice));
        Assert.False(HellFortFurniture1458.TryPlaceCandidate(workspace, 289, 295, random));
        random.AssertConsumed(); Assert.False(workspace.TileStore.Get(300, 320).IsActive);
    }

    [Theory]
    [InlineData(10, false)] // span difference8 < 5*1.75 (table)
    [InlineData(11, true)] // span difference9, not a count-of-cells comparison
    public void Width_gate_uses_source_span_difference(int count, bool admitted)
    {
        Workspace workspace = Room();
        for (int x = 280; x <= 320; x++) At(workspace.TileStore, x, 321).Flags = 0;
        for (int x = 295; x < 295 + count - 1; x++) At(workspace.TileStore, x, 321).Flags = WorldTileFlags.Active;
        var random = admitted ? new ScriptedRandom((0, 13, 0), (0, 6, 5)) : new ScriptedRandom((0, 13, 0));
        Assert.Equal(admitted, HellFortFurniture1458.TryPlaceCandidate(workspace, 297, 295, random)); random.AssertConsumed();
    }

    [Theory]
    [InlineData(93, false)]
    [InlineData(14, true)]
    [InlineData(79, true)]
    public void Lamp_is_dry_only_but_ordinary_furniture_preserves_liquid(ushort type, bool placed)
    {
        Workspace workspace = Room();
        At(workspace.TileStore, 300, 320).LiquidAmount = 80;
        Assert.Equal(placed, HellFortFurniture1458.Place(workspace, 300, 320, type));
        Assert.Equal(80, workspace.TileStore.Get(300, 320).LiquidAmount);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Dresser_validation_checks_all_six_cells_and_does_not_accept_mixed_styles(int cell)
    {
        Workspace workspace = Room();
        Assert.True(HellFortFurniture1458.Place(workspace, 300, 320, 88));
        At(workspace.TileStore, 299 + cell % 3, 319 + cell / 3).FrameX += 54;
        Assert.False(GeneratedContainerFootprint.IsValid(workspace.TileStore, 299, 319));
    }

    [Fact]
    public void Failed_dresser_placement_creates_neither_partial_tiles_nor_phantom_metadata()
    {
        Workspace workspace = Room();
        At(workspace.TileStore, 301, 321).Shape = 1;
        Assert.False(HellFortFurniture1458.Place(workspace, 300, 320, 88));
        Assert.Empty(workspace.CaptureGeneratedChests());
        for (int x = 299; x <= 301; x++) Assert.False(workspace.TileStore.Get(x, 320).IsActive);
    }

    [Fact]
    public void Floor_search_passes_through_platforms_instead_of_treating_them_as_solid_floor()
    {
        Workspace workspace = Room();
        At(workspace.TileStore, 300, 300) = new WorldTile { Type = 19, FrameY = 234, Flags = WorldTileFlags.Active };
        var random = new ScriptedRandom((0, 13, 2));
        Assert.True(HellFortFurniture1458.TryPlaceCandidate(workspace, 300, 295, random));
        random.AssertConsumed(); Assert.Equal(105, workspace.TileStore.Get(300, 320).Type);
        Assert.Equal(19, workspace.TileStore.Get(300, 300).Type);
    }

    [Fact]
    public void Registry_rejection_rolls_back_dresser_footprint_without_allocating_a_second_slot()
    {
        Workspace workspace = Room();
        Assert.True(HellFortFurniture1458.Place(workspace, 300, 320, 88));
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 3; dx++) At(workspace.TileStore, 299 + dx, 319 + dy).Flags &= ~WorldTileFlags.Active;
        Assert.False(HellFortFurniture1458.Place(workspace, 300, 320, 88));
        Assert.Single(workspace.CaptureGeneratedChests());
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 3; dx++) Assert.False(workspace.TileStore.Get(299 + dx, 319 + dy).IsActive);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(467)]
    public void Shared_container_validation_preserves_ordinary_chests_and_rejects_cross_column_style_mismatch(ushort type)
    {
        Workspace workspace = Room();
        for (int dy = 0; dy < 2; dy++)
        for (int dx = 0; dx < 2; dx++) At(workspace.TileStore, 299 + dx, 319 + dy) = new WorldTile
            { Type = type, Flags = WorldTileFlags.Active, FrameX = (short)(36 + dx * 18), FrameY = (short)(dy * 18) };
        Assert.True(GeneratedContainerFootprint.IsValid(workspace.TileStore, 299, 319));
        At(workspace.TileStore, 300, 319).FrameX += 36; At(workspace.TileStore, 300, 320).FrameX += 36;
        Assert.False(GeneratedContainerFootprint.IsValid(workspace.TileStore, 299, 319));
    }

    private static Workspace Room()
    {
        var workspace = new Workspace(600, 400);
        for (int x = 270; x <= 330; x++)
        for (int y = 270; y <= 321; y++) At(workspace.TileStore, x, y).Wall = 14;
        for (int x = 280; x <= 320; x++) At(workspace.TileStore, x, 321) = new WorldTile { Type = 75, Wall = 14, Flags = WorldTileFlags.Active };
        return workspace;
    }
    private static ref WorldTile At(WorldTileStore store, int x, int y) => ref store.Tiles[store.GetUncheckedIndex(x, y)];
    private sealed class ScriptedRandom(params (int Min, int Max, int Value)[] script) : IWorldGenerationVanillaRandom
    {
        private int calls;
        public void AssertConsumed() => Assert.Equal(script.Length, calls);
        public int Next(int minValue, int maxValue)
        {
            Assert.True(calls < script.Length, "Unexpected furniture RNG draw");
            var draw = script[calls++]; Assert.Equal((draw.Min, draw.Max), (minValue, maxValue)); return draw.Value;
        }
        public int Next(int maxValue) => Next(0, maxValue);
        public int Next() => throw new NotSupportedException();
        public double NextDouble() => throw new NotSupportedException();
        public void NextBytes(byte[] buffer) => throw new NotSupportedException();
    }
}
