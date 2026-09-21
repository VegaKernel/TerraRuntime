using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.QuickCleanup</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell.
///
/// Every fixture but one is run under two seeds and the pair must agree, because only the ocean's sand column
/// spends shared RNG - and that one fixture's two seeds must therefore DISAGREE, which is what pins the draw
/// to the loop's own condition rather than to a single sample taken once.
///
/// The world has to be a thousand columns wide, because <c>beachDistance</c> is a readonly 380 in the source:
/// a narrower world has no middle that is not ocean, and the ocean rules would then reach every fixture. The
/// ground is bare air over dirt over stone, and each fixture adds one thing: lava inside and outside the
/// ocean's columns and below its floor line, sand over open water, a hive wall, a temple wall, sand
/// overhanging open space near two different walls, every kind of slope over open space, and a row of slopes
/// with traps among them.
/// </remarks>
public sealed class QuickCleanupPass1458Tests
{
    private const int Width = 1000;
    private const int Height = 300;
    private const double WorldSurface = 100.0;
    private const double RockLayer = 160.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    // Bare ground with nothing to clean: the pass must leave it byte for byte as it was, and spend nothing.
    [InlineData("clean", 42, 668106, "172b264676b55f43e09f0ebb6a2c591841c969b88bcd6079a2789ac9e529295f")]
    [InlineData("clean", 1458, 422351, "172b264676b55f43e09f0ebb6a2c591841c969b88bcd6079a2789ac9e529295f")]
    // Lava in three places: inside the ocean's columns above its floor line, outside them, and inside them
    // but below the line. Only the first is forced back to water.
    [InlineData("ocean-liquid", 42, 668106, "3cf8c54e002adae0d7cbd908e91fb96ea9a9b25c1c41d8c6e7cee666f780ec02")]
    [InlineData("ocean-liquid", 1458, 422351, "3cf8c54e002adae0d7cbd908e91fb96ea9a9b25c1c41d8c6e7cee666f780ec02")]
    // Sand at the ocean surface over open space. This is the only half of the pass that draws, and the two
    // seeds MUST disagree - both in the next draw and in the world - because the length of every column is
    // redrawn on each step of the loop that writes it. One column is bottom-sloped, which is flattened before
    // the write; another already holds sandstone four rows down, which stops the write short; and a third
    // holds hardened sand on the very first row the column would write, which is the only place the row's own
    // test can differ from the two rows below it.
    [InlineData("ocean-sand", 42, 280508, "61f6c26540617391e3740c23b7d24cc5f36757a38793b20661ed032ea2cc22c6")]
    [InlineData("ocean-sand", 1458, 597545, "849696eda33c8689128b82fdc6682751a5709a3c3e15e590b01ec03f07815299")]
    // A hive wall over mud in the shallow band and over granite in the deep one, part of each deactivated
    // but still carrying its identity. Every one of them is retyped to sandstone, present or not; the shallow
    // band's liquid is emptied and the deep band's is filled to the brim and turned to lava, which is a depth
    // rule and not a liquid one.
    [InlineData("hive", 42, 668106, "38983fd3dbeacc23b0179048674dd886fa20ed2681d72c7fbbf5a0f9f39e3ce1")]
    [InlineData("hive", 1458, 422351, "38983fd3dbeacc23b0179048674dd886fa20ed2681d72c7fbbf5a0f9f39e3ce1")]
    // The temple wall, which takes the same two rules, over gems and over marble.
    [InlineData("temple", 42, 668106, "34c74ffdf8860be2ae59f09cc6d2a2c2799dc3304c7916fc82172b61f8964369")]
    [InlineData("temple", 1458, 422351, "34c74ffdf8860be2ae59f09cc6d2a2c2799dc3304c7916fc82172b61f8964369")]
    // Sand overhanging open unpapered space, with one wall three columns to its left and a different wall
    // three columns to its right. The borrowed wall is the right-hand one, because the search breaks out of
    // its inner loop only and so keeps the last column's find. One sand cell already carries a wall of its own
    // with a different wall inside its box: it keeps its own, and only the open cell under it is papered.
    [InlineData("sandwall", 42, 668106, "29859cef74424b8007c7b56809b1399b3e350208a381d4a155a0635ff3ca8a04")]
    [InlineData("sandwall", 1458, 422351, "29859cef74424b8007c7b56809b1399b3e350208a381d4a155a0635ff3ca8a04")]
    // Every kind of slope over open space: half brick, both top slopes, a bottom slope, a platform, the one
    // half-brick identity the pass exempts, a non-solid identity that cannot keep a slope at all, and an
    // inactive cell carrying a slope it should never have kept.
    [InlineData("slopes", 42, 668106, "af04e79d50179ae2ccd3eb90806769fe2887ea5f814e619b99d2471e9a497dfe")]
    [InlineData("slopes", 1458, 422351, "af04e79d50179ae2ccd3eb90806769fe2887ea5f814e619b99d2471e9a497dfe")]
    // A row of top-sloped blocks on solid ground with two traps among them - one of them deactivated but
    // still typed as a trap. A trap beside a sloped cell drops it whatever holds it up, and the deactivated
    // one drops its neighbours just the same. Two more blocks stand ON a trap and on an active stone block
    // with no trap either side, which only the pass's own temporary solidity override can decide.
    [InlineData("traps", 42, 668106, "16b3ad7fff88bd0a79f3e88e96fb5d7ce377210f3b0b2074bed2d16981607f68")]
    [InlineData("traps", 1458, 422351, "16b3ad7fff88bd0a79f3e88e96fb5d7ce377210f3b0b2074bed2d16981607f68")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new QuickCleanupPass1458(
                store, random, WorldSurface, RockLayer, BeachDistance, TestContext.Current.CancellationToken)
            .Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < 60)
            {
                tile.FrameX = -1;
                tile.FrameY = -1;
            }
            else
            {
                tile.Flags = WorldTileFlags.Active;
                tile.Type = y < 100 ? (ushort)0 : (ushort)1;
            }

