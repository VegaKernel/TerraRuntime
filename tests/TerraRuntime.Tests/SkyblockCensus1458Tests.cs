using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class SkyblockCensus1458Tests
{
    public static IEnumerable<object[]> OriginalRows()
    {
        using var input = typeof(SkyblockCensus1458Tests).Assembly.GetManifestResourceStream("SkyblockCensus1458");
        using var gzip = new GZipStream(input!, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        Assert.Equal("4B87890AC53D40F61DB5F928693A379ACF4CCBD8ED3B47EB32FB096F145DF034", json.RootElement.GetProperty("officialSha256").GetString());
        Assert.Empty(json.RootElement.GetProperty("exceptions").EnumerateArray());
        foreach (string group in new[] { "rounding", "scenes" })
            foreach (var row in json.RootElement.GetProperty(group).EnumerateArray())
                yield return [group, row.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(OriginalRows))]
    public void Census_matches_original_load_live_scan_history_and_single_rounding(string group, string observation)
    {
        using var json = JsonDocument.Parse(observation);
        var row = json.RootElement;
        if (group == "rounding")
        {
            Assert.Equal(row.GetProperty("low").GetBoolean(), VanillaSkyblockRuntimePolicy1458.IsLowTiles(
                row.GetProperty("enabled").GetBoolean(), row.GetProperty("count").GetInt32(),
                row.GetProperty("width").GetInt32() * row.GetProperty("height").GetInt32()));
            return;
        }
        string scene = row.GetProperty("scene").GetString()!;
        var tiles = new WorldTileStore(new(128, 128));
        Assert.True(tiles.TryAttachWorldSurface(row.GetProperty("surface").GetDouble()));
        for (int x = 0; x < 128; x++)
            for (int y = 0; y < 128; y++)
            {
                bool active = scene switch
                {
                    "dense" or "actuated" => x >= 40 && x < 88 && y >= 40 && y < 88,
                    "border" or "retained" => x < 40 && y >= 40 && y < 88,
                    _ => x == 65 && y == 65
                };
                if (active) tiles.Set(x, y, new WorldTile
                {
                    Type = 1,
                    Flags = WorldTileFlags.Active |
                    (scene == "actuated" ? WorldTileFlags.Inactive : 0)
                });
            }
        int notifications = 0;
        var state = new ServerRuntimeState(worldTiles: tiles,
            townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458) with { SkyblockWorld = scene != "ordinary" },
            worldStateAdopted: () => notifications++);
        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
        bool current = row.GetProperty("initial").GetBoolean();
        Assert.True(graph.WorldTileAuthority.TryCaptureSkyblockLowTiles(out bool initial));
        Assert.Equal(current, initial);
        int prefix = 0, changes = 0;
        for (int cycle = 0; cycle < 2; cycle++)
        {
            for (int x = 0; x < 128; x++)
            {
                for (int tick = 0; tick < 29; tick++)
                {
                    graph.WorldTileAuthority.TickMechanisms();
                    Assert.True(graph.WorldTileAuthority.TryCaptureSkyblockLowTiles(out bool held));
                    Assert.Equal(current, held);
                }
                graph.WorldTileAuthority.TickMechanisms();
                Assert.True(graph.WorldTileAuthority.TryCaptureSkyblockLowTiles(out bool after));
                if (x != 127) Assert.Equal(current, after);
                if (current != after) changes++;
                current = after;
                if (cycle == 0 && x == 39 && scene == "retained")
                    for (int cx = 0; cx < 40; cx++)
                        for (int y = 40; y < 88; y++) tiles.Set(cx, y, default);
                if (x is 0 or 39 or 126) Assert.Equal(row.GetProperty("prefixes")[prefix++].GetBoolean(), current);
                Assert.Equal(changes, notifications);
            }
            Assert.Equal(row.GetProperty("cycles")[cycle].GetBoolean(), current);
        }
    }
}
