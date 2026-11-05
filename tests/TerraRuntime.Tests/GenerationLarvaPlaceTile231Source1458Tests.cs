using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationLarvaPlaceTile231Source1458Tests
{
    [Theory]
    [InlineData("occupied", true, "8cfba8b404e8ca8da52888938c6c05594fc2d4104ca18a04bc4d977cd3723a26")]
    [InlineData("success", true, "65f2957bb457662a5feb62d10f695dbe2e5d13a70f3f135d834c21527433b96d")]
    [InlineData("floor", false, "13512b5cb4d0f0a2571e7b683dc2f72fffb5a4d0fe9c2db299e18f465172fd2d")]
    [InlineData("488", false, "d6a1b1207d7ca7b1bf35467c839c6ee66394e5f79a5849716bba5ccccdeb6624")]
    public void Existing_larva_placement_matches_official_PlaceTile231(
        string arrangement, bool expectedResult, string expectedGrid)
    {
        // TerrariaServer 1.4.5.8 direct PlaceTile(300,250,231,mute:true,
        // forced:false) with generatingWorld and source hive solidity.
        var workspace = new Workspace(600, 500);
        workspace.InitializeVanillaGenerationSolidity();
        workspace.SetVanillaGenerationSolid(new TileTypeId(225), true);
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                Set(x, y, default);
        if (arrangement is "success" or "floor")
            for (int x = 299; x <= 301; x++)
                for (int y = 248; y <= 250; y++)
                    Set(x, y, default);
        if (arrangement == "floor")
            for (int x = 299; x <= 301; x++)
                Set(x, 251, default);
        if (arrangement == "488")
            Set(300, 250, new WorldTile { Type = 488, Flags = WorldTileFlags.Active });
        var random = new RandomAdapter(1458);
        var framing = new GenerationTileFraming1458(
            workspace.TileStore, random, workspace, useWorldGenerationSolidity: true);

        bool result = framing.TryPlaceLarva(300, 250);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedGrid, HashSixFields(workspace));
        Assert.Equal(906992634, random.Next());

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static string HashSixFields(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 16);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';');
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
