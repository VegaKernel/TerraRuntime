using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;
using System.Security.Cryptography;
using System.Text;

namespace TerraRuntime.Tests;

public sealed class GenerationFinishNoTraps1458Tests
{
    [Theory]
    [InlineData(1458, false, "1b5c202b52adc0afc16e4a2bbe2ae5e6b93063c636d62adee3975b1fb9f9e24b", 581743634)]
    [InlineData(15, true, "71018d173e04f9098aca0eb5eaba685da0fa71977367fb71a0f5cebc16e188f6", 996433393)]
    public void Detached_official_finish_no_traps_matches_full_grid_chest_items_and_shared_rng(
        int seed, bool withChest, string expectedGrid, int expectedNext)
    {
        // Actual TerrariaServer 1.4.5.8 WorldGen.FinishNoTraps, invoked on a 600x500
        // stone/chamber grid with five 2x2 Life Crystals and an unwired trapped chest.
        // The ignored local reflection probe invokes the original private helper.
        var workspace = CreateOfficialFixture(withChest);
        var random = new RandomAdapter(seed);

        Assert.NotEqual(expectedGrid, HashSixFields(workspace));
        FinalPass1458.FinishNoTraps(workspace, random, TestContext.Current.CancellationToken);

        Assert.Equal(expectedGrid, HashSixFields(workspace));
        Assert.Equal(expectedNext, random.Next());
        Assert.Equal((ushort)21, workspace.TileStore.Get(40, 40).Type);
        if (withChest)
        {
            WorldChest chest = Assert.Single(workspace.CaptureGeneratedChests());
            Assert.Equal(1, chest.Items[0].ItemType);
            Assert.Equal(2, chest.Items[1].ItemType);
            Assert.Equal(new WorldChestItem(1, 5346, 0), chest.Items[2]);
            Assert.All(chest.Items.Skip(3), item => Assert.True(item.IsEmpty));
        }

    }

    [Fact]
    public void Omitting_finish_no_traps_fails_the_official_grid_rng_and_chest_inventory()
    {
        // Test-only omission of the production helper on the exact original seed-15 input.
        // Each independent oracle projection must detect the missing call.
        Workspace workspace = CreateOfficialFixture(withChest: true);
        var random = new RandomAdapter(15);
        Assert.NotEqual("71018d173e04f9098aca0eb5eaba685da0fa71977367fb71a0f5cebc16e188f6",
            HashSixFields(workspace));
        Assert.NotEqual(996433393, random.Next());
        WorldChest chest = Assert.Single(workspace.CaptureGeneratedChests());
        Assert.True(chest.Items[2].IsEmpty);
    }

    [Fact]
    public void Empty_no_traps_tail_consumes_one_draw_per_interior_cell()
    {
        var workspace = CreateWorkspace(120, 120);
        var actual = new RandomAdapter(1458);
        var expected = new RandomAdapter(1458);
        for (int i = 0; i < 20 * 20; i++)
            expected.Next(5);

        FinalPass1458.FinishNoTraps(workspace, actual, TestContext.Current.CancellationToken);

        Assert.Equal(expected.Next(), actual.Next());
        Assert.True(workspace.IsVanillaGenerationSolid(new TileTypeId(138)));
        Assert.True(workspace.IsVanillaGenerationSolid(new TileTypeId(484)));
    }

    [Theory]
    [InlineData(WorldTileFlags.None)]
    [InlineData(WorldTileFlags.WireBlue)]
    [InlineData(WorldTileFlags.WireGreen)]
    public void Trapped_chest_without_red_wire_becomes_basic_chest_without_losing_its_record(
        WorldTileFlags otherWire)
    {
        var workspace = CreateWorkspace(120, 120);
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile cell = new()
                {
                    Type = 467,
                    FrameX = (short)(144 + dx * 18),
                    FrameY = (short)(dy * 18),
                    Flags = WorldTileFlags.Active | otherWire
                };
                workspace.TileStore.Set(40 + dx, 40 + dy, in cell);
            }
        WorldGenerationChestItem[] contents =
        [
            new(1, new ItemTypeId(1)),
            new(1, new ItemTypeId(2))
        ];
        Assert.True(workspace.TryAddChest(40, 40, string.Empty, contents));

