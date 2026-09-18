using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.World;
using TerraRuntime.WorldGeneration.Vanilla;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for the registered TerrariaServer 1.4.5.8
/// <c>GenPassNameID.SpeleothemsAndGemTrees</c> pass: its whole-map scan, the speleothem atlas chosen by
/// substrate, and the gem trees it grows on the way.
/// </summary>
/// <remarks>
/// Expectations come from calling the unmodified registered delegate through <c>GenPass.Apply</c> on the same
/// synthetic ground inside the pinned dedicated server. Each fixture is a uniform band with one horizontal
/// cavity, so both speleothem forms - hanging from the cavity's ceiling and standing on its floor - are
/// exercised over a whole map, and the substrate that picks the atlas is varied one fixture at a time. Walled
/// and unwalled pairs exist because a background wall was the suspected gate on gem-tree growth; the probe
/// showed it is not, which is exactly why the pairs are kept.
/// </remarks>
public sealed class SpeleothemPass1458Tests
{
    private const int Width = 400;
    private const int Height = 700;
    private const double WorldSurface = 200.0;
    private const double RockLayer = 300.0;
    private const int GroundRow = 400;
    private const int CavityTop = 420;
    private const int CavityBottomExclusive = 430;
    private const int BeachDistance = 380;

    [Theory]
    // fixture, seed, nextDraw, worldHash
    [InlineData("stone-unwalled", 42, 979837, "cd1615822d2cfde90a680ccadca99718aabc9c8c9d76d86d646407da3b7a154c")]
    [InlineData("stone-unwalled", 1458, 656543, "2caff88cf2d708384c358f92c1895daf03d7308e91a994547b55bfa256f8043a")]
    [InlineData("snow-unwalled", 42, 506648, "584a7961acca85e93a858b2f453cc9a9732dc8422894edb57f787f5b0811bde0")]
    [InlineData("snow-unwalled", 1458, 658489, "d8ffefcb9d9b946dca77e60af2fdaad03a52035ef9e576ff3a0cddeefbfbe302")]
    [InlineData("ebonstone-unwalled", 42, 647385, "1ba9f3f9524e61b8ad723174ccf082c4d6ebffed5211c33d3dce0b45c4b9dce1")]
    [InlineData("ebonstone-unwalled", 1458, 662593, "23ca011b36528b096c6a79484a8ecaaf3883607604b31eb092b3d48b4059f283")]
    [InlineData("sandstone-unwalled", 42, 506648, "6978151cc210e33cee2fa02ec6b57752712515c801a479a63cca065d8ce7155e")]
    [InlineData("sandstone-unwalled", 1458, 658489, "c6f0c4606941821215ddb93124818d42160c2e159ac5d1d8aa4de4cdeeed27bd")]
    [InlineData("granite-unwalled", 42, 506648, "bc0dc3ba7f1869dcef589be85c5158dccacaf5743e8e879ef9c04ee21edf2a0c")]
    [InlineData("granite-unwalled", 1458, 658489, "f3ec3b09576d9ad13960df8d8630418f483c2040f782ba7401b4bbe6e9df3750")]
    [InlineData("marble-unwalled", 42, 506648, "8d1383a3257909077b19d909161b5e680dc6943a6e33899dde1283c8634d0904")]
    [InlineData("marble-unwalled", 1458, 658489, "7c04351a5e5d6480666c12398d0a7b7347b621287648d658397f5a6a7904c390")]
    [InlineData("hive-unwalled", 42, 736026, "1e9be33be36a094a388e9dd54def70d8d9cc2f0b6c7f8739ea46d98a7b180973")]
    [InlineData("hive-unwalled", 1458, 663860, "6d5a248ba5968a51b366c6097e034f11b88104069a8c4e7862d145e487b31d69")]
    [InlineData("moss-unwalled", 42, 979837, "14e60b86ccb339d25b2496d6e70b94eaa9626c0006cecd5c2fdb4e31fc8bc090")]
    [InlineData("moss-unwalled", 1458, 656543, "8c7ffca53818c7a0cea34b5238ea9e2389a90d00ec9427f0d74df26c698aa16f")]
    [InlineData("stone-walled", 42, 979837, "990695665ad25a3b175f1051999a1dab111804f4da97fbfbfe063d2201da036e")]
    [InlineData("stone-walled", 1458, 656543, "28dcd601cbb31b91fdb2cb1e660f6eb08b39c15052badfd0e61577d9306e277e")]
    [InlineData("ebonstone-walled", 42, 647385, "54066e3ef07092a159be2ee3d5ad7bdd091f122548520c15a3567118d6c992b3")]
    [InlineData("ebonstone-walled", 1458, 662593, "59d5202c8723fd6e5c8c72afd6cd276acec8686e7a0a86558173aa82a2f0cc95")]
    [InlineData("crimstone-walled", 42, 647385, "e4185901dac587acaf93d86f88f84457036ffcdcb753c3bf697bb9a30987e520")]
    [InlineData("crimstone-walled", 1458, 662593, "206e34dbfc3b48552ee8956190253c2bf9e7f8451a4114dc235bbbd8ad1f29d0")]
    [InlineData("pearlstone-walled", 42, 647385, "af24c9cc54c862dc144cd054be5d43efd88e9827b020ef459bef491046bcbdbf")]
    [InlineData("pearlstone-walled", 1458, 662593, "5b2d7faeb718b0553f47abd4dd06b36688eddbef8dbd7ccb439d7469113ea87b")]
    // Above worldSurface, where only the pass's second walk runs: ice and the two evil stones are offered a
    // speleothem there and ordinary stone is not, which is what makes the stone case a refusal control.
    [InlineData("snow-above-surface", 42, 201099, "0eeb67f168461eb5861fa392b619f527f2d887003a3ace20a7eaefe5986add3a")]
    [InlineData("snow-above-surface", 1458, 490646, "3a67f63ffbf6e4884b4756aeaa1d39c355676dd8bae6676942437888b34f0a23")]
    [InlineData("ebonstone-above-surface", 42, 194001, "8d8be809c21aefd28810c1f0fab58152f83e2d2cfa80a1312f06551f8a2e17e0")]
    [InlineData("ebonstone-above-surface", 1458, 670519, "f9871e27b5d71321ee5964e65844fa959460c7d1822cdcd4eaf01450b20c173b")]
    [InlineData("crimstone-above-surface", 42, 194001, "b44684b351eda3ee94d42220f0200f58bf0f3838304061d265d235d609321e9d")]
    [InlineData("crimstone-above-surface", 1458, 670519, "fc376db70fcd8170a8037c1dd990cb289dc11c12f77516c1ab87720a005de079")]
    [InlineData("stone-above-surface", 42, 747700, "0da8ffc06ee7df075179027768438353d1358a7719b004d4f1cc67599bb690b4")]
    [InlineData("stone-above-surface", 1458, 560358, "0da8ffc06ee7df075179027768438353d1358a7719b004d4f1cc67599bb690b4")]
    public void Pass_matches_official(string fixture, int seed, int nextDraw, string worldHash)
    {
        (ushort substrate, ushort wall) = Fixture(fixture);
        WorldTileStore store = CreateStore(substrate, wall, fixture.EndsWith("above-surface", StringComparison.Ordinal));
        var random = new RandomAdapter(seed);

        new SpeleothemPass1458(
            store,
            random,
            WorldSurface,
            RockLayer,
            BeachDistance,
            TestContext.Current.CancellationToken).Apply();

        string expected = $"{nextDraw}|{worldHash}";
        string actual = $"{random.Next(1000000)}|{Hash(store)}";
        Assert.True(expected == actual, $"official={expected} runtime={actual} histogram={Histogram(store)}");
    }

