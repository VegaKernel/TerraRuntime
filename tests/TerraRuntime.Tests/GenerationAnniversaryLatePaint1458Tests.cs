using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationAnniversaryLatePaint1458Tests
{
    [Theory]
    [InlineData(false, "207e673df4478be3949379923a7c0fc4644e16994ab2435d53b0a161388ea86c")]
    [InlineData(true, "22d4478eb42bca8af8b099e2acb3d4ee6419e36233c9afb4795dd39a6186b27c")]
    public void Late_anniversary_tree_and_mushroom_paint_matches_original_helper(bool remix, string sourceHash)
    {
        // Direct TerrariaServer 1.4.5.8 FinishTenthAnniversaryWorldPaintTreesAndMushrooms.
        // Both source runs start with this exact x-major stone grid and cleared camera.
        var workspace = new Workspace(600, 500);
        Assert.True(workspace.TrySetLayers(200, 300));
        workspace.SetVanillaSeedProfile(new(
            VanillaSpecialWorldSeed1458.CelebrationMk10 |
                (remix ? VanillaSpecialWorldSeed1458.Remix : VanillaSpecialWorldSeed1458.None),
            VanillaSecretWorldSeed1458.None));
        WorldTile stone = new() { Type = 1, Flags = WorldTileFlags.Active };
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                workspace.TileStore.Set(x, y, in stone);
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                workspace.TileStore.Set(x, y, default);

        Put(50, 50, 5); Put(51, 250, 5); Put(52, 251, 323);
        Put(53, 252, 596); Put(54, 253, 616);
        Put(60, 60, 70); Put(61, 260, 578); Put(62, 260, 190, wall: 80);
        Put(63, 260, 519, wall: 74, frameY: 90); Put(64, 260, 71);
        Put(65, 260, 528); Put(66, 260, 1, wall: 80); Put(67, 260, 1, wall: 74);
        Put(80, 50, 70); Put(81, 51, 578); Put(82, 250, 190);

        var random = new RandomAdapter(1458);
        Assert.NotEqual(sourceHash, HashGrid(workspace)); // Skipping the helper is a detectable mutation.
        FinalPass1458.PaintAnniversaryTreesAndMushrooms(
            workspace, random, TestContext.Current.CancellationToken);
        Assert.Equal(sourceHash, HashGrid(workspace));
        Assert.Equal(1507290721, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, short frameY = 0)
        {
            WorldTile tile = workspace.TileStore.Get(x, y);
            tile.Type = type;
            tile.Wall = wall;
            tile.FrameY = frameY;
            workspace.TileStore.Set(x, y, in tile);
        }
    }

    private static string HashGrid(Workspace workspace)
    {
        var projection = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 24);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                projection.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(';');
            }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projection.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Next() => source.Next();
        public int Next(int max) => source.Next(max);
        public int Next(int min, int max) => source.Next(min, max);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] bytes) => source.NextBytes(bytes);
    }
}
