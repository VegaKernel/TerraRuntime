using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class SlimeContainedGenerator1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(SlimeContainedGenerator1458Tests).Assembly.GetManifestResourceStream("SlimeContainedGenerator1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray())
            if (row.GetProperty("netMode").GetInt32() == 2)
                yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Dedicated_server_generator_matches_original_full_item_table_and_next_draw(JsonElement row)
    {
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        int actual = VanillaSlimeContainedInitializer1458.GenerateServerItem(random,
            row.GetProperty("balloon").GetBoolean(), row.GetProperty("low").GetBoolean(),
            row.GetProperty("moon").GetInt32(), row.GetProperty("depth").GetInt32() > 200,
            row.GetProperty("hard").GetBoolean());
        Assert.Equal(row.GetProperty("item").GetInt32(), actual);
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
    }
}
