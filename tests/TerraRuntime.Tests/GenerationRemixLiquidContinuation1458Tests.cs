using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixLiquidContinuation1458Tests
{
    [Fact]
    public void Original_full_helper_liquid_basins_and_spawn_scrub_match_full_grid()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, seed 1458,
        // 1000x800, surface 200, rock 201, spawn (500,650). Two sealed stone
        // basins force a retained off-spawn flow and a scrubbed spawn flow.
        Workspace workspace = new(1000, 800);
        Assert.True(workspace.TrySetLayers(200, 201));
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.Remix,
            VanillaSecretWorldSeed1458.None));
        workspace.SetVanillaBootstrapState(new VanillaWorldGenerationBootstrapState1458
        {
            EffectiveCrimson = false, CrimsonLeft = false,
            HellChestItems = [], TreeX = [], TreeStyle = [], CaveBackX = [],
            CaveBackStyle = [], ForestBackgroundStyles = []
        });
        Put(100, 50, 0, wall: 178);
        Put(120, 180, 0, wall: 180);
        Put(140, 100, 56, active: true);
        Put(160, 250, 0, active: true);
        Put(500, 50, 0, wall: 87);
        for (int y = 290; y <= 500; y++) { Put(299, y, 1, active: true); Put(301, y, 1, active: true); }
        Put(300, 500, 1, active: true);
        Put(300, 300, 0, liquid: 200);
        for (int y = 290; y <= 650; y++) { Put(499, y, 1, active: true); Put(501, y, 1, active: true); }
        Put(500, 650, 1, active: true);
        Put(500, 300, 0, liquid: 220);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 650,
            randomSpawn: false, TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)200, workspace.TileStore.Get(300, 499).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(500, 649).LiquidAmount);
        Assert.Equal("7D4F09931C77096F23B00CAC031F6B3F88665C1A2C079489D1D9B310776D1AE5", FullGridHash());
        // Source AddTrees consumes two density doubles, one grass offer, then
        // the later cavern decoration draws. Liquid settlement spends no genRand.
        random.NextDouble();
        random.NextDouble();
        if (random.Next(7) == 0)
            random.Next(2);
        random.Next(2, 5);
        random.Next(390, 610);
        random.Next(10, 31);
        Assert.Equal(1802539725, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, bool active = false, byte liquid = 0) =>
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = type, Wall = wall, Flags = active ? WorldTileFlags.Active : 0,
                LiquidAmount = liquid
            });

        string FullGridHash()
        {
            var cells = new StringBuilder(1000 * 800 * 16);
            for (int x = 0; x < 1000; x++)
            for (int y = 0; y < 800; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                cells.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';');
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cells.ToString())));
        }
    }

    private sealed class SourceRandom(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Next() => source.Next();
        public int Next(int max) => source.Next(max);
        public int Next(int min, int max) => source.Next(min, max);
        public double NextDouble() => source.NextDouble();
        public void NextBytes(byte[] bytes) => source.NextBytes(bytes);
    }
}
