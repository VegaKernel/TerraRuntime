using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed partial class WiringNpcStatue1458Tests
{
    public static IEnumerable<object[]> OriginalConstructorRows()
    {
        foreach (bool windows in new[] { false, true })
        {
            using var input = typeof(WiringNpcStatue1458Tests).Assembly.GetManifestResourceStream(windows ? "WiringNpcStatueWindows1458" : "WiringNpcStatueLinux1458")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
            foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            {
                string kind = row.GetProperty("kind").GetString()!;
                if (row.GetProperty("seed").GetInt32() != 1 || row.GetProperty("colors").GetInt32() != 1 || kind != "birth" && !kind.StartsWith("world", StringComparison.Ordinal)) continue;
                var born = row.GetProperty("points")[0].GetProperty("state").GetProperty("npcs").EnumerateArray().First(n => n.GetProperty("statue").GetBoolean());
                yield return [windows, row.GetProperty("mode").GetInt32(), row.GetProperty("world").GetInt32(), row.GetProperty("good").GetBoolean(), born.GetRawText()];
            }
        }
    }

    [Theory, MemberData(nameof(OriginalConstructorRows))]
    public void Both_original_platforms_match_shared_creation_rules(bool windows, int mode, int mask, bool good, string observation)
    {
        using var json = JsonDocument.Parse(observation); var npc = json.RootElement;
        Assert.True(VanillaNpcDefinitionCatalog.TryGet(new(npc.GetProperty("type").GetInt32()), out var definition));
        var context = new VanillaNpcSpawnContext(mode + 1f + (good ? 1f : 0f), 0, good)
        {
            HardMode = (mask & 1) != 0, RemixWorld = (mask & 2) != 0, TenthAnniversaryWorld = (mask & 4) != 0,
            DownedPlantera = (mask & 8) != 0, EaterOfWorldsActive = (mask & 16) != 0, WallOfFleshActive = (mask & 32) != 0
        };
        Assert.True(VanillaNpcSpawnDefaults.TryResolve(in definition, in context, windows, out var actual));
        Assert.Equal(npc.GetProperty("width").GetInt32(), actual.Hitbox.Width); Assert.Equal(npc.GetProperty("height").GetInt32(), actual.Hitbox.Height);
        Assert.Equal(npc.GetProperty("scale").GetSingle(), actual.Scale); Assert.Equal(npc.GetProperty("lifeMax").GetInt32(), actual.LifeMax);
        Assert.Equal(npc.GetProperty("damage").GetInt32(), actual.Damage); Assert.Equal(npc.GetProperty("defense").GetInt32(), actual.Defense);
    }
}
