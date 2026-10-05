using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class SlimerSpawn1458Tests
{
    public static IEnumerable<object[]> Defaults()
    {
        using var stream = typeof(SlimerSpawn1458Tests).Assembly.GetManifestResourceStream(
            "TerraRuntime.Tests.Fixtures.slimer-tenth-defaults-windows-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            if (row.GetProperty("type").GetInt32() == -2) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Defaults))]
    public void Source_reset_defaults_match_genuine_Framework_arithmetic(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var context = new VanillaNpcSpawnContext(row.GetProperty("requestedDifficulty").GetSingle(),
            row.GetProperty("playerCount").GetInt32(), row.GetProperty("good").GetBoolean())
        { HardMode = row.GetProperty("hard").GetBoolean(), DownedPlantera = row.GetProperty("plantera").GetBoolean(),
            RemixWorld = row.GetProperty("remix").GetBoolean() };
        Assert.True(VanillaNpcSpawnDefaults.TryResolveSlimerChild(in context, true, out var actual));
        Assert.Equal((row.GetProperty("width").GetInt32(), row.GetProperty("height").GetInt32()),
            (actual.Hitbox.Width, actual.Hitbox.Height));
        Assert.Equal((row.GetProperty("lifeMax").GetInt32(), row.GetProperty("damage").GetInt32(),
            row.GetProperty("defense").GetInt32()), (actual.LifeMax, actual.Damage, actual.Defense));
        Assert.Equal(row.GetProperty("scale").GetSingle(), actual.Scale);
        Assert.Equal(row.GetProperty("knockback").GetSingle(), actual.KnockBackResist);
        Assert.Equal(row.GetProperty("value").GetSingle(), actual.VerifiedMoneyValue);
    }
    public static IEnumerable<object[]> HitEffects()
    {
        string platform = OperatingSystem.IsWindows() ? "windows" : "linux";
        using var stream = typeof(SlimerSpawn1458Tests).Assembly.GetManifestResourceStream(
            $"TerraRuntime.Tests.Fixtures.slimer-tenth-hit-{platform}-official.json.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray()) yield return [row.GetRawText()];
    }

    public static IEnumerable<object[]> PlatformHitDefaults()
    {
        foreach (string platform in new[] { "linux", "windows" })
        {
            using var stream = typeof(SlimerSpawn1458Tests).Assembly.GetManifestResourceStream(
                $"TerraRuntime.Tests.Fixtures.slimer-tenth-hit-{platform}-official.json.gz")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.EnumerateArray())
                if (row.GetProperty("seed").GetInt32() == 0 && !row.GetProperty("statue").GetBoolean() &&
                    !row.GetProperty("interactions").GetBoolean() && row.GetProperty("body").GetInt32() == 0 &&
                    row.GetProperty("capacity").GetInt32() == 1) yield return [row.GetRawText(), platform == "windows"];
        }
    }

    [Theory, MemberData(nameof(PlatformHitDefaults))]
    public void Both_original_platforms_resolve_exact_post_Reset_child_defaults(string json, bool windowsArithmetic)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement; var child = row.GetProperty("children")[0];
        bool good = row.GetProperty("good").GetBoolean(); int hard = row.GetProperty("hardPhase").GetInt32();
        var context = new VanillaNpcSpawnContext(row.GetProperty("mode").GetInt32() + 1f + (good ? 1f : 0f), 1, good)
        { HardMode = hard > 0, DownedPlantera = hard == 2 };
        Assert.True(VanillaNpcSpawnDefaults.TryResolveSlimerChild(in context, windowsArithmetic, out var actual));
        Assert.Equal((child.GetProperty("width").GetInt32(), child.GetProperty("height").GetInt32()),
            (actual.Hitbox.Width, actual.Hitbox.Height));
        Assert.Equal((child.GetProperty("lifeMax").GetInt32(), child.GetProperty("damage").GetInt32(), child.GetProperty("defense").GetInt32()),
            (actual.LifeMax, actual.Damage, actual.Defense));
        Assert.Equal(child.GetProperty("knockback").GetSingle(), actual.KnockBackResist);
        Assert.Equal(child.GetProperty("scale").GetSingle(), actual.Scale);
        Assert.Equal(child.GetProperty("value").GetSingle(), actual.VerifiedMoneyValue);
    }

    [Theory, MemberData(nameof(HitEffects))]
    public void Actual_dedicated_HitEffect_matches_private_birth_reset_motion_and_next_random(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        var random = new VanillaUnifiedRandom1458(row.GetProperty("seed").GetInt32());
        var sink = new Commits(); var store = new RuntimeNpcStore(200, sink);
        int capacity = row.GetProperty("capacity").GetInt32();
        var occupant = new NpcStateUpdate(3, 3, 0f, 0f, 0f, 0f, 255, default, NpcSimulationState.Initial);
        for (int slot = capacity; slot < 199; slot++) Assert.True(store.TrySpawn((byte)slot, in occupant, out _));
        var state = new NpcStateUpdate(121, 121, 1000.75f, 1000.25f, 2.25f, -.75f, 0, default,
            NpcSimulationState.Initial with { Life = 0, LifeMax = 100, DirectionX = -1,
                SpawnedFromStatue = row.GetProperty("statue").GetBoolean(),
                HitboxOverride = new(row.GetProperty("parentWidth").GetInt32(), row.GetProperty("parentHeight").GetInt32()) });
        Assert.True(store.TrySpawn(199, in state, out var parent));
        int hard = row.GetProperty("hardPhase").GetInt32(); bool good = row.GetProperty("good").GetBoolean();
        var context = new VanillaNpcSpawnContext(row.GetProperty("mode").GetInt32() + 1f + (good ? 1f : 0f), 1, good)
        { HardMode = hard > 0, DownedPlantera = hard == 2 };
        store.SetVanillaSpawnContextSource(() => context);
        store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random)); sink.Values.Clear();
        var children = row.GetProperty("children");
        Assert.True(store.TryExecuteSlimerDeathSpawn(in parent, out bool accepted, out var child));
        Assert.Equal(children.GetArrayLength() != 0, accepted);
        Assert.Equal(children.GetArrayLength(), sink.Values.Count);
        if (accepted)
        {
            var expected = children[0];
            Assert.Equal(expected.GetProperty("slot").GetInt32(), child.Handle.Slot);
            Assert.Equal((expected.GetProperty("type").GetInt32(), expected.GetProperty("netID").GetInt32()), (child.Type, (int)child.NetId));
            Assert.Equal((expected.GetProperty("x").GetSingle(), expected.GetProperty("y").GetSingle(),
                expected.GetProperty("vx").GetSingle(), expected.GetProperty("vy").GetSingle()),
                (child.PositionX, child.PositionY, child.VelocityX, child.VelocityY));
            Assert.Equal((expected.GetProperty("width").GetInt32(), expected.GetProperty("height").GetInt32()),
                (child.Simulation.HitboxOverride!.Value.Width, child.Simulation.HitboxOverride.Value.Height));
            Assert.Equal((expected.GetProperty("lifeMax").GetInt32(), expected.GetProperty("damage").GetInt32(), expected.GetProperty("defense").GetInt32()),
                (child.Simulation.LifeMax, child.Simulation.DamageOverride!.Value, child.Simulation.DefenseOverride!.Value));
            Assert.Equal(expected.GetProperty("scale").GetSingle(), child.Simulation.Scale);
            Assert.Equal(expected.GetProperty("knockback").GetSingle(), child.Simulation.KnockBackResist);
            Assert.Equal(expected.GetProperty("value").GetSingle(), child.Simulation.MoneyValue);
            Assert.Equal(expected.GetProperty("difficulty").GetSingle(), child.Simulation.SpawnDifficulty);
            Assert.Equal((expected.GetProperty("alpha").GetInt32(), expected.GetProperty("timeLeft").GetInt32(),
                expected.GetProperty("target").GetInt32()), (child.Simulation.Alpha, child.Simulation.TimeLeft, (int)child.Target));
            Assert.Equal(default, child.Ai);
            Assert.False(child.Simulation.SpawnedFromStatue);
            Assert.Equal(NpcStateCommitKind.Spawn, sink.Values[0].Kind);
            Assert.Equal(child, sink.Values[0].State);
        }
        Assert.Equal(row.GetProperty("next").GetInt32(), random.Next());
        Assert.True(store.TryGet(parent.Handle, out var retained)); Assert.Equal(parent, retained);
    }

    [Fact]
    public void Stale_or_alive_parent_cannot_construct_or_consume_NewNpc_random()
    {
        var random = new VanillaUnifiedRandom1458(1458); var before = random.Clone();
        var store = new RuntimeNpcStore(); store.SetVanillaSpawnRandomSource(new SystemVanillaNpcRandom(random));
        store.SetVanillaSpawnContextSource(static () => new(2f, 1, true));
        var state = new NpcStateUpdate(121, 121, 100f, 200f, 0f, 0f, 0, default,
            NpcSimulationState.Initial with { Life = 1, LifeMax = 100 });
        Assert.True(store.TrySpawn(0, in state, out var alive)); Assert.False(store.TryExecuteSlimerDeathSpawn(in alive, out _, out _));
        state = state with { Simulation = state.Simulation with { Life = 0 } };
        Assert.True(store.TryUpdate(alive.Handle, in state, out var dead));
        Assert.False(store.TryExecuteSlimerDeathSpawn(in alive, out _, out _));
        Assert.True(store.TryUpdate(dead.Handle, in state, out _)); Assert.False(store.TryExecuteSlimerDeathSpawn(in dead, out _, out _));
        Assert.True(random.HasSameState(before)); Assert.Equal(1, store.ActiveCount);
    }

    private sealed class Commits : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot State)> Values { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Values.Add((kind, snapshot));
    }

}
