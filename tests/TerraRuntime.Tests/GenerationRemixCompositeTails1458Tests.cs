using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixCompositeTails1458Tests
{
    [Fact]
    public void Original_full_helper_liquids_palette_and_cavern_band_compose()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld on a single
        // 1000x800/seed1458 fixture. The independent paint, basin, and tile637
        // sites are non-overlapping but exercise the ordered helper together.
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
        Put(500, 50, 0, wall: 87);
        Put(700, 300, 226, active: true);
        Put(710, 300, 61, active: true);
        Put(710, 301, 226, active: true);
        Put(720, 300, 137, active: true, frameY: 18);
        Put(730, 300, 0, wall: 87);
        Put(740, 300, 0, wall: 34);
        Put(750, 300, 0, wall: 244);
        for (int y = 290; y <= 500; y++) { Put(299, y, 1, active: true); Put(301, y, 1, active: true); }
        Put(300, 500, 1, active: true);
        Put(300, 300, 0, liquid: 200);
        for (int y = 290; y <= 650; y++) { Put(499, y, 1, active: true); Put(501, y, 1, active: true); }
        Put(500, 650, 1, active: true);
        Put(500, 300, 0, liquid: 220);
        for (int x = 340; x <= 660; x++) Put(x, 700, 637, active: true);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 650,
            randomSpawn: false, TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixAddTreesNoGrowth(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixPaletteSparse(workspace,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)200, workspace.TileStore.Get(300, 499).LiquidAmount);
        Assert.Equal((byte)0, workspace.TileStore.Get(500, 649).LiquidAmount);
        Assert.Equal((byte)22, workspace.TileStore.Get(700, 300).TileColor);
        Assert.Equal((byte)25, workspace.TileStore.Get(730, 300).WallColor);
        Assert.Equal((short)108, workspace.TileStore.Get(500, 700).FrameX);
        Assert.Equal("1A88A4B3DC7148A874B003F2CE18523A30D2543C0FCBED5725CEF83E1A5AAB3D", FullGridHash());
        Assert.Equal(2016854496, random.Next());

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
