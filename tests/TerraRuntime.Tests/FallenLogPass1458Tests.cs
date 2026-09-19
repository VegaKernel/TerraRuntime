using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.FallenLogsAndWaterFeatures</c> pass, whose whole body on an ordinary world is the fallen
/// logs.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG, a SHA-256 over every
/// field of every cell, and the anchor the pass leaves in <c>GenVars.logX</c>/<c>logY</c>.
///
/// Each fixture is a flat grass surface over stone with one obstacle added, because each obstacle is a separate
/// refusal in the source and they are reached at different points in the attempt budget: a band of wall inside
/// the ten-by-ten headroom the pass scans, a blob of corrupt stone inside its fifty-tile reach, a strip of sand,
/// and a pool of standing water. The anchor is part of the expectation on purpose - it is drawn one in two and
/// the Flowers pass later moves its first patch onto it, so losing that draw moves a whole flower patch.
/// </remarks>
public sealed class FallenLogPass1458Tests
{
    private const int Width = 4200;
    private const int Height = 600;
    private const int GroundRow = 200;
    private const double WorldSurface = 400.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, nextDraw, worldHash, anchorX, anchorY, logCells
    [InlineData("plain", 42, 257321,
        "5a638a8e052c5a00bf20bfb568dc138f4b89b467aeba0e26ea939f7067e4fd50", 2988, 199, 18)]
    [InlineData("plain", 1458, 448271,
        "c0ea2daf6c784cf323a083962aeba7da939700771939fb7c06dfc4baf1ac68ab", 1302, 199, 12)]
    [InlineData("walled", 42, 257321,
        "f844f8f9667be7a9e0b46068664c312d2fb63a50f96b2309b20b4845a91ec14a", 2988, 199, 18)]
    // The one fixture where the anchor draw is lost, so the Flowers pass would have no log to move onto.
    [InlineData("walled", 1458, 805817,
        "86f8a47fce441436db9abdfdfb14261cf90c43d99397425a9a34d62ce1d1ea71", -1, -1, 12)]
    [InlineData("evil", 42, 517451,
        "8cc542540d5f905e62f5e37229177bb2896061ef90e554f2b0e13f8b4d64974a", 1488, 199, 18)]
    [InlineData("evil", 1458, 448271,
        "50a9530405090fc2e34810dee975133a51e6017b34bc63d2b99a962ecbef0913", 1302, 199, 12)]
    [InlineData("sand", 42, 517451,
        "cc3975e02b56155b2d87064f97df8c24327794bc633e3855cf85c3b984b3bce9", 1488, 199, 18)]
    [InlineData("sand", 1458, 448271,
        "baa33beb90aba5038b8cb1c549a8f47064d755e4de4f1c9043e5efa2c7dadae6", 1302, 199, 12)]
    [InlineData("water", 42, 257321,
        "c8a77e126ecc205d150173c22d4895a38053fa168308e2d0b1278e75584f416c", 2988, 199, 18)]
    [InlineData("water", 1458, 448271,
        "c6cd4ef840f4aa4337be2adfe3fc0115a5c1d20c480391c26ecac07cf2dcac25", 1302, 199, 12)]
    public void Pass_matches_official(
        string fixture,
        int seed,
        int nextDraw,
        string worldHash,
        int anchorX,
        int anchorY,
        int logCells)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        var pass = new FallenLogPass1458(
            store, random, WorldSurface, BeachDistance, TestContext.Current.CancellationToken);
        pass.Apply();

        (int X, int Y) anchor = pass.Anchor ?? (-1, -1);
        string expected = $"{nextDraw}|{worldHash}|{anchorX},{anchorY}|{logCells}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}|{anchor.X},{anchor.Y}|{CountLogs(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool active = y >= GroundRow;
            store.Set(x, y, new WorldTile
            {
                Type = active ? (y == GroundRow ? (ushort)2 : (ushort)1) : (ushort)0,
                FrameX = active ? (short)0 : (short)-1,
                FrameY = active ? (short)0 : (short)-1,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }

        switch (fixture)
        {
            case "walled":
                for (int x = 1000; x < 1400; x++)
                for (int y = 150; y <= GroundRow; y++)
                {
                    WorldTile tile = store.Get(x, y);
                    tile.Wall = 2;
                    store.Set(x, y, in tile);
                }

                break;
            case "evil":
                for (int x = 900; x < 1100; x++)
                for (int y = GroundRow + 5; y < GroundRow + 25; y++)
                {
                    WorldTile tile = store.Get(x, y);
                    tile.Type = 25;
                    store.Set(x, y, in tile);
                }

                break;
            case "sand":
                for (int x = 600; x < 900; x++)
                {
                    WorldTile tile = store.Get(x, GroundRow);
                    tile.Type = 53;
                    store.Set(x, GroundRow, in tile);
                }

                break;
            case "water":
                for (int x = 3000; x < 3300; x++)
                {
                    WorldTile tile = store.Get(x, GroundRow - 1);
                    tile.LiquidAmount = byte.MaxValue;
                    store.Set(x, GroundRow - 1, in tile);
                }

                break;
        }

        return store;
    }

    private static int CountLogs(WorldTileStore store)
    {
        int count = 0;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.IsActive && tile.Type == 488)
                count++;
        }

        return count;
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
