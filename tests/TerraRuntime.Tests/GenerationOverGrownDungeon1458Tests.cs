using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationOverGrownDungeon1458Tests
{
    [Theory]
    [InlineData(1458, 155, 298, "7ac35c3fa30f5ef0cc0bce87755c57529604520b3c9b22109a8c9988e78d8405", 1354577429)]
    [InlineData(42, 217, 106, "7d3f611f9301558cc2ea34114e66a6b6b232f9b9dfa8297e8ee22903c7fa98a1", 938179412)]
    public void Actual_original_single_dungeon_wall_hit_matches_full_grid_and_rng(
        int seed, int expectedX, int expectedY, string expectedHash, int expectedNext)
    {
        // Direct WorldGen.OverGrownDungeon, official TerrariaServer 1.4.5.8.
        // This is the first sampled dungeon-wall hit; the exposed brick converts
        // while its active neighbours block GrowTree and vine PlaceTile.
        Workspace workspace = Fixture(seed, out int x, out int y);
        Assert.Equal((expectedX, expectedY), (x, y));
        var random = new RandomAdapter(seed);
        Assert.NotEqual(expectedHash, Hash(workspace));

        GenerationOverGrownDungeon1458.ApplySingleEnclosedHit(
            workspace, random, -1, 140, 200, TestContext.Current.CancellationToken);

        Assert.Equal((ushort)60, workspace.TileStore.Get(x, y).Type);
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(1458, "4ff9ae9b7b206f330a28b53f7c9e75375949e5e8b5dd0bf1a47dea740633fc0c", 497869663, 5)]
    [InlineData(42, "f80a28cd76872f96f2debce5886c54ecb6ce742f30be30d8d35f25d7266facaf", 442563727, 3)]
    public void Actual_original_dungeon_vine_continuation_matches_full_grid_and_rng(
        int seed, string expectedHash, int expectedNext, int expectedVines)
    {
        Workspace workspace = Fixture(seed, out int x, out int y, vine: true);
        var random = new RandomAdapter(seed);
        Assert.NotEqual(expectedHash, Hash(workspace));

        GenerationOverGrownDungeon1458.ApplySingleEnclosedHit(
            workspace, random, -1, 140, 200, TestContext.Current.CancellationToken);

        Assert.Equal((ushort)60, workspace.TileStore.Get(x, y).Type);
        Assert.Equal(expectedVines, Enumerable.Range(1, 6)
            .Count(offset => workspace.TileStore.Get(x, y + offset).IsActive &&
                             workspace.TileStore.Get(x, y + offset).Type == 62));
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(1458, "bdf63f89c7e200ec8646b1c25137526e7e9f4fdcb388e68a89d41e7f71ef2cd7", 1427009610, 90)]
    [InlineData(42, "452e5343d711b55cdd982dc6694c12a87a10d95da6c0df9ba47ad3a353aebdad", 492020783, 54)]
    public void Actual_original_failed_tree_then_plant_placement_matches_full_grid_and_rng(
        int seed, string expectedHash, int expectedNext, short expectedFrame)
    {
        Workspace workspace = Fixture(seed, out int x, out int y, plant: true);
        var random = new RandomAdapter(seed);
        Assert.NotEqual(expectedHash, Hash(workspace));

        GenerationOverGrownDungeon1458.ApplySingleEnclosedHit(
            workspace, random, -1, 140, 200, TestContext.Current.CancellationToken);

        Assert.Equal((ushort)60, workspace.TileStore.Get(x, y).Type);
        WorldTile plant = workspace.TileStore.Get(x, y - 1);
        Assert.Equal((ushort)61, plant.Type);
        Assert.Equal(expectedFrame, plant.FrameX);
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Theory]
    [InlineData(1458, "ee04cd31a220b8746cb68a9671279e2ae9e8068a6721b4b7088778d65d2fb7ce", 557660369)]
    [InlineData(42, "1a753b9a8f2c4998648d33fd2d1fdb6fab78fe88e53cfc9814955554fc07aed8", 2047937641)]
    public void Actual_original_successful_dungeon_tree_matches_full_grid_and_rng(
        int seed, string expectedHash, int expectedNext)
    {
        Workspace workspace = Fixture(seed, out int x, out int y, tree: true);
        workspace.InitializeVanillaGenerationSolidity();
        var random = new RandomAdapter(seed);
        Assert.NotEqual(expectedHash, Hash(workspace));

        GenerationOverGrownDungeon1458.ApplySingleEnclosedHit(
            workspace, random, -1, 140, 200, TestContext.Current.CancellationToken);

        Assert.Equal((ushort)60, workspace.TileStore.Get(x, y).Type);
        Assert.Equal((ushort)5, workspace.TileStore.Get(x, y - 1).Type);
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void Unsupported_second_dungeon_wall_is_rejected_before_mutation_or_rng()
    {
        Workspace workspace = Fixture(1458, out int x, out int y);
        WorldTile extra = new() { Type = 41, Wall = 7, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(x + 20, y, in extra);
        string before = Hash(workspace);
        var random = new RandomAdapter(1458);
        Assert.Throws<NotSupportedException>(() => GenerationOverGrownDungeon1458.ApplySingleEnclosedHit(
            workspace, random, -1, 140, 200, TestContext.Current.CancellationToken));
        Assert.Equal(before, Hash(workspace));
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
    }

    private static Workspace Fixture(int seed, out int hitX, out int hitY,
        bool vine = false, bool plant = false, bool tree = false)
    {
        var workspace = new Workspace(600, 500);
        var preview = new VanillaUnifiedRandom1458(seed);
        hitX = preview.Next(50, 300);
        hitY = preview.Next(50, 450);
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                workspace.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                workspace.TileStore.Set(x, y, default);
        for (int x = hitX - 2; x <= hitX + 2; x++)
            for (int y = hitY - 2; y <= hitY + 2; y++)
                workspace.TileStore.Set(x, y, in stone);
        WorldTile brick = new() { Type = 41, Wall = 7, Flags = WorldTileFlags.Active };
        workspace.TileStore.Set(hitX, hitY, in brick);
        workspace.TileStore.Set(hitX - 1, hitY - 1, default);
        if (vine)
            for (int y = hitY + 1; y <= hitY + 6; y++)
                workspace.TileStore.Set(hitX, y, default);
        if (plant)
            workspace.TileStore.Set(hitX, hitY - 1, default);
        if (tree)
        {
            for (int x = hitX - 2; x <= hitX + 2; x++)
                for (int y = hitY - 30; y < hitY; y++)
                    workspace.TileStore.Set(x, y, default);
            WorldTile grass = new() { Type = 60, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(hitX - 1, hitY, in grass);
            workspace.TileStore.Set(hitX + 1, hitY, in grass);
        }
        return workspace;
    }

    private static string Hash(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';')
                    .Append((byte)tile.LiquidKind).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(';');
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
