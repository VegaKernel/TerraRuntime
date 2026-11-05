using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixGrowthComposite1458Tests
{
    [Fact]
    public void Original_full_helper_growth_liquids_palette_and_cavern_band_compose()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, 1000x800,
        // seed1458. The source harness registers tileSolid[2] and frame-important
        // 5/596/616, as the official Main metadata initializer does.
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
        Put(700, 300, 226, active: true);
        Put(710, 300, 61, active: true);
        Put(710, 301, 226, active: true);
        Put(720, 300, 137, active: true, frameY: 18);
        Put(730, 300, 0, wall: 87);
        Put(740, 300, 0, wall: 34);
        Put(750, 300, 0, wall: 244);
        Put(800, 300, 41, wall: 7, active: true);
        Put(810, 300, 19, wall: 7, active: true);
        Put(820, 300, 19, wall: 7, active: true, frameY: 180);
        Put(830, 300, 0, wall: 7);
        Put(840, 300, 481, active: true);
        for (int y = 290; y <= 500; y++) { Put(199, y, 1, active: true); Put(201, y, 1, active: true); }
        Put(200, 500, 1, active: true);
        Put(200, 300, 0, liquid: 200);
        for (int y = 290; y <= 650; y++) { Put(499, y, 1, active: true); Put(501, y, 1, active: true); }
        Put(500, 650, 1, active: true);
        Put(500, 300, 0, liquid: 220);
        for (int x = 340; x <= 660; x++) Put(x, 700, 637, active: true);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 650,
            randomSpawn: false, TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixAddTreesGrassGrowth(workspace, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixPaletteSparse(workspace,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixDungeonPaletteSparse(workspace,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)200, workspace.TileStore.Get(200, 499).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(500, 649).LiquidAmount);
        Assert.Equal((ushort)5, workspace.TileStore.Get(300, 390).Type);
        Assert.Equal((ushort)596, workspace.TileStore.Get(304, 394).Type);
        Assert.Equal((byte)22, workspace.TileStore.Get(700, 300).TileColor);
        Assert.Equal((byte)25, workspace.TileStore.Get(730, 300).WallColor);
        Assert.Equal((byte)22, workspace.TileStore.Get(800, 300).TileColor);
        Assert.Equal((byte)25, workspace.TileStore.Get(830, 300).WallColor);
        Assert.Equal((byte)0, workspace.TileStore.Get(820, 300).TileColor);
        Assert.NotEqual((short)0, workspace.TileStore.Get(540, 700).FrameX);
        Assert.Equal("9484542337D595A5D1041E7EFD8B7D1F6EAD8F2544A6283FC45E64D1E793A581", FullGridHash());
        Assert.Equal(447583895, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, bool active = false,
            byte liquid = 0, short frameY = 0) =>
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = type, Wall = wall, Flags = active ? WorldTileFlags.Active : 0,
                LiquidAmount = liquid, FrameY = frameY
            });

        string FullGridHash()
        {
            var cells = new StringBuilder(1000 * 800 * 20);
            for (int x = 0; x < 1000; x++)
            for (int y = 0; y < 800; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                cells.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(',').Append(tile.TileColor).Append(',')
                    .Append(tile.WallColor).Append(';');
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
