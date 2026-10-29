using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RangedCohortProjectileActors1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(RangedCohortProjectileActors1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.RangedCohortPlayerBoundariesLinux1458.v1.json.gz")!;
        using var zip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(zip);
        foreach (var row in document.RootElement.GetProperty("rows").EnumerateArray()) yield return [row.Clone()];
    }

    [Theory, MemberData(nameof(Cases))]
    public void Real_authority_actor_matches_original_local_update_contact_and_lifetime_boundaries(JsonElement row)
    {
        using var f = new GroundCombatPlayerSubupdates1458Tests.Fixture(row);
        Assert.True(f.Graph.Projectiles.TryTickState());
        f.Graph.ProjectilePlayerCombat.Tick(f.Graph.Projectiles.PendingExplosions);
        Assert.True(f.World.TryCapturePlayerSnapshot(f.Connection.Player, out var player));
        var after = row.GetProperty("afterUpdate");
        var expected = after.GetProperty("player").GetProperty("player");
        Assert.Equal(expected.GetProperty("statLife").GetInt32(), player.Life);
        Assert.Equal(expected.GetProperty("dead").GetBoolean(), player.IsDead);
        foreach (int type in new[] { 31, 144 })
            Assert.Equal(Duration(expected, type), f.Graph.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(type)));
        var state = after.GetProperty("projectile");
        bool active = state.GetProperty("active").GetBoolean();
        Assert.Equal(active, f.Shots.TryGet(f.Shot.Handle, out var shot));
        if (active) Assert.Empty(f.Terminations.Events);
        else shot = Assert.Single(f.Terminations.Events).FinalProjectile;
        Assert.Equal(F(state, "position", 0), shot.PositionX);
        Assert.Equal(F(state, "position", 1), shot.PositionY);
        Assert.Equal(F(state, "velocity", 0), shot.VelocityX);
        Assert.Equal(F(state, "velocity", 1), shot.VelocityY);
        Assert.Equal(state.GetProperty("ai")[0].GetSingle(), shot.Ai.Ai0);
        Assert.Equal(state.GetProperty("ai")[1].GetSingle(), shot.Ai.Ai1);
        Assert.Equal(state.GetProperty("ai")[2].GetSingle(), shot.Ai.Ai2);
        if (active)
        {
            Assert.True(f.Shots.TryGetLifecycle(shot.Handle, out var life));
            Assert.Equal(state.GetProperty("timeLeft").GetInt32(), life.TimeLeft);
        }
        if (row.GetProperty("scene").GetString()!.StartsWith("lethal")) return;
        Assert.Equal(F(expected, "velocity", 0), player.VelocityX);
        Assert.Equal(F(expected, "velocity", 1), player.VelocityY);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var rng = after.GetProperty("player").GetProperty("rng");
        Assert.Equal(rng.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(f.Random)!);
        Assert.Equal(rng.GetProperty("slots").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(f.Random)!);
    }

    private static int Duration(JsonElement player, int type)
    {
        var types = player.GetProperty("buffType"); var times = player.GetProperty("buffTime");
        for (int i = 0; i < types.GetArrayLength(); i++) if (types[i].GetInt32() == type) return times[i].GetInt32();
        return 0;
    }
    private static float F(JsonElement value, string name, int index) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetProperty(name)[index].GetString(), 16));
}
