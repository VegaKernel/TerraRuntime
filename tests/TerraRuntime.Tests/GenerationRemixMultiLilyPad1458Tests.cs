using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixMultiLilyPad1458Tests
{
    [Fact]
    public void Original_final_scan_moves_four_separated_lily_pads_and_styles_them()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, 1000x800,
        // seed1458. Original metadata registers 518 as frame-important and
        // support types 477/109/60/116 as solid in the headless harness.
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
        foreach ((int x, ushort support) in new[] { (200, (ushort)477),
            (220, (ushort)109), (240, (ushort)60), (260, (ushort)116) })
        {
            Put(x, 400, 518, active: true);
            Put(x, 430, support, active: true);
        }

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 400,
            randomSpawn: false, TestContext.Current.CancellationToken);
        // These four isolated support cells have no neighbour needed to grow
        // a tree. Only the two AddTrees density doubles are source-reachable.
        random.NextDouble();
        random.NextDouble();
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixLilyPadScan(workspace.TileStore, framing,
            TestContext.Current.CancellationToken);

        foreach ((int x, short style) in new[] { (200, (short)0), (220, (short)18),
            (240, (short)36), (260, (short)18) })
        {
            Assert.False(workspace.TileStore.Get(x, 400).IsActive);
            Assert.Equal((ushort)518, workspace.TileStore.Get(x, 429).Type);
            Assert.Equal(style, workspace.TileStore.Get(x, 429).FrameY);
        }
        Assert.Equal("F8F5278B7C06F1D8F678D82D9B0D6E58952054950A35798F3337D93C504FBF63", FullGridHash());
        Assert.Equal(35779028, random.Next());

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
