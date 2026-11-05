using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixTreeGrowth1458Tests
{
    [Fact]
    public void Original_finish_remix_grows_ordinary_and_sakura_trees_with_full_grid_rng()
    {
        // Untouched 1.4.5.8 FinishRemixWorld, seed1458, 1000x800, with
        // source grass2 x300..304/y400 in its underground AddTrees band.
        // The headless original probe registers canonical frame-important
        // identities 5/596/616 before invoking the unchanged helper.
        Workspace workspace = new(1000, 800);
        workspace.InitializeVanillaGenerationSolidity();
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
        for (int x = 300; x <= 304; x++) Put(x, 400, 2, active: true);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 400,
            randomSpawn: false, TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixAddTreesGrassGrowth(workspace, random, framing,
            TestContext.Current.CancellationToken);
        // This fixture has no dungeon/special paint or tile637; the cavern
        // tail still consumes its source band/center/radius genRand draws.
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((ushort)5, workspace.TileStore.Get(300, 390).Type);
        Assert.Equal((ushort)596, workspace.TileStore.Get(304, 394).Type);
        Assert.Equal(505734682, random.Next());
        Assert.Equal("0419BC83B8D87D6C5B5F3869158539922DE05DF35C270B56E29AE93CF3BC6D14", FullGridHash());

        void Put(int x, int y, ushort type, ushort wall = 0, bool active = false) =>
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = type, Wall = wall, Flags = active ? WorldTileFlags.Active : 0
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
