using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8 <c>GenPassNameID.Piles</c> pass.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server, and checking the next four shared RNG values and a
/// SHA-256 over every field of every cell.
///
/// The ground is a stack of floors twelve rows apart, so every sampled column finds ground quickly and the
/// descent loop always has somewhere to stop. The fixtures then vary the one thing each stage keys on: bands
/// of nineteen different floor materials so every arm of every cascade runs; walls, including the mud wall
/// the fifth stage re-rolls out of and a dungeon wall that rewrites the style; a world whose every floor is
/// one of the eleven identities the pass clears out of <c>Main.tileSolid</c> for its own duration; and a
/// flooded world with shimmer over half of it, because shimmer refuses a placement outright while ordinary
/// liquid only changes which style table is used.
///
/// The solidity fixture is the decisive one: it produces not a single pile of any size, which is the only way
/// to see an override that is otherwise invisible.
/// </remarks>
public sealed class PilePass1458Tests
{
    private const int Width = 1400;
    private const int Height = 900;
    private const double WorldSurface = 220.0;
    private const double RockLayer = 320.0;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, four next draws, worldHash
    // No ground at all: the draw count is the sampling structure alone, with no cascade reached.
    [InlineData("empty", 42, 764416, 755783, 912890, 151767, "deac91d38b9115d763fb28d51ba8af69d79ec198f2bb95e697bd99f8703d4fc6")]
    [InlineData("empty", 1458, 60724, 500674, 85228, 531810, "deac91d38b9115d763fb28d51ba8af69d79ec198f2bb95e697bd99f8703d4fc6")]
    // Plain dirt and stone floors: the common path through every stage.
    [InlineData("ground", 42, 485212, 609249, 972855, 573475, "1046f84074beeabbc13b2c7e68344464ba5d27bea685bf56d27995a112dc6dbe")]
    [InlineData("ground", 1458, 60673, 595526, 996319, 811253, "3a62c13e6062d37934bf94d15ef8f657afd1d859ff859935cb51312968d73dd0")]
    // Nineteen floor materials in bands, so every arm of every stage's style cascade runs, with walls.
    [InlineData("floors", 42, 7453, 964945, 350755, 454270, "8261b5432209b5b06f1ba7fb08910fbefd43a2842b658833194048a144b87af1")]
    [InlineData("floors", 1458, 962533, 894646, 296996, 225562, "d63443bd080b0a6ebf1eb7c69845e1ca164922a921fe2a957283d3a8a5bf9dea")]
    // Walls only: dirt, mud, the wall the fifth stage re-rolls out of, and a dungeon wall.
    [InlineData("walled", 42, 283757, 448655, 546354, 640295, "584bc7ef3e6faf155b37dec985015452c2773a48b742cb580b0c99825809f156")]
    [InlineData("walled", 1458, 156994, 512108, 776564, 109279, "6d56d61e313ca19f7310aa29abc2074f93033979513f0c378aba95bdc353e63a")]
    // Every floor is one of the ten plain identities the pass clears out of Main.tileSolid for its own
    // duration. Not one pile of any size is taken, which is the only way that override is visible.
    [InlineData("sky", 42, 983571, 308375, 44511, 596029, "020467ab728795a29423bf7db26dabc05e3d198c057347dc3112a432834530a4")]
    [InlineData("sky", 1458, 338966, 543166, 768401, 369270, "a0050cc0ff7c21b7c25e94d9a8113bde0326bc7f1a41426d80251e35c2023d3d")]
    // Flooded, with shimmer over half the map: shimmer refuses a placement outright, while ordinary
    // liquid only decides which style table the sand branch uses.
    [InlineData("wet", 42, 980618, 799028, 211105, 245585, "d64597d64fa4de9a71888ce0dc6934850950ddb8264413f0627e2c3817c26110")]
    [InlineData("wet", 1458, 194128, 250667, 853956, 999285, "fe090bddaef3e02d7aa0623b2b3279b502ccf288f8f749e450ad227527ade8ac")]
    public void Pass_matches_official(
        string fixture, int seed, int d0, int d1, int d2, int d3, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);

        new PilePass1458(store, random, WorldSurface, RockLayer, BeachDistance,
            TestContext.Current.CancellationToken).Apply();

        string expected = $"{d0}|{d1}|{d2}|{d3}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{random.Next(1000000)}|{random.Next(1000000)}|" +
            $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(string fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, new WorldTile { FrameX = -1, FrameY = -1 });

        ushort[] floors = fixture == "floors"
            ? [0, 1, 57, 147, 161, 60, 226, 53, 367, 368, 396, 2, 151, 70, 30, 19, 25, 203, 41]
            : [0, 1];

        // "empty" has no ground at all: nothing is ever placed, so the draws are purely the sampling loops
        // and the ocean re-rolls, which separates the pass's structure from its style cascades.
        if (fixture != "empty")
        {
            for (int y = 40; y < Height - 6; y += 12)
            for (int x = 0; x < Width; x++)
                store.Set(x, y, new WorldTile
                {
                    Flags = WorldTileFlags.Active, Type = floors[(x / 70) % floors.Length],
                    FrameX = -1, FrameY = -1
                });
        }

        if (fixture is "walled" or "floors")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;

                int band = (x / 100) % 4;
                tile.Wall = band == 0 ? (ushort)2 : band == 1 ? (ushort)40 : band == 2 ? (ushort)87 : (ushort)7;
                store.Set(x, y, in tile);
            }
        }

        // The identities the pass clears out of Main.tileSolid for its own duration.
        if (fixture == "sky")
        {
            // 484 is left out on purpose: it is a frame-important two-by-two boulder, so a single cell of it
            // is an invalid object and the framing destroys it, which tests the framer rather than
            // this pass's solidity override.
            ushort[] cleared = [379, 229, 190, 196, 189, 717, 718, 719, 202, 460];
            for (int y = 40; y < Height - 6; y += 12)
            for (int x = 0; x < Width; x++)
            {
                WorldTile tile = store.Get(x, y);
                tile.Type = cleared[(x / 60) % cleared.Length];
                store.Set(x, y, in tile);
            }
        }

        if (fixture == "wet")
        {
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                WorldTile tile = store.Get(x, y);
                if (tile.IsActive)
                    continue;

                tile.LiquidAmount = 255;
                tile.LiquidKind = x % 200 < 100 ? WorldLiquidKind.Shimmer : WorldLiquidKind.Water;
                store.Set(x, y, in tile);
            }
        }

        return store;
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
