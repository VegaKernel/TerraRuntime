using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>WorldGen.GrowLivingTree</c>: the trunk, its branches,
/// the crown, the roots and the canopy with its litter.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified official method on the same synthetic ground inside the pinned
/// dedicated server. Fixture three opens a cavity directly under the trunk base, because the source only digs
/// its underground passage when the twenty rows below the tree are solid and unwalled; that isolates the tree
/// itself. Fixtures one and two leave the ground solid so the passage runs, and they are asserted only in patch
/// mode, where the source opens no secret room and therefore never reaches <c>AddBuriedChest</c> - the one part
/// of a living tree that is still not ported.
/// </remarks>
public sealed class LivingTreeGrower1458Tests
{
    private const int Width = 900;
    private const int Height = 700;
    private const int SurfaceRow = 400;
    private const int AnchorX = 450;

    [Theory]
    // fixture, patch, seed, nextDraw, worldHash
    [InlineData(3, 0, 42, 42603, "0d65d3eff9368653a191d9f13cc36d87b753b7915ec0848b652ac80ad78ba652")]
    [InlineData(3, 1, 42, 821956, "0a4869ce5b3d40a1eb2557163f3f744dbfb7566814327847589a5fb810614d68")]
    [InlineData(3, 0, 1458, 952940, "3b9881c992ca9cfbe1a3d7e980b300725b692aba36f51fa05312d73e6140559d")]
    [InlineData(3, 1, 1458, 701245, "f37de953030dea53efbb12bcd4a5124ae5a6095bcdb2b9c396197490e7960506")]
    [InlineData(3, 0, 8675309, 155631, "bf1bd5a9119c8a512237d6a20884e8c75d32617aeb0f9310df4fd172c833c332")]
    [InlineData(3, 1, 8675309, 481349, "386f3220a4c791b78dc4c252e66f4c8ae260e2319bc6766ebbad613c7b80e1cc")]
    // Solid ground: the underground passage and, in patch mode, its horizontal tunnels run in full.
    [InlineData(1, 1, 42, 168939, "7fa9fa35633e7bd2414aa791e5ddac76900ff37e1283e0f836f25e7f65b77fa5")]
    [InlineData(1, 1, 1458, 99963, "798ef47f87c74181ccb52f6528311e8fa006fa1e2b47ecc83f8546a9b3b5f5cf")]
    [InlineData(1, 1, 8675309, 189976, "bd342f8ba2d56b5f337812068b47c536e66c42e4e21f690052185058ffbc4477")]
    // The same, with a stone shelf the roots and the shaft both have to drive through.
    [InlineData(2, 1, 42, 168939, "38885a985e0fb469a1b15aeb2165c7a7506b8d6a0b2e08baff9fd99d427aab38")]
    [InlineData(2, 1, 1458, 99963, "ab38ed529c12eeeb845a33de63b861e5aa3e7d05950b00aea5d7c64564f9921f")]
    [InlineData(2, 1, 8675309, 189976, "e9b94d391c35d7c2a12abefe845a9ca90441c161cb4bd571607644bc1011361b")]
    public void Tree_matches_official(int fixture, int patch, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var grower = new LivingTreeGrower1458(store, random, 450d, Height - 200, TestContext.Current.CancellationToken);

        Assert.True(grower.TryGrow(AnchorX, SurfaceRow - 1, patch == 1));

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        if (expected != actual && fixture == 1 && patch == 1 && seed == 42)
        {
            using var writer = new StreamWriter(Path.Combine(Path.GetTempPath(), "ltree-runtime.txt"));
            for (int y = 200; y < 700; y++)
            for (int x = 300; x < 620; x++)
            {
                WorldTile t = store.Get(x, y);
                if (!t.IsActive && t.Wall == 0) continue;
                writer.WriteLine($"{x} {y} {(t.IsActive ? 1 : 0)} {t.Type} {t.Wall} {t.FrameX} {t.FrameY}");
            }
        }

        Assert.True(expected == actual, $"official={expected} runtime={actual} histogram={Histogram(store)}");
    }

    [Fact]
    public void A_site_without_solid_ground_is_refused_without_drawing()
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        var random = new RandomAdapter(42);
        var grower = new LivingTreeGrower1458(store, random, 450d, Height - 200, TestContext.Current.CancellationToken);

        Assert.False(grower.TryGrow(AnchorX, SurfaceRow - 1));
        Assert.Equal(new RandomAdapter(42).Next(), random.Next());
    }

    private static WorldTileStore CreateStore(int fixture)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
            store.Set(x, y, Fixture(fixture, x, y));
        return store;
    }

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTile Fixture(int fixture, int x, int y)
    {
        bool active = y >= SurfaceRow;
        ushort type = y == SurfaceRow ? (ushort)2 : (ushort)0;
        if (y > SurfaceRow + 60)
            type = 1;
        // Fixture two buries a stone shelf the roots have to drive through.
        if (fixture == 2 && y >= SurfaceRow + 8 && y < SurfaceRow + 12)
            type = 1;
        // Fixture three's cavity, which suppresses the underground passage.
        if (fixture == 3 && y >= SurfaceRow + 6 && y < SurfaceRow + 12 && x >= 440 && x <= 460)
            active = false;

        return new WorldTile
        {
            Type = type,
            FrameX = active ? (short)0 : (short)-1,
            FrameY = active ? (short)0 : (short)-1,
            Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
        };
    }

    private static string Histogram(WorldTileStore store)
    {
        var counts = new SortedDictionary<int, int>();
        int walls = 0;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (tile.Wall != 0) walls++;
            if (!tile.IsActive || tile.Type is 0 or 1 or 2) continue;
            counts.TryGetValue(tile.Type, out int n);
            counts[tile.Type] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<int, int> entry in counts)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(entry.Key).Append('=').Append(entry.Value);
        }

        sb.Append(" walls=").Append(walls);
        return sb.ToString();
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
