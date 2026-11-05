using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationOverGrownDungeonTerrain1458Tests
{
    [Theory]
    [InlineData(1458, 155, 298, 239, 238, 93, 409, "d69778b839a14d3eb3ea24e1465d5cfd64761120dba10639762615869fa80e44", 573940008)]
    [InlineData(42, 217, 106, 180, 117, 231, 255, "ea709629df61bf9cbe2f9f01ade59182aae9f45f4fea72270f18b9c57fc00878", 442563727)]
    public void Actual_original_multiple_dungeon_wall_hits_match_full_grid_and_rng(
        int seed, int firstX, int firstY, int secondX, int secondY, int thirdX, int thirdY,
        string expectedHash, int expectedNext)
    {
        // Direct official WorldGen.OverGrownDungeon, TerrariaServer 1.4.5.8.
        // Three 11x11 dungeon wall/brick pockets, sampled at iterations 0/1/2.
        Workspace workspace = Fixture(seed, out (int X, int Y) first,
            out (int X, int Y) second, out (int X, int Y) third);
        Assert.Equal((firstX, firstY), first);
        Assert.Equal((secondX, secondY), second);
        Assert.Equal((thirdX, thirdY), third);
        var random = new RandomAdapter(seed);
        Assert.NotEqual(expectedHash, Hash(workspace));

        GenerationOverGrownDungeonTerrain1458.Apply(
            workspace, random, -1, TestContext.Current.CancellationToken);

        Assert.Equal((ushort)60, workspace.TileStore.Get(first.X, first.Y).Type);
        Assert.Equal((ushort)60, workspace.TileStore.Get(second.X, second.Y).Type);
        Assert.Equal((ushort)60, workspace.TileStore.Get(third.X, third.Y).Type);
        Assert.Equal((ushort)41, workspace.TileStore.Get(first.X + 2, first.Y + 2).Type);
        Assert.Equal((ushort)43, workspace.TileStore.Get(second.X + 2, second.Y + 2).Type);
        Assert.Equal((ushort)44, workspace.TileStore.Get(third.X + 2, third.Y + 2).Type);
        Assert.Equal(expectedHash, Hash(workspace));
        Assert.Equal(expectedNext, random.Next());
    }

    [Fact]
    public void An_exposed_brick_needing_vine_placement_is_rejected_before_rng_or_mutation()
    {
        Workspace workspace = Fixture(1458, out (int X, int Y) first, out _, out _);
        WorldTile unsupported = default;
        workspace.TileStore.Set(first.X, first.Y + 1, in unsupported);
        string before = Hash(workspace);
        var random = new RandomAdapter(1458);
        Assert.Throws<NotSupportedException>(() => GenerationOverGrownDungeonTerrain1458.Apply(
            workspace, random, -1, TestContext.Current.CancellationToken));
        Assert.Equal(before, Hash(workspace));
        Assert.Equal(new RandomAdapter(1458).Next(), random.Next());
    }

    private static Workspace Fixture(int seed, out (int X, int Y) first,
        out (int X, int Y) second, out (int X, int Y) third)
    {
        var workspace = new Workspace(600, 500);
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                workspace.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                workspace.TileStore.Set(x, y, default);

        var preview = new VanillaUnifiedRandom1458(seed);
        first = (preview.Next(50, 300), preview.Next(50, 450));
        _ = preview.Next(5, 16); // The first hit consumes its radius before sample two.
        second = (preview.Next(50, 300), preview.Next(50, 450));
        _ = preview.Next(5, 16);
        third = (preview.Next(50, 300), preview.Next(50, 450));
        PlacePocket(first, 7, 41);
        PlacePocket(second, 8, 43);
        PlacePocket(third, 9, 44);
        return workspace;

        void PlacePocket((int X, int Y) center, ushort wall, ushort brickType)
        {
            WorldTile walledStone = new() { Type = 1, Wall = wall, Flags = WorldTileFlags.Active };
            for (int x = center.X - 5; x <= center.X + 5; x++)
                for (int y = center.Y - 5; y <= center.Y + 5; y++)
                    workspace.TileStore.Set(x, y, in walledStone);
            WorldTile brick = new() { Type = brickType, Wall = wall, Flags = WorldTileFlags.Active };
            workspace.TileStore.Set(center.X, center.Y, in brick);
            workspace.TileStore.Set(center.X + 2, center.Y + 2, in brick);
            WorldTile gap = new() { Wall = wall };
            workspace.TileStore.Set(center.X - 1, center.Y - 1, in gap);
        }
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
