using System.Globalization;
using System.IO.Compression;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Tests;

public sealed class NpcContactBuffRules1458Tests
{
    [Fact]
    public void Contact_effects_duration_and_rng_match_both_original_source_editions()
    {
        // Direct original Player.StatusFromNPC calls: all 694 identities, three difficulty modes,
        // frozen state, 16 seeds, plus carried-slime/immune/cold/good-world and negative-netID cases.
        // These numeric observations establish the helper boundary, not whole Player.Update parity.
        Span<NpcContactBuff1458> effects = stackalloc NpcContactBuff1458[VanillaNpcContactBuffs1458.MaximumEffects];
        foreach (bool windows in new[] { false, true })
        {
            string name = "NpcContactBuff" + (windows ? "Windows" : "Linux") + "1458";
            using Stream input = typeof(NpcContactBuffRules1458Tests).Assembly.GetManifestResourceStream(name)!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            int rows = 0;
            while (reader.ReadLine() is { } line)
            {
                int[] row = line.Split(',').Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var random = new ContactRandom(row[5]);
                // Main.Difficulty adds one difficulty tier for getGoodWorld, independently of GameMode.
                int difficulty = row[3] + row[8];
                int count = VanillaNpcContactBuffs1458.Evaluate(row[0], row[1], row[2], difficulty >= 1,
                    row[4] != 0, row[8] != 0, random, effects, windows);
                int offset = 9;
                if (row[7] == 0)
                {
                    for (int i = 0; i < count; i++)
                    {
                        Assert.Equal(row[offset++], effects[i].Type.Value);
                        Assert.Equal(row[offset++], VanillaNpcContactBuffs1458.ResolveDuration(effects[i].Type,
                            effects[i].DurationTicks, difficulty >= 1, difficulty >= 2, row[6] != 0));
                        Assert.True(VanillaNpcContactBuffs1458.IsContactBuffType(effects[i].Type));
                    }
                }
                Assert.Equal(0, row[offset++]);
                Assert.Equal(row[offset++], random.Draws);
                Assert.Equal(row[offset++], random.Next());
                Assert.Equal(row.Length, offset);
                rows++;
            }
            Assert.Equal(110016, rows);
        }
        Assert.True(VanillaNpcContactBuffs1458.IsSlimeContactOnly(1, 1125f));
        Assert.False(VanillaNpcContactBuffs1458.IsSlimeContactOnly(59, 1125f));
        Assert.False(VanillaNpcContactBuffs1458.IsContactBuffType(new BuffTypeId(5)));
    }

    private sealed class ContactRandom(int seed) : Random
    {
        private readonly VanillaUnifiedRandom1458 source = new(seed);
        public int Draws { get; private set; }
        public override int Next() { Draws++; return source.Next(); }
        public override int Next(int maxValue) { Draws++; return source.Next(maxValue); }
        public override int Next(int minValue, int maxValue) { Draws++; return source.Next(minValue, maxValue); }
    }
}
