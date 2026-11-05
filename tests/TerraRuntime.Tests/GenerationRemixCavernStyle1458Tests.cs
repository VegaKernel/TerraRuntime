using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixCavernStyle1458Tests
{
    [Fact]
    public void Original_full_helper_tile637_band_matches_grid_and_shared_rng()
    {
        // Untouched 1.4.5.8 FinishRemixWorld, seed1458, 1000x800,
        // active tile637 row y600/x340..660. Original helper's genRand and
        // Main.rand refer to the same UnifiedRandom in this harness.
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
        for (int x = 340; x <= 660; x++) Put(x, 600, 637, active: true);

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
        // Ordinary palette does not touch type637, and the grid oracle below
        // deliberately excludes paint fields already covered separately.
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((short)108, workspace.TileStore.Get(500, 600).FrameX);
        Assert.Equal("EAC6540378F1906E05AD4B7D755A8AFCB82576287872FD2841A1B7F309DF64B3", FullGridHash());
        Assert.Equal(2016854496, random.Next());

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