            store.Set(x, y, tile);
        }

        switch (fixture)
        {
            case "ocean-liquid":
                Wet(store, 30, 71, 62, 66, WorldLiquidKind.Lava);
                Wet(store, 400, 441, 62, 66, WorldLiquidKind.Lava);
                Wet(store, 30, 71, 172, 176, WorldLiquidKind.Lava);
                break;
            case "ocean-sand":
                for (int x = 30; x < 71; x++)
                {
                    Hollow(store, x, 61, 100);
                    Solid(store, x, 60, 53);
                }

                Shape(store, 40, 60, 4);
                Solid(store, 60, 64, 397);
                Solid(store, 50, 61, 495);
                break;
            case "hive":
                Paper(store, 450, 501, 120, 141, 187);
                Retype(store, 450, 501, 120, 141, 59);
                Wet(store, 450, 501, 120, 141, WorldLiquidKind.Water);
                Paper(store, 450, 501, 180, 201, 187);
                Retype(store, 450, 501, 180, 201, 368);
                Wet(store, 450, 501, 180, 201, WorldLiquidKind.Water);
                Ghost(store, 460, 471, 125, 130);
                Ghost(store, 460, 471, 185, 190);
                break;
            case "temple":
                Paper(store, 450, 501, 120, 141, 216);
                Retype(store, 450, 501, 120, 141, 123);
                Wet(store, 450, 501, 120, 141, WorldLiquidKind.Water);
                Paper(store, 450, 501, 180, 201, 216);
                Retype(store, 450, 501, 180, 201, 367);
                Wet(store, 450, 501, 180, 201, WorldLiquidKind.Water);
                Ghost(store, 460, 471, 125, 130);
                Ghost(store, 460, 471, 185, 190);
                break;
            case "sandwall":
                for (int x = 520; x < 541; x++)
                {
                    Hollow(store, x, 61, 100);
                    Solid(store, x, 60, 53);
                }

                Paper(store, 518, 519, 58, 62, 2);
                Paper(store, 542, 543, 58, 62, 3);
                Paper(store, 530, 531, 60, 61, 4);
                Paper(store, 533, 534, 58, 59, 5);
                break;
            case "slopes":
                for (int x = 560; x < 601; x++)
                    Hollow(store, x, 61, 100);
                for (int x = 560; x < 601; x += 8)
                {
                    Solid(store, x, 60, 1);
                    Shape(store, x, 60, 1);
                    Solid(store, x + 1, 60, 1);
                    Shape(store, x + 1, 60, 2);
                    Solid(store, x + 2, 60, 1);
                    Shape(store, x + 2, 60, 4);
                    Solid(store, x + 3, 60, 19);
                    Shape(store, x + 3, 60, 2);
                    Solid(store, x + 4, 60, 225);
                    Shape(store, x + 4, 60, 1);
                    Solid(store, x + 5, 60, 3);
                    Shape(store, x + 5, 60, 2);
                    Shape(store, x + 6, 59, 3);
                    Solid(store, x + 7, 59, 1);
                    Shape(store, x + 7, 59, 4);
                }

                break;
            case "traps":
                for (int x = 560; x < 601; x++)
                {
                    Solid(store, x, 60, 1);
                    Shape(store, x, 60, 2);
                }

                Solid(store, 570, 60, 137);
                Solid(store, 580, 60, 137);
                WorldTile ghost = store.Get(580, 60);
                ghost.Flags &= ~WorldTileFlags.Active;
                store.Set(580, 60, ghost);
                Solid(store, 594, 61, 137);
                Solid(store, 597, 61, 130);
                break;
        }

        return store;
    }

    private static void Ghost(WorldTileStore store, int left, int right, int top, int bottom)
    {
        for (int x = left; x < right; x++)
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Flags &= ~WorldTileFlags.Active;
            store.Set(x, y, tile);
        }
    }

    private static void Solid(WorldTileStore store, int x, int y, ushort type)
    {
        WorldTile tile = store.Get(x, y);
        tile.Flags |= WorldTileFlags.Active;
        tile.Type = type;
        tile.FrameX = 0;
        tile.FrameY = 0;
        store.Set(x, y, tile);
    }

    private static void Shape(WorldTileStore store, int x, int y, byte shape)
    {
        WorldTile tile = store.Get(x, y);
        tile.Shape = shape;
        store.Set(x, y, tile);
    }

    private static void Hollow(WorldTileStore store, int x, int top, int bottom)
    {
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Flags &= ~WorldTileFlags.Active;
            tile.FrameX = -1;
            tile.FrameY = -1;
            store.Set(x, y, tile);
        }
    }

    private static void Wet(WorldTileStore store, int left, int right, int top, int bottom, WorldLiquidKind kind)
    {
        for (int x = left; x < right; x++)
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.LiquidAmount = 100;
            tile.LiquidKind = kind;
            store.Set(x, y, tile);
        }
    }

    private static void Paper(WorldTileStore store, int left, int right, int top, int bottom, ushort wall)
    {
        for (int x = left; x < right; x++)
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Wall = wall;
            store.Set(x, y, tile);
        }
    }

    private static void Retype(WorldTileStore store, int left, int right, int top, int bottom, ushort type)
    {
        for (int x = left; x < right; x++)
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive)
                continue;

            tile.Type = type;
            store.Set(x, y, tile);
        }
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
