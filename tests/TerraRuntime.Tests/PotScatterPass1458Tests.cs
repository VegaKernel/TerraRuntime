using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.PotsGraveyardsAndBoulderPiles</c> pass, whose ordinary path is the pot scatter.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell.
///
/// The ground is a stack of floors with two rows of air over each, which is exactly what a pot needs - it
/// occupies the two rows above the row it is offered at and stands on the row below. Nine bands of columns
/// carry one material each so every arm of the style cascade runs: stone, snow, jungle grass, dungeon brick,
/// ebonstone, crimstone, marble, lihzahrd brick, and a band papered with the temple wall. Above the surface
/// line every other column is papered, so both sides of the wall gate run in one scan.
///
/// The fixtures then vary one thing each: no walls at all above the surface line, standing lava and shimmer in
/// the gaps, and a stack with only one row of air per floor so every offer is refused and the walk has to run
/// the column out. That last one places nothing at all and still has its own draw count, which is what pins
/// the style draw that a refused row spends anyway.
/// </remarks>
public sealed class PotScatterPass1458Tests
{
    private const int Width = 1600;
    private const int Height = 700;
    private const double WorldSurface = 200.0;
    private const double WorldSurfaceHigh = 150.0;
    private const double WorldSurfaceLow = 120.0;
    private const double RockLayer = 300.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    // The banded stack: every arm of the style cascade runs, and the underworld override wins over each
    // material below the underworld line while still paying for the material's own draw.
    [InlineData("pots", 42, 607978, "c5f685d6b7816447ba6bc379e42aafbd681045d5c9470628803f204fae97bb8e")]
    [InlineData("pots", 1458, 333734, "fdc41831ca72749fd0d507d7a129caef01a6ec4e38a0a431da4dfaaf0477c7fe")]
    // No walls anywhere above the surface line, so every row up there is walked past without a draw and
    // the pots all land below it.
    [InlineData("bare", 42, 57332, "a0c5030bcfdb6ad53b25070e4b93c8cc4175178a49b1f7afdd693cfed81ce9f2")]
    [InlineData("bare", 1458, 783814, "87340e2718dde5b1129899f69ee4017a7ceb640a3da3e336b22b37edd6cd51ad")]
    // Standing lava and shimmer in two bands. They refuse at two different points - the floor scan will not
    // accept a block whose cell above holds either, and the placement test rejects the pot's own cell.
    [InlineData("wet", 42, 881060, "2b67cacdeef0a4547a9c8bb9ef25ef03e7c08cb8a94a14fdd5a99f8d8e3fcf31")]
    [InlineData("wet", 1458, 848161, "fb274a150e080e0eb36168274037f0548795c71f696f87aaa700f7f8448dbb92")]
    // One row of air per floor in the upper half instead of two, so every offer up there is refused for want
    // of headroom and pays its style draw anyway before the walk reaches the ordinary stack below and lands.
    [InlineData("tight", 42, 920214, "6776720a2f4f2c9ab19cde6b313edae2647365f17d91cbd4a7130b3975480e74")]
    [InlineData("tight", 1458, 705666, "0c50ddde133f91e64ef0a563e75785125205765e8f309bfd0af24974032c133a")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new PotScatterPass1458(
                store, random, WorldSurface, WorldSurfaceHigh, WorldSurfaceLow, RockLayer, BeachDistance,
                TestContext.Current.CancellationToken)
            .Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        bool noWalls = fixture == "bare";
        bool oneRowGap = fixture == "tight";

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < 120)
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = 1;
            }

            store.Set(x, y, tile);
        }

        for (int y = 121; y < Height - 1; y++)
        {
            // "tight" leaves one row of air per floor in the upper half only: every offer up there is refused
            // for want of headroom and pays its style draw anyway, and the walk then reaches the ordinary
            // stack below and lands.
            int pitch = oneRowGap && y < 400 ? 2 : 3;
            if (y % pitch == 0)
                continue;

            for (int x = 0; x < Width; x++)
            {
                WorldTile tile = store.Get(x, y);
                tile.Flags &= ~WorldTileFlags.Active;
                tile.FrameX = -1;
                tile.FrameY = -1;
                store.Set(x, y, tile);
            }
        }

        ushort[] materials = [1, 147, 60, 41, 25, 203, 367, 226, 1];
        for (int band = 0; band < materials.Length; band++)
        {
            int left = 390 + band * 90;
            for (int x = left; x < left + 90 && x < Width; x++)
            for (int y = 120; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (!tile.IsActive)
                    continue;

                tile.Type = materials[band];
                store.Set(x, y, tile);
            }
        }

        // The snow band is papered with the temple wall for its whole depth, so its cells match TWO arms of
        // the style cascade at once - and above the underworld line, where nothing overrides them both, which
        // is the only place a cascade of separate statements can be told from a chain of else-ifs.
        for (int x = 480; x < 570; x++)
        for (int y = 120; y < Height; y++)
            PaperIfOpen(store, x, y, 216);

        for (int x = 390 + 8 * 90; x < Width; x++)
        for (int y = 120; y < Height; y++)
            PaperIfOpen(store, x, y, 216);

        if (!noWalls)
        {
            for (int x = 0; x < Width; x += 2)
            for (int y = 120; y < 200; y++)
                PaperIfOpen(store, x, y, 2);
        }

        // Four deep bands, each reaching a rule the banded stack above cannot. They sit below row 450 so the
        // walk always enters them with a floor already found, and past the ocean's floor line so the ocean
        // test does not apply. Two of them exist for the support tests: a pot needs its support to be an
        // unactuated FLAT SOLID block, and neither "not flat" nor "not solid" is reachable from plain stone.
        // The third gives the style cascade a cell matching two arms at once, which is the only way to tell
        // separate statements from a chain of else-ifs.
        for (int x = 1230; x < 1310; x++)
        for (int y = 460; y < Height - 1; y++)
            RetypeIfSolid(store, x, y, 3);
        for (int x = 1320; x < 1400; x++)
        for (int y = 460; y < Height - 1; y++)
            HalfBrickIfSolid(store, x, y);
        for (int x = 1410; x < 1490; x++)
        for (int y = 460; y < Height - 1; y++)
        {
            RetypeIfSolid(store, x, y, 147);
            PaperIfOpen(store, x, y, 216);
        }

        if (fixture == "wet")
        {
            for (int x = 500; x < 560; x++)
            for (int y = 300; y < 400; y++)
                WetIfOpen(store, x, y, WorldLiquidKind.Lava);
            for (int x = 700; x < 760; x++)
            for (int y = 300; y < 400; y++)
                WetIfOpen(store, x, y, WorldLiquidKind.Shimmer);

            // A liquid region also poisons the floors inside it, because the row a pot would occupy IS the row
            // the floor scan checks. Wetting every other ledge leaves the entry ledge dry and the next one
            // wet, which is the only arrangement where the pot's OWN cell is what refuses it.
            for (int x = 1500; x < 1580; x++)
            for (int y = 460; y < 620; y++)
                if (y % 6 == 2)
                    WetIfOpen(store, x, y, WorldLiquidKind.Lava);
        }

        return store;
    }

    private static void RetypeIfSolid(WorldTileStore store, int x, int y, ushort type)
    {
        WorldTile tile = store.Get(x, y);
        if (!tile.IsActive)
            return;

        tile.Type = type;
        store.Set(x, y, tile);
    }

    private static void HalfBrickIfSolid(WorldTileStore store, int x, int y)
    {
        WorldTile tile = store.Get(x, y);
        if (!tile.IsActive)
            return;

        tile.Shape = 1;
        store.Set(x, y, tile);
    }

    private static void PaperIfOpen(WorldTileStore store, int x, int y, ushort wall)
    {
        WorldTile tile = store.Get(x, y);
        if (tile.IsActive)
            return;

        tile.Wall = wall;
        store.Set(x, y, tile);
    }

    private static void WetIfOpen(WorldTileStore store, int x, int y, WorldLiquidKind kind)
    {
        WorldTile tile = store.Get(x, y);
        if (tile.IsActive)
            return;

        tile.LiquidAmount = 200;
        tile.LiquidKind = kind;
        store.Set(x, y, tile);
    }

    private static string Hash(WorldTileStore store)
    {
        var sb = new StringBuilder();
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            int slope = tile.Shape >= 2 ? tile.Shape - 1 : 0;
            sb.Append(tile.IsActive ? '1' : '0').Append(',')
              .Append(tile.Type).Append(',')
              .Append(tile.Wall).Append(',')
              .Append(tile.FrameX).Append(',')
              .Append(tile.FrameY).Append(',')
              .Append(tile.LiquidAmount).Append(',')
              .Append((int)tile.LiquidKind).Append(',')
              .Append(slope).Append(',')
              .Append(tile.Shape == 1 ? '1' : '0').Append(';');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private sealed class RandomAdapter(int seed) : IWorldGenerationVanillaRandom
    {
        private readonly VanillaUnifiedRandom1458 random = new(seed);
        public int Next() => random.Next();
        public int Next(int max) => random.Next(max);
        public int Next(int min, int max) => random.Next(min, max);
        public double NextDouble() => random.NextDouble();
        public void NextBytes(byte[] bytes) => random.NextBytes(bytes);
    }
}