    private static (ushort Substrate, ushort Wall) Fixture(string name) => name switch
    {
        "stone-unwalled" => (1, 0),
        "snow-unwalled" => (147, 0),
        "ebonstone-unwalled" => (25, 0),
        "sandstone-unwalled" => (396, 0),
        "granite-unwalled" => (368, 0),
        "marble-unwalled" => (367, 0),
        "hive-unwalled" => (225, 0),
        "moss-unwalled" => (179, 0),
        "stone-walled" => (1, 2),
        "ebonstone-walled" => (25, 2),
        "crimstone-walled" => (203, 2),
        "pearlstone-walled" => (117, 2),
        "snow-above-surface" => (147, 0),
        "ebonstone-above-surface" => (25, 0),
        "crimstone-above-surface" => (203, 0),
        "stone-above-surface" => (1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // Deterministic synthetic input shared verbatim with the official probe.
    private static WorldTileStore CreateStore(ushort substrate, ushort wall, bool aboveSurface)
    {
        var store = new WorldTileStore(new WorldDimensions(Width, Height));
        int groundRow = aboveSurface ? 100 : GroundRow;
        int cavityTop = aboveSurface ? 150 : CavityTop;
        int cavityBottom = aboveSurface ? 160 : CavityBottomExclusive;
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            bool inCavity = y >= cavityTop && y < cavityBottom;
            bool active = y >= groundRow && !inCavity;
            store.Set(x, y, new WorldTile
            {
                Type = active ? substrate : (ushort)0,
                Wall = inCavity ? wall : (ushort)0,
                FrameX = active ? (short)0 : (short)-1,
                FrameY = active ? (short)0 : (short)-1,
                Flags = active ? WorldTileFlags.Active : WorldTileFlags.None
            });
        }

        return store;
    }

    private static string Histogram(WorldTileStore store)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int x = 0; x < Width; x++)
        for (int y = 0; y < Height; y++)
        {
            WorldTile tile = store.Get(x, y);
            if (!tile.IsActive)
                continue;

            string? key = tile.Type switch
            {
                165 => $"165:{tile.FrameX}/{tile.FrameY}",
                >= 583 and <= 590 => tile.Type.ToString(),
                _ => null
            };

            if (key is null)
                continue;

            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<string, int> entry in counts)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(entry.Key).Append('=').Append(entry.Value);
        }

        return sb.Length == 0 ? "none" : sb.ToString();
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
