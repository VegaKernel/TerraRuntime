using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.CaveWallVariety</c>
/// pass ("Wall Variety").
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next shared RNG and a SHA-256 over
/// every field of every cell.
///
/// The ground is solid stone under a bare sky with a dense grid of nine-by-nine pockets carved into it, dense
/// because the pass samples random points and needs to find candidates quickly. Four bands of columns carry
/// the four outcomes: an ordinary pocket that is papered, a pocket with snow against its ceiling that is
/// refused for touching it, a five-by-five pocket refused for being under fifty-one cells, and one cavity
/// larger than the flood's own budget, which never closes and is refused for that.
///
/// Every band still leaves stone with open space above it, and that is not incidental. The pass budgets only a
/// candidate that FAILS its checks; a sample that was never a candidate costs nothing, so ground with no stone
/// under open space anywhere makes the official delegate loop forever. The first version of this fixture
/// turned all the stone to snow and hung the probe.
///
/// The quota scales with world area against a Small world, so this eight-hundred-row world paints 47 pockets -
/// and each nine-by-nine pocket comes out as an eleven-by-eleven block, 5687 cells in total, because the
/// painting covers the pocket plus one ring and is performed one row below where the flood found it.
/// </remarks>
public sealed class CaveWallVarietyPass1458Tests
{
    private const int Width = 1000;
    private const int Height = 800;
    private const double WorldSurface = 200.0;
    private const double RockLayer = 300.0;
    private const int LavaLine = 500;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    // Pockets across all three depth bands: rock wall above the rock layer, the lava family at or below the
    // lava line, the deeper rock family between them.
    [InlineData("caves", 42, 532293, "0c4973e90c9592049f6ca9df7d8f287dbc5099f5b1b7d284d87c4b90d11b1321")]
    [InlineData("caves", 1458, 18421, "45b17bc16a13b2087a560214a10028b6e04945625585e163b54a8562153b7cb3")]
    // Jungle grass under every pocket short-circuits the depth test entirely and takes the jungle family.
    [InlineData("jungle", 42, 532293, "8b9d939616d4db1f522a68d93334a4baa0c69c9e414cb961f58e8178e768e17c")]
    [InlineData("jungle", 1458, 18421, "41586a2c389f5644579a9210910700a13cf0984528d0c773fcc19498dc18b16a")]
    // Only above the rock layer, and only at or below the lava line, so each family is isolated.
    [InlineData("shallow", 42, 109739, "057e5921d2a49a924e74ce56bcadafc2408d52b5615dd36e41b08e6d47126fa6")]
    [InlineData("shallow", 1458, 467956, "832a66c5721481748a1d71b2115699fbd1a91acc9de642ae0a13da89762c9aa3")]
    [InlineData("deep", 42, 163792, "5bc38846cb9c4a21036bd7b7cd47a03429d5d102fc96224d15898abae3d5bac3")]
    [InlineData("deep", 1458, 499269, "bbd394baf45bb1660bfdc97591da19e2ff40f4ec1ca2f7b223aa3bf3117e85aa")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new CaveWallVarietyPass1458(
                store, random, WorldSurface, RockLayer, LavaLine,
                new WorldGenerationPoint(0, 0), TestContext.Current.CancellationToken)
            .Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        bool jungleFloors = fixture == "jungle";
        int gridTop = fixture == "deep" ? 510 : 210;
        int gridBottom = fixture == "shallow" ? 285 : 600;

        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            var tile = new WorldTile();
            if (y < 200)
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

        for (int px = 100; px < 900; px += 15)
        {
            if (px >= 780 && px < 880)
                continue;

            bool snowCeiling = px >= 420 && px < 520;
            bool tooSmall = px >= 660 && px < 760;
            int size = tooSmall ? 5 : 9;
            for (int py = gridTop; py < gridBottom; py += 15)
            {
                Carve(store, px, px + size, py, py + size);
                if (snowCeiling)
                    Retype(store, px - 1, py - 1, 147);
                if (jungleFloors)
                    for (int x = px; x < px + size; x++)
                        Retype(store, x, py + size, 60);
                if (px == 880 || px == 895)
                {
                    ushort papered = px == 880 ? (ushort)87 : (ushort)224;
                    for (int x = px; x < px + size; x++)
                    for (int y = py; y < py + 5; y++)
                        Paper(store, x, y, papered);
                }
            }
        }

        Carve(store, 790, 830, gridTop + 60, gridTop + 90);
        Carve(store, 2, 11, gridTop, gridTop + 9);
        for (int y = gridTop - 1; y < gridTop + 12; y++)
            Paper(store, 0, y, 2);
        return store;
    }

    private static void Carve(WorldTileStore store, int left, int right, int top, int bottom)
    {
        for (int x = left; x < right; x++)
        for (int y = top; y < bottom; y++)
        {
            WorldTile tile = store.Get(x, y);
            tile.Flags &= ~WorldTileFlags.Active;
            tile.FrameX = -1;
            tile.FrameY = -1;
            store.Set(x, y, tile);
        }
    }

    private static void Paper(WorldTileStore store, int x, int y, ushort wall)
    {
        WorldTile tile = store.Get(x, y);
        tile.Wall = wall;
        store.Set(x, y, tile);
    }

    private static void Retype(WorldTileStore store, int x, int y, ushort type)
    {
        if ((uint)x >= Width || (uint)y >= Height)
            return;

        WorldTile tile = store.Get(x, y);
        tile.Flags |= WorldTileFlags.Active;
        tile.Type = type;
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
