using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
namespace TerraRuntime.Tests;
public sealed class OnlyShimmerOceanWorlds1458Tests
{
    [Theory]
    [MemberData(nameof(Rows))]
    public void All_original_seed_flag_combinations_match_owned_projection(int mask, bool expected)
    {
        var facts = default(RuntimeTownCommerceWorldFacts1458) with {
            DrunkWorld=(mask&1)!=0, TenthAnniversaryWorld=(mask&2)!=0,
            RemixWorld=(mask&4)!=0, ZenithWorld=(mask&8)!=0, NotTheBeesWorld=(mask&16)!=0 };
        Assert.Equal(expected, facts.OnlyShimmerOceanWorlds);
    }
    public static IEnumerable<object[]> Rows()
    {
        using var stream = typeof(OnlyShimmerOceanWorlds1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.npc-only-shimmer-ocean-worlds-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach(var row in document.RootElement.EnumerateArray())
            yield return [row.GetProperty("mask").GetInt32(),row.GetProperty("value").GetBoolean()];
    }
}
