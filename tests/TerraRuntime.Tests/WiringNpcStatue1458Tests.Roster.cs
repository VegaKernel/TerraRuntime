using System.IO.Compression;
using System.Text.Json;

namespace TerraRuntime.Tests;

public sealed partial class WiringNpcStatue1458Tests
{
    public static IEnumerable<object[]> OriginalRosterRows()
    {
        using var input = typeof(WiringNpcStatue1458Tests).Assembly.GetManifestResourceStream(OperatingSystem.IsWindows() ? "WiringNpcStatueRosterWindows1458" : "WiringNpcStatueRosterLinux1458")!;
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalRosterRows))]
    public void Extended_statue_roster_matches_original(string observation) =>
        Statue_birth_population_replacement_pending_wire_and_rng_match_original(observation);

    public static IEnumerable<object[]> OriginalRosterConstructorRows()
    {
        foreach (bool windows in new[] { false, true })
        {
            using var input = typeof(WiringNpcStatue1458Tests).Assembly.GetManifestResourceStream(windows ? "WiringNpcStatueRosterWindows1458" : "WiringNpcStatueRosterLinux1458")!;
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var json = JsonDocument.Parse(gzip);
            var seen = new HashSet<(int Type, int Mode, int World, bool Good)>();
            foreach (var row in json.RootElement.GetProperty("rows").EnumerateArray())
            {
                string kind = row.GetProperty("kind").GetString()!;
                if (kind != "birth" && !kind.StartsWith("world", StringComparison.Ordinal)) continue;
                int mode = row.GetProperty("mode").GetInt32(), world = row.GetProperty("world").GetInt32();
                bool good = row.GetProperty("good").GetBoolean();
                foreach (var point in row.GetProperty("points").EnumerateArray())
                    foreach (var born in point.GetProperty("state").GetProperty("npcs").EnumerateArray())
                        if (born.GetProperty("statue").GetBoolean() && seen.Add((born.GetProperty("type").GetInt32(), mode, world, good)))
                            yield return [windows, mode, world, good, born.GetRawText()];
            }
        }
    }

    [Theory, MemberData(nameof(OriginalRosterConstructorRows))]
    public void Extended_roster_matches_both_original_constructor_arithmetics(bool windows, int mode, int mask, bool good, string observation) =>
        Both_original_platforms_match_shared_creation_rules(windows, mode, mask, good, observation);
}