        FinalPass1458.FinishNoTraps(workspace, new RandomAdapter(42), TestContext.Current.CancellationToken);

        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
            {
                WorldTile cell = workspace.TileStore.Get(40 + dx, 40 + dy);
                Assert.Equal((ushort)21, cell.Type);
                Assert.Equal((short)(36 + dx * 18), cell.FrameX);
                Assert.Equal((short)(dy * 18), cell.FrameY);
                Assert.Equal(otherWire, cell.Flags & otherWire);
            }
        WorldChest chest = Assert.Single(workspace.CaptureGeneratedChests());
        Assert.Equal(40, chest.X);
        Assert.Equal(40, chest.Y);
        Assert.Equal(1, chest.Items[0].ItemType);
        Assert.Equal(2, chest.Items[1].ItemType);
    }

    [Fact]
    public void Chest_inventory_scan_stops_at_first_removed_source_slot()
    {
        var workspace = CreateWorkspace(120, 120);
        foreach (int left in new[] { 40, 44 })
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                {
                    WorldTile tile = new()
                    {
                        Type = 467,
                        FrameX = (short)(144 + 18 * dx),
                        FrameY = (short)(18 * dy),
                        Flags = WorldTileFlags.Active
                    };
                    workspace.TileStore.Set(left + dx, 40 + dy, in tile);
                }
        Assert.True(workspace.TryAddChest(40, 40, string.Empty, []));
        WorldGenerationChestItem[] items =
        [
            new(1, new ItemTypeId(1)),
            new(1, new ItemTypeId(2))
        ];
        Assert.True(workspace.TryAddChest(44, 40, string.Empty, items));
        Assert.True(workspace.TryRemoveEmptyGeneratedChest(40, 40));
        Assert.Equal((short)1, Assert.Single(workspace.CaptureGeneratedChests()).SlotId);
        var actual = new RandomAdapter(15);
        var expected = new RandomAdapter(15);
        for (int i = 0; i < 20 * 20; i++)
            expected.Next(5);

        FinalPass1458.FinishNoTraps(workspace, actual, TestContext.Current.CancellationToken);

        Assert.Equal(expected.Next(), actual.Next());
        WorldChest retained = Assert.Single(workspace.CaptureGeneratedChests());
        Assert.True(retained.Items[2].IsEmpty);
    }

    private static Workspace CreateWorkspace(int width, int height)
    {
        var workspace = new Workspace(width, height);
        workspace.InitializeVanillaGenerationSolidity();
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NoTraps, VanillaSecretWorldSeed1458.None));
        return workspace;
    }

    private static Workspace CreateOfficialFixture(bool withChest)
    {
        var workspace = CreateWorkspace(600, 500);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        // The established official probe's base terrain contains this empty chamber.
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                Set(x, y, default);
        foreach (int crystalX in new[] { 60, 62, 64, 66, 68 })
            for (int dx = 0; dx < 2; dx++)
                for (int dy = 0; dy < 2; dy++)
                    Set(crystalX + dx, 60 + dy, new WorldTile { Type = 12, Flags = WorldTileFlags.Active });
        for (int dx = 0; dx < 2; dx++)
            for (int dy = 0; dy < 2; dy++)
                Set(40 + dx, 40 + dy, new WorldTile
                {
                    Type = 467,
                    FrameX = (short)(144 + 18 * dx),
                    FrameY = (short)(18 * dy),
                    Flags = WorldTileFlags.Active
                });
        if (withChest)
        {
            WorldGenerationChestItem[] items =
            [
                new(1, new ItemTypeId(1)),
                new(1, new ItemTypeId(2))
            ];
            Assert.True(workspace.TryAddChest(40, 40, string.Empty, items));
        }
        return workspace;

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static string HashSixFields(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 16);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile cell = workspace.TileStore.Get(x, y);
                snapshot.Append(cell.IsActive ? '1' : '0').Append(',')
                    .Append(cell.Type).Append(',').Append(cell.Wall).Append(',')
                    .Append(cell.FrameX).Append(',').Append(cell.FrameY).Append(',')
                    .Append(cell.LiquidAmount).Append(';');
            }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] buffer) => random.NextBytes(buffer);
    }
}
