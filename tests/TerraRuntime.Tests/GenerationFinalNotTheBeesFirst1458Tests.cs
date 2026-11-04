using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Runtime;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

public sealed class GenerationFinalNotTheBeesFirst1458Tests
{
    [Fact]
    public void Original_not_the_bees_helper_matches_full_grid_and_rng_on_repeatable_input()
    {
        // Direct TerrariaServer 1.4.5.8 WorldGen.NotTheBees helper, 600x500,
        // seed1458, surface140, rock200, lava300, left-side dungeon.
        // This is one invocation of the helper, not the later FinishNotTheBees tail.
        const string original = "4c35e52cf0a66098d9614a124338e7879e553c3896d1e2d6d28a557ee35f30fa";
        var workspace = CreateOfficialFixture();
        var random = new RandomAdapter(1458);

        Assert.NotEqual(original, Hash(workspace));
        NotTheBeesEarly1458.Apply(workspace, random, workspace.VanillaSeedProfile,
            140, 200, 300, -1, TestContext.Current.CancellationToken);

        Assert.Equal(original, Hash(workspace));
        Assert.Equal(1103903621, random.Next());

    }

    [Fact]
    public void Omitting_bees_helper_fails_official_grid_and_rng()
    {
        // Test-only omission of the helper on the exact independently captured input.
        var workspace = CreateOfficialFixture();
        var random = new RandomAdapter(1458);
        Assert.NotEqual("4c35e52cf0a66098d9614a124338e7879e553c3896d1e2d6d28a557ee35f30fa",
            Hash(workspace));
        Assert.NotEqual(1103903621, random.Next());
    }

    private static Workspace CreateOfficialFixture()
    {
        var workspace = new Workspace(600, 500);
        workspace.SetVanillaSeedProfile(new(VanillaSpecialWorldSeed1458.NotTheBees, VanillaSecretWorldSeed1458.None));
        for (int x = 0; x < 600; x++)
            for (int y = 0; y < 500; y++)
                Set(x, y, Cell(1, true));
        for (int x = 120; x < 180; x++)
            for (int y = 120; y < 300; y++)
                Set(x, y, default);
        Set(400, 140, Cell(53, true));
        Set(400, 141, Cell(397, true));
        Set(410, 180, Cell(2, true));
        Set(420, 180, Cell(59, true, wall: 15));
        Set(440, 190, Cell(1, true, wall: 86, amount: 200));
        Set(450, 190, Cell(1, true, wall: 7, amount: 200, kind: WorldLiquidKind.Honey));
        Set(500, 160, Cell(224, true));
        return workspace;

        void Set(int x, int y, WorldTile tile) => workspace.TileStore.Set(x, y, in tile);
    }

    private static WorldTile Cell(
        ushort type, bool active, ushort wall = 0, byte amount = 0, WorldLiquidKind kind = WorldLiquidKind.Water) =>
        new()
        {
            Type = type,
            Wall = wall,
            LiquidAmount = amount,
            LiquidKind = kind,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
        };

    private static string Hash(Workspace workspace)
    {
        var snapshot = new StringBuilder(workspace.WidthTiles * workspace.HeightTiles * 20);
        for (int x = 0; x < workspace.WidthTiles; x++)
            for (int y = 0; y < workspace.HeightTiles; y++)
            {
                WorldTile tile = workspace.TileStore.Get(x, y);
                snapshot.Append(tile.IsActive ? '1' : '0').Append(',')
                    .Append(tile.Type).Append(',').Append(tile.Wall).Append(',')
                    .Append(tile.FrameX).Append(',').Append(tile.FrameY).Append(',')
                    .Append(tile.LiquidAmount).Append(';')
                    .Append((byte)tile.LiquidKind).Append(',')
                    .Append(tile.TileColor).Append(',').Append(tile.WallColor).Append(';');
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
