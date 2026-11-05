using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationRemixDungeonPalette1458Tests
{
    [Fact]
    public void Original_full_helper_dungeon_brick_wall_platform_and_crack_match_grid_rng()
    {
        // Untouched TerrariaServer.exe 1.4.5.8 FinishRemixWorld, 1000x800,
        // seed1458. The headless source probe initializes the canonical
        // Main.tileDungeon[41] and Main.wallDungeon[7] registration arrays.
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
        Put(700, 300, 41, wall: 7, active: true);
        Put(710, 300, 19, wall: 7, active: true);
        Put(720, 300, 19, wall: 7, active: true, frameY: 180);
        Put(730, 300, 0, wall: 7);
        Put(740, 300, 481, active: true);

        var random = new SourceRandom(1458);
        var framing = new GenerationTileFraming1458(workspace.TileStore, random, workspace);
        int cloud = FinalPass1458.FinishRemixFirstScan(workspace, random,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixSecondScanSparse(workspace, cloud, random, framing,
            TestContext.Current.CancellationToken);
        FinalPass1458.FinishRemixFirstLiquidAndSpawnScrub(workspace.TileStore, 500, 400,
            randomSpawn: false, TestContext.Current.CancellationToken);
        // AddTrees sees no grass/saplings: only its two density draws survive.
        random.NextDouble();
        random.NextDouble();
        FinalPass1458.FinishRemixDungeonPaletteSparse(workspace,
            TestContext.Current.CancellationToken);
        // Later tile637 band is empty, but its three genRand draws still occur.
        random.Next(2, 5);
        random.Next(390, 610);
        random.Next(10, 31);
        FinalPass1458.FinishRemixFinalQuickWater(workspace.TileStore,
            TestContext.Current.CancellationToken);

        Assert.Equal((byte)22, workspace.TileStore.Get(700, 300).TileColor);
        Assert.Equal((byte)22, workspace.TileStore.Get(710, 300).TileColor);
        Assert.Equal((byte)0, workspace.TileStore.Get(720, 300).TileColor);
        Assert.Equal((byte)22, workspace.TileStore.Get(740, 300).TileColor);
        Assert.Equal((byte)25, workspace.TileStore.Get(730, 300).WallColor);
        Assert.Equal("6BE58FB7F742EF239ABF670A38879920A3BEC8A4C74D2783BEFCE1F0E0D74FD9", FullGridHash());
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
