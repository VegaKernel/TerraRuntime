using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>WorldGen.Pyramid</c>.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified official method on the same synthetic desert inside the pinned
/// dedicated server. Each row checks the next shared-RNG draw and a hash over every cell in the world, so the
/// wedge, the interior walls, the sand-capped entry, the staircase, the treasure chamber, its decoration and the
/// exit tunnel all have to agree. This closes the method on synthetic input; it is not a claim about the real
/// generated prefix.
/// </remarks>
public sealed class PyramidBuilder1458Tests
{
    private const int Width = 1600;
    private const int Height = 1500;
    private const int SurfaceRow = 200;
    private const int AnchorX = 800;

    [Theory]
    // fixture, seed, nextDraw, worldHash. A shallow depth ends the descent before it can open a treasure
    // chamber, so these cases cover the wedge, the interior walls, the sand-capped entry, the staircase and the
    // exit tunnel with no chest in the way.
    [InlineData(1, 42, 450335, "ab68f4866a049ee11a51f10b6f754f7324f67ba05023f53887b80c100d54cf97")]
    [InlineData(1, 1458, 864266, "4f531f5681b67f3b8b81025a75405121d177c56be12769ec9409b9b9d6c1933a")]
    [InlineData(2, 42, 450335, "326141423f256b4711dd2c12c5da41b2e5fd13922b478833ce6591458dabf142")]
    [InlineData(2, 1458, 864266, "33e0ecb96774f945051fa6a110d114f57bf3dd2eaa8827c8fccecb0f95b3efaf")]
    [InlineData(3, 42, 450335, "997ccde156f9881343722d1957ea0a43f6d7df2d099aee40fb8918b60aadfa09")]
    [InlineData(3, 1458, 864266, "475242d16dcedcc85149530b83dd90e2e40f6bfa8453a311432926b2e85f8328")]
    public void Geometry_matches_official(int fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var builder = new PyramidBuilder1458(store, random, TestContext.Current.CancellationToken);

        Assert.Null(builder.TryBuild(AnchorX, SurfaceRow, minimumDepth: 20, maximumDepth: 26));

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual} histogram={Histogram(store)}");
    }

    [Theory]
    // Shallow and no-tunnel together isolate the wedge, the interior walls, the sand-capped entry cut and the
    // staircase, with neither a treasure chamber nor an exit tunnel in the way.
    [InlineData(1, 42, 980408, "528bb3c031439a9c475773a6e7c815eab5c39b521c0b0e35a4524554483497ee")]
    [InlineData(1, 1458, 272557, "0862c132c2cf803c60d9b9bb681df2525ddd6ccb95bfb47bf849e76aaede6ea1")]
    [InlineData(2, 42, 980408, "c18e9d98f2ce0e4957b454a496d8d380cc66ee2c7f2cc3b24946b6fcccea312d")]
    [InlineData(2, 1458, 272557, "9d3fc81c85490cf89383a2873afa93b0fd67bfb59c8ced5b16a6358d9c4653e9")]
    [InlineData(3, 42, 980408, "d51c28d9bbe7e34a72883cd61b46a20af93cb1d12a9945fa21217e499af4bdad")]
    [InlineData(3, 1458, 272557, "e7574cf4d3e2a13f5abf8781b59ac279c2cd6273a6133d886e9e7d3c3976695d")]
    public void Wedge_walls_entry_and_staircase_match_official(
        int fixture, int seed, int nextDraw, string worldHash)
    {
        WorldTileStore store = CreateStore(fixture);
        var random = new RandomAdapter(seed);
        var builder = new PyramidBuilder1458(store, random, TestContext.Current.CancellationToken);

        Assert.Null(builder.TryBuild(AnchorX, SurfaceRow, minimumDepth: 20, maximumDepth: 26, noTunnel: true));

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual} histogram={Histogram(store)}");
    }

    [Fact]
    public void An_existing_pyramid_at_the_anchor_is_refused_without_touching_a_cell()
    {
        WorldTileStore store = CreateStore(1);
        var brick = new WorldTile { Type = 151, Flags = WorldTileFlags.Active };
        store.Set(AnchorX, SurfaceRow, in brick);
        string before = Hash(store);

        var random = new RandomAdapter(42);
        var builder = new PyramidBuilder1458(store, random, TestContext.Current.CancellationToken);
        Assert.Null(builder.TryBuild(AnchorX, SurfaceRow));
        Assert.Equal(before, Hash(store));
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
        ushort type = 53;
        if (fixture >= 2 && y >= SurfaceRow + 120)
            type = 397;
        if (fixture == 3 && y >= SurfaceRow + 40 && y < SurfaceRow + 44 && (x / 40) % 2 == 0)
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
            if (tile.Wall == 34) walls++;
            if (!tile.IsActive || tile.Type is 53 or 397) continue;
            counts.TryGetValue(tile.Type, out int n);
            counts[tile.Type] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<int, int> entry in counts)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(entry.Key).Append('=').Append(entry.Value);
        }

        sb.Append(" wall34=").Append(walls);
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
