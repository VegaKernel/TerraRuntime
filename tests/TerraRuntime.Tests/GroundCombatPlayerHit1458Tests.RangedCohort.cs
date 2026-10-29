using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;

namespace TerraRuntime.Tests;

public sealed partial class GroundCombatPlayerHit1458Tests
{
    public static IEnumerable<object[]> RangedCases()
    {
        foreach (string name in new[] { "RangedCohortPlayerHitLinux1458.v1", "RangedCohortPlayerHitModesLinux1458.v1", "RangedLegacyArrowPlayerHitLinux1458.v1" })
        {
            using var stream = typeof(GroundCombatPlayerHit1458Tests).Assembly.GetManifestResourceStream(
                "TerraRuntime.Tests.Fixtures." + name + ".json.gz")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.Clone()];
        }
    }

    [Theory, MemberData(nameof(RangedCases))]
    public void Ranged_projectile_hit_matches_actual_original_status_debit_and_shared_entropy(JsonElement row)
    {
        int type = row.GetProperty("type").GetInt32(), seed = row.GetProperty("seed").GetInt32();
        string scene = row.GetProperty("scene").GetString()!;
        var before = row.GetProperty("before");
        int mode = row.TryGetProperty("mode", out var m) ? m.GetInt32() : 0;
        bool good = row.TryGetProperty("goodWorld", out var g) && g.GetBoolean();
        using var f = new Fixture(type, seed, scene, mode: mode, goodWorld: good,
            damage: before.GetProperty("projectile").GetProperty("damage").GetInt32());
        Assert.Equal(F(before.GetProperty("projectile"), "position", 0), f.Shot.PositionX);
        Assert.Equal(F(before.GetProperty("projectile"), "position", 1), f.Shot.PositionY);
        AssertRandom(before.GetProperty("player").GetProperty("rng"), f.Random);
        f.Pass.Tick([]);
        var expected = row.GetProperty("afterCaller");
        var player = expected.GetProperty("player").GetProperty("player");
        if (scene == "inactive") Assert.False(f.Players.TryCapture(f.Connection.Player, out _));
        else
        {
            Assert.True(f.Players.TryCapture(f.Connection.Player, out var actual));
            Assert.Equal(player.GetProperty("statLife").GetInt32(), actual.Life);
            Assert.Equal(player.GetProperty("dead").GetBoolean(), actual.IsDead);
            foreach (int buff in new[] { 31, 144 })
                Assert.Equal(Duration(player, buff), f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(buff)));
        }
        Assert.Equal(expected.GetProperty("projectile").GetProperty("active").GetBoolean(),
            f.Store.TryGet(f.Shot.Handle, out _));
        // Existing KillMe/tombstone entropy remains separately unqualified; never infer an offset.
        if (scene != "lethal") AssertRandom(expected.GetProperty("player").GetProperty("rng"), f.Random);
    }

    [Theory]
    [InlineData(435, 300)]
    [InlineData(437, 480)]
    public void Electrified_is_committed_before_health_callback_and_disconnect_retires_stale_notification(int type, int duration)
    {
        using var f = new Fixture(type, 0, "hit");
        int observed = 0;
        f.Events.OnHealth = () =>
        {
            observed++;
            Assert.Equal(duration, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(144)));
            f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection));
        };
        f.Pass.Tick([]);
        Assert.Equal(1, observed);
        Assert.Empty(f.Events.Published);
        Assert.False(f.Store.TryGet(f.Shot.Handle, out _));
        var after = f.Random.Clone();
        f.Pass.Tick([]);
        Assert.True(after.HasSameState(f.Random));
    }
}
