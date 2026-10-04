using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MotherSlimeHitEffect1458Tests
{
    // Independently captured original NPC.HitEffect, Linux server binary 1.4.5.8
    // SHA256 4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034.
    public static IEnumerable<object[]> Cases()
    {
        using var stream = typeof(MotherSlimeHitEffect1458Tests).Assembly.GetManifestResourceStream("MotherSlimeHitEffect1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.EnumerateArray())
            yield return [row.Clone()];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Original_birth_fields_live_body_capacity_and_next_random_match(JsonElement row)
    {
        int I(string name) => row.GetProperty(name).GetInt32();
        bool B(string name) => row.GetProperty(name).GetBoolean();
        var sink = new Recording();
        var store = new RuntimeNpcStore(200, sink);
        for (int slot = I("capacity"); slot < 199; slot++)
        {
            var occupied = new NpcStateUpdate(3, 3, 0, 0, 0, 0, 255, default, NpcSimulationState.Initial);
            Assert.True(store.TrySpawn((byte)slot, in occupied, out _));
        }
        var parentUpdate = new NpcStateUpdate(16, 16, 1000.75f, 1000.25f, 2.25f, -.75f, 0,
            default, NpcSimulationState.Initial with
            {
                DirectionX = -1,
                Life = 0,
                LifeMax = 90,
                SpawnedFromStatue = B("statue"),
                HitboxOverride = new(I("parentWidth"), I("parentHeight"))
            });
        Assert.True(store.TrySpawn(199, in parentUpdate, out var parent));
        sink.Events.Clear();
        var random = new SystemVanillaNpcRandom(I("seed"));
        var context = new VanillaNpcSpawnContext(I("mode") + 1 + (B("good") ? 1 : 0), 1, B("good"))
        {
            HardMode = I("hardPhase") > 0,
            DownedPlantera = I("hardPhase") == 2
        };
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => context);
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, random);
        var expected = row.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(expected.Length, sink.Events.Count);
        foreach (var entry in expected)
        {
            var child = Assert.Single(sink.Events, e => e.Snapshot.Handle.Slot == entry.GetProperty("slot").GetInt32()).Snapshot;
            Assert.True(store.TryGet(child.Handle, out var current));
            Assert.Equal(child, current);
            float F(string name) => entry.GetProperty(name).GetSingle();
            int N(string name) => entry.GetProperty(name).GetInt32();
            Assert.Equal(N("type"), child.Type);
            Assert.Equal(N("netID"), child.NetId);
            Assert.Equal(F("x"), child.PositionX);
            Assert.Equal(F("y"), child.PositionY);
            Assert.Equal(F("vx"), child.VelocityX);
            Assert.Equal(F("vy"), child.VelocityY);
            var simulation = child.Simulation;
            Assert.Equal(new NpcHitboxDimensions(N("width"), N("height")), simulation.HitboxOverride);
            Assert.Equal(N("life"), simulation.Life);
            Assert.Equal(N("lifeMax"), simulation.LifeMax);
            Assert.Equal(N("damage"), simulation.BaseDamage);
            Assert.Equal(N("defense"), simulation.BaseDefense);
            Assert.Equal(N("defDamage"), simulation.DamageOverride);
            Assert.Equal(N("defLifeMax"), simulation.BaseLifeMax);
            Assert.Equal(F("knockback"), simulation.KnockBackResist);
            Assert.Equal(F("difficulty"), simulation.SpawnDifficulty);
            Assert.Equal(F("scale"), simulation.Scale);
            Assert.Equal(F("value"), simulation.MoneyValue);
            Assert.Equal(N("target"), child.Target);
            Assert.Equal(N("direction"), simulation.DirectionX);
            Assert.Equal(N("directionY"), simulation.DirectionY);
            Assert.Equal(N("sprite"), simulation.SpriteDirection);
            Assert.Equal(entry.GetProperty("statue").GetBoolean(), simulation.SpawnedFromStatue);
            Assert.Equal(N("alpha"), simulation.Alpha);
            Assert.Equal(N("timeLeft"), simulation.TimeLeft);
            Assert.Equal(entry.GetProperty("noGravity").GetBoolean(), simulation.NoGravity);
            Assert.Equal(entry.GetProperty("noTile").GetBoolean(), simulation.NoTileCollide);
            var ai = entry.GetProperty("ai");
            Assert.Equal(new NpcAiState(ai[0].GetSingle(), ai[1].GetSingle(), ai[2].GetSingle(), ai[3].GetSingle()), child.Ai);
            var birth = Assert.Single(sink.Events, e => e.Snapshot.Handle == child.Handle);
            Assert.Equal(NpcStateCommitKind.Spawn, birth.Kind);
            Assert.Equal(child, birth.Snapshot);
            Assert.Equal(new NpcRevision(1), child.Revision);
        }
        Assert.Equal(I("next"), random.SourceRandom.Next());
    }

    [Fact]
    public void Replacement_has_a_fresh_generation_and_only_the_final_baby_birth_is_published()
    {
        var sink = new Recording();
        var store = new RuntimeNpcStore(2, sink);
        var oldUpdate = new NpcStateUpdate(3, 3, 0, 0, 0, 0, 255, default,
            NpcSimulationState.Initial with { CanBeReplacedByOtherNpcs = true });
        Assert.True(store.TrySpawn(0, in oldUpdate, out var old));
        var parentUpdate = DeadParent();
        Assert.True(store.TrySpawn(1, in parentUpdate, out var parent));
        sink.Events.Clear();
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, new SystemVanillaNpcRandom(0));
        var birth = Assert.Single(sink.Events);
        Assert.Equal(NpcStateCommitKind.Spawn, birth.Kind);
        Assert.Equal(0, birth.Snapshot.Handle.Slot);
        Assert.NotEqual(old.Handle.Generation, birth.Snapshot.Handle.Generation);
        Assert.False(store.TryGet(old.Handle, out _));
        Assert.Equal(-5, birth.Snapshot.NetId);
        Assert.Equal(new NpcRevision(1), birth.Snapshot.Revision);
        Assert.False(birth.Snapshot.Simulation.CanBeReplacedByOtherNpcs);
        Assert.Equal(750, birth.Snapshot.Simulation.TimeLeft);
        Assert.Equal(new NpcHitboxDimensions(21, 17), birth.Snapshot.Simulation.HitboxOverride);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stale_parent_revision_or_generation_consumes_no_random_and_publishes_nothing(bool replace)
    {
        var sink = new Recording();
        var store = new RuntimeNpcStore(4, sink);
        var update = DeadParent();
        Assert.True(store.TrySpawn(3, in update, out var parent));
        if (replace)
        {
            Assert.True(store.TryDespawn(parent.Handle));
            Assert.True(store.TrySpawn(3, in update, out _));
        }
        else
        {
            var next = update with { PositionX = 1001f };
            Assert.True(store.TryUpdate(parent.Handle, in next, out _));
        }
        sink.Events.Clear();
        var random = new SystemVanillaNpcRandom(1458);
        int expected = new SystemVanillaNpcRandom(1458).SourceRandom.Next();
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, random);
        Assert.Empty(sink.Events);
        Assert.Equal(1, store.ActiveCount);
        Assert.Equal(expected, random.SourceRandom.Next());
    }

    [Fact]
    public void A_retired_protected_child_slot_is_not_reused_by_the_next_split()
    {
        var sink = new Recording();
        var store = new RuntimeNpcStore(2, sink);
        var update = DeadParent();
        Assert.True(store.TrySpawn(1, in update, out var parent));
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, new SystemVanillaNpcRandom(0));
        var birth = Assert.Single(sink.Events, e => e.Snapshot.Handle.Slot == 0).Snapshot;
        Assert.True(store.TryDespawn(birth.Handle));
        sink.Events.Clear();
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, new SystemVanillaNpcRandom(0));
        Assert.Empty(sink.Events);
        Assert.Equal(1, store.ActiveCount);
        store.UpdateProtectedSpawnSlots();
        store.UpdateProtectedSpawnSlots();
        VanillaMotherSlimeDeathSplit1458.SpawnChildren(store, in parent, new SystemVanillaNpcRandom(0));
        var replacement = Assert.Single(sink.Events).Snapshot;
        Assert.NotEqual(birth.Handle.Generation, replacement.Handle.Generation);
        Assert.Equal(-5, replacement.NetId);
    }

    private static NpcStateUpdate DeadParent() => new(16, 16, 1000.75f, 1000.25f, 2.25f, -.75f, 0,
        default, NpcSimulationState.Initial with { DirectionX = -1, Life = 0, LifeMax = 90 });

    private sealed class Recording : INpcStateCommitSink
    {
        public List<(NpcStateCommitKind Kind, NpcSnapshot Snapshot)> Events { get; } = [];
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) => Events.Add((kind, snapshot));
    }
}
