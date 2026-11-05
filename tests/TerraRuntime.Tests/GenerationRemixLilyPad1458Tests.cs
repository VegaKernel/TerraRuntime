using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixLilyPad1458Tests
{
    [Fact]
    public void Original_final_scan_moves_single_lily_pad_to_golf_grass_support()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, 1000x800,
        // seed1458, pad (200,400), support golf grass477 at y430. Headless
        // original registers tileSolid[477] and tileFrameImportant[518].
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
        Put(200, 400, 518, active: true);
        Put(200, 430, 477, active: true);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace,
            useWorldGenerationSolidity: true);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 400,
            randomSpawn: false, TestContext.Current.CancellationToken);
        // Neither the lone pad nor its unsupported-for-trees golf-grass
        // support can grow a tree. Source AddTrees only takes density draws.
        random.NextDouble();
        random.NextDouble();
        FinalPass1458.FinishRemixCavernStyle637(workspace.TileStore, random, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixLilyPadScan(workspace.TileStore, framing,
            TestContext.Current.CancellationToken);

        Assert.False(workspace.TileStore.Get(200, 400).IsActive);
        Assert.True(workspace.TileStore.Get(200, 429).IsActive);
        Assert.Equal((ushort)518, workspace.TileStore.Get(200, 429).Type);
        Assert.Equal("C0A20DB39ABC68821B01193034AD7CFA02AF530509CDB071C38649E23C427033", FullGridHash());
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
