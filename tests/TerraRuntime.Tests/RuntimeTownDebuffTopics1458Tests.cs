using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeTownDebuffTopics1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(RuntimeTownDebuffTopics1458Tests).Assembly.GetManifestResourceStream("TownDebuffTopics1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Actual_ProbeDebuffs_order_and_short_circuit_next_rng_match(string json)
    {
        using var doc = JsonDocument.Parse(json); var row = doc.RootElement;
        int mask = row.GetProperty("mask").GetInt32();
        var source = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var random = new NpcRuntimeTownScheduleRandom1458(new SystemVanillaNpcRandom(source));
        var flags = new PlayerDebuffSnapshot1458((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0);
        var npc = new PlayerDebuffSnapshot1458((mask & 16) != 0, false, (mask & 32) != 0);
        var zones = new PlayerZoneSnapshot1458 { Zone1 = (byte)((mask & 8) != 0 ? 16 : 0) };
        Span<byte> topics = stackalloc byte[4];
        Assert.True(RuntimeTownNpcSchedule1458.TryCopySocialDebuffTopics((mask & 64) != 0 ? 4821 : 621,
            500, flags, zones, npc, row.GetProperty("held").GetInt32(), random, topics, out int count));
        Assert.Equal(row.GetProperty("list").EnumerateArray().Select(x => x.GetByte()), topics[..count].ToArray());
        Assert.Equal(row.GetProperty("next").GetInt32(), source.Next());
    }

    [Fact]
    public void Unknown_selected_flags_zones_and_inventory_are_not_replaced_by_clear_defaults()
    {
        var random = new NpcRuntimeTownScheduleRandom1458(new SystemVanillaNpcRandom(1458));
        Span<byte> topics = stackalloc byte[4];
        Assert.False(RuntimeTownNpcSchedule1458.TryCopySocialDebuffTopics(621, 500, null, default(PlayerZoneSnapshot1458), default, 0, random, topics, out _));
        Assert.False(RuntimeTownNpcSchedule1458.TryCopySocialDebuffTopics(621, 500, default(PlayerDebuffSnapshot1458), null, default, 0, random, topics, out _));
        Assert.False(RuntimeTownNpcSchedule1458.TryCopySocialDebuffTopics(621, 500, default(PlayerDebuffSnapshot1458), default(PlayerZoneSnapshot1458), default, null, random, topics, out _));
        Assert.True(RuntimeTownNpcSchedule1458.TryCopySocialDebuffTopics(4821, 500, null, null, new(true, false, true), 215, random, topics, out int count));
        Assert.Contains((byte)9, topics[..count].ToArray()); Assert.Contains((byte)8, topics[..count].ToArray());
    }
}
