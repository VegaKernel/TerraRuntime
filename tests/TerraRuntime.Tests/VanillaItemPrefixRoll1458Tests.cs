using System.Security.Cryptography;
using System.Text;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Items;

namespace TerraRuntime.Tests;

/// <summary>
/// Differential comparison for TerrariaServer 1.4.5.8 <c>Item.Prefix(-1)</c> on the shared generation stream.
/// </summary>
/// <remarks>
/// Expectations come from running the unmodified method inside the pinned dedicated server, with
/// <c>WorldGen.isGeneratingOrLoadingWorld</c> raised so the roll reads <c>genRand</c>, over every item in
/// 1.4.5.8 that can receive a prefix at all - all 886 of them - on eight seeds each. Each case records both the
/// prefix the roll produced and the next value left on the stream, because the second is what every later
/// placement depends on and the first alone would not notice a roll that drew one value too many.
///
/// The cases are grouped by prefix family so a failure says which family is wrong rather than only that
/// something is. The accessory family is the one that proves the acceptance rule matters: its nineteen prefixes
/// change no stat at all, so every one of them is kept on the first try, while the sword family rerolls
/// constantly against items whose damage the multiplier rounds straight back.
/// </remarks>
public sealed class VanillaItemPrefixRoll1458Tests
{
    private const int MaxItemId = 6200;

    [Theory]
    [InlineData(0, 210, "c60887c02c5af3f8723835514e5ad0adb8cbc6191845b392220b54cdab768524")]
    [InlineData(1, 68, "e4aa3f97448f4073dc7dd4d721803841fccb8d0eade92c991e4147fb57d37ef1")]
    [InlineData(2, 92, "059fee32989c3759d30eb0df144eb175afc220e8b2ef769fdc3b79c02fab0c99")]
    [InlineData(3, 81, "e61ff4ab55c844ad52fb1fcebc0fa74c5ba8992e0f0812f316b155ca68f2695b")]
    [InlineData(4, 46, "a626d8f9c7af3232794519dacba158e2d3c0c152f97f659bf818c01c0bea9f29")]
    [InlineData(5, 41, "8bba2a90949480c214ba11366a9f2348cc85b91ff42d6680b1b91388f424fda3")]
    [InlineData(6, 1, "b690ed35ea07dd8acb690f507b4864ae3d0f7bda2c1980fa855d67dace7eac90")]
    [InlineData(7, 347, "08b462e5ae47d51b534cfe638d5a075f68c78b88ac1a41589976219567e14e56")]
    public void Roll_matches_official(int family, int itemCount, string outcomeHash)
    {
        var sb = new StringBuilder();
        int items = 0;
        for (int id = 1; id <= MaxItemId; id++)
        {
            var type = new ItemTypeId(id);
            if (!VanillaItemPrefixTable1458.TryGet(type, out VanillaItemPrefixRecord1458 record) ||
                record.Family != family)
                continue;

            items++;
            for (int seed = 0; seed < 8; seed++)
            {
                var random = new RandomAdapter(seed * 7919 + id);
                byte prefix = VanillaItemPrefixRoll1458.Roll(type, random);
                sb.Append(id).Append('|').Append(seed).Append('|').Append(prefix).Append('|')
                  .Append(random.Next(1000000)).Append(';');
            }
        }

        string expected = $"{itemCount}|{outcomeHash}";
        string actual = $"{items}|{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))}";
        Assert.True(expected == actual, $"official={expected} runtime={actual}");
    }

    /// <summary>
    /// An item outside the table draws nothing at all, which is how <c>CanHavePrefixes</c> refuses. A roller
    /// that drew first and refused afterwards would pass every hash above and still desynchronise the stream.
    /// </summary>
    [Fact]
    public void Item_without_a_family_draws_nothing()
    {
        // Dirt Block: stackable, places a tile, has no prefix family.
        Assert.False(VanillaItemPrefixTable1458.TryGet(new ItemTypeId(2), out _));

        var random = new RandomAdapter(1458);
        var untouched = new RandomAdapter(1458);
        Assert.Equal(0, VanillaItemPrefixRoll1458.Roll(new ItemTypeId(2), random));
        Assert.Equal(untouched.Next(1000000), random.Next(1000000));
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
