using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixPaletteSparse1458Tests
{
    [Fact]
    public void Original_full_helper_no_growth_palette_matches_grid_and_rng()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, 1000x800,
        // seed1458/surface200/rock201. The six paint sites are deep enough
        // that Convert's surface scan cannot confound palette attribution.
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
        Put(300, 300, 226, active: true);
        Put(310, 300, 61, active: true);
        Put(310, 301, 226, active: true);
        Put(320, 300, 137, active: true, frameY: 18);
        Put(330, 300, 0, wall: 87);
        Put(340, 300, 0, wall: 34);
        Put(350, 300, 0, wall: 244);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 400,
            randomSpawn: false, TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixAddTreesNoGrowth(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixPaletteSparse(workspace,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)22, workspace.TileStore.Get(300, 300).TileColor);
        Assert.Equal((byte)25, workspace.TileStore.Get(330, 300).WallColor);
        Assert.Equal("E0C8EAC5F9FBA940763889136B4EA29A4BE393CCD4DBC83F341170FDDF258C75", FullGridHash());
        // Source cavern decoration runs after the palette and draws even when
        // no tile637 exists; Main.rand branches remain inert on this fixture.
        random.Next(2, 5);
        random.Next(390, 610);
        random.Next(10, 31);
        Assert.Equal(35779028, random.Next());

        void Put(int x, int y, ushort type, ushort wall = 0, bool active = false, short frameY = 0) =>
            workspace.TileStore.Set(x, y, new WorldTile
            {
                Type = type, Wall = wall, FrameY = frameY,
                Flags = active ? WorldTileFlags.Active : 0
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
