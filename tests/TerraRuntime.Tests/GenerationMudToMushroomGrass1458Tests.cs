using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationMudToMushroomGrass1458Tests
{
    [Theory]
    [InlineData(70, 1458, "open", "bf1361acac9e4778f57ab7495a2ec6d8e51c159d82ea63c76e739684dc80b0b8", 906992634)]
    [InlineData(70, 42, "open", "bf1361acac9e4778f57ab7495a2ec6d8e51c159d82ea63c76e739684dc80b0b8", 1434747710)]
    [InlineData(60, 1458, "open", "2a8c90940dbf6cc36bb6ea7545afdbf84110911369d35e5707bbcf72ccb61ad9", 906992634)]
    [InlineData(60, 42, "open", "2a8c90940dbf6cc36bb6ea7545afdbf84110911369d35e5707bbcf72ccb61ad9", 1434747710)]
    [InlineData(70, 1458, "enclosed", "cc2934df4dfb14792e406cb1a7f2234b54ec6e29f08fe14839d00da0684e85dc", 906992634)]
    [InlineData(70, 1458, "lava", "addff2993bf74cc5adb7e76e8d5fa4b8cc97b60b765e0c4176dc8869f4ce5c06", 906992634)]
    public void Mud_grass_spread_matches_actual_original_grid_and_rng(
        int target, int seed, string arrangement, string originalHash, int expectedNext)
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.SpreadGrass(300,250,59,target,true),
        // 600x500. The headless oracle uses netMode0 so SendTileSquare returns before
        // network I/O; that permits the source's recursive continuation to run.
        var workspace = new Workspace(600, 500);
        workspace.InitializeVanillaGenerationSolidity();
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                Set(x, y, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                Set(x, y, default);
        for (int x = 300; x <= 302; x++)
            for (int y = 250; y <= 252; y++)
                Set(x, y, new WorldTile { Type = 59, Flags = WorldTileFlags.Active });
        if (arrangement != "enclosed")
            for (int x = 300; x <= 302; x++)
                Set(x, 249, default);
        if (arrangement == "lava")
            Set(301, 249, new WorldTile { LiquidAmount = byte.MaxValue, LiquidKind = WorldLiquidKind.Lava });
        var random = new RandomAdapter(seed);
        var spread = new GenerationGrass1458(
            workspace.TileStore, random, TestContext.Current.CancellationToken,
            worldSurface: 140, workspace: workspace);

        if (arrangement == "open")
            Assert.NotEqual(originalHash, HashSixFields(workspace));
        else
            Assert.Equal(originalHash, HashSixFields(workspace));
        spread.Apply(300, 250, 59, (ushort)target);

        string actualHash = HashSixFields(workspace);
        Assert.True(originalHash == actualHash,
            $"Expected {originalHash}, actual {actualHash}; " +
            string.Join(";", from x in Enumerable.Range(300, 3)
                from y in Enumerable.Range(249, 4)
                let tile = workspace.TileStore.Get(x, y)
                select $"{x},{y}:{tile.IsActive},{tile.Type},{tile.FrameX},{tile.FrameY}"));
        Assert.Equal(expectedNext, random.Next());

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
