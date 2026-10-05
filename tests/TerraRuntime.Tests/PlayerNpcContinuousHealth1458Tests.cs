using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Npcs.Loot;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerNpcContinuousHealth1458Tests
{
    public static IEnumerable<object[]> Sequences()
    {
        using var stream = typeof(PlayerNpcContinuousHealth1458Tests).Assembly.GetManifestResourceStream("PlayerNpcContinuousHealth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("sequences").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Sequences))]
    public void Genuine_Main_player_sequences_keep_life_and_persistent_clocks_without_replacing_combat_reports(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int maximum = row.GetProperty("maximum").GetInt32();
        int buff = row.GetProperty("buff").GetInt32();
        using var f = new PlayerDerivedHealth1458Tests.Fixture(maximum, buff == 2024 ? 0 : buff);
        if (buff == 2024)
            f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Session.Slot, new BuffTypeId[] { new(20), new(24) })));
        Configure(f, row.GetProperty("expert").GetBoolean());
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection,
            new(f.Session.Slot, row.GetProperty("initialLife").GetInt16(), checked((short)maximum))));
        short report = f.Member.Life;
        foreach (var tick in row.GetProperty("ticks").EnumerateArray())
        {
            int index = tick.GetProperty("tick").GetInt32();
            if (index == row.GetProperty("reportTick").GetInt32())
            {
                report = checked((short)(maximum / 2 + 1));
                f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, report, checked((short)maximum))));
            }
            f.Phase(tick.GetProperty("phase").GetString()!);
            f.Member.PositionY = 438f;
            f.Member.VelocityX = row.GetProperty("moving").GetBoolean() ? 1f : 0f;
            f.Players.TickHealthContext();
            var state = Assert.IsType<PlayerNpcHealthState1458>(f.Member.NpcHealth);
            Assert.True(f.Member.NpcLifeCurrent);
            Assert.Equal(tick.GetProperty("life").GetInt32(), state.Life);
            Assert.Equal(tick.GetProperty("count").GetInt32(), state.RegenCount);
            Assert.Equal(tick.GetProperty("time").GetSingle(), state.RegenTime);
            Assert.Equal(tick.GetProperty("maximum").GetInt32(), f.Member.DerivedLifeMax);
            Assert.Equal(report, f.Member.Life);
            Assert.True(f.Member.HasHealth);
            Assert.Equal(state.Life, f.Member.CaptureSnapshot().NpcLife);
            if (tick.GetProperty("hearts").ValueKind != JsonValueKind.Null)
            {
                var context = new VanillaNpcHealingContext1458(new(13), new(13), 50, 0,
                    f.Member.CaptureSnapshot().NpcLife < f.Member.DerivedLifeMax, false,
                    row.GetProperty("expert").GetBoolean());
                var origin = new NpcLootWorldItemOrigin(16000, 1600);
                var rolls = new Rolls(row.GetProperty("healSeed").GetInt32());
                var sink = new HealSink();
                Assert.True(VanillaNpcHealingLoot1458.TryExecute(in context, in origin, rolls, sink));
                Assert.Equal(tick.GetProperty("hearts").GetInt32(), sink.Hearts);
                Assert.Equal(tick.GetProperty("healNext").GetInt32(), rolls.Random.Next());
            }
        }
    }

    public static IEnumerable<object[]> Spawns()
    {
        using var stream = typeof(PlayerNpcContinuousHealth1458Tests).Assembly.GetManifestResourceStream("PlayerNpcContinuousHealth1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("spawns").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(Spawns))]
    public void Genuine_Spawn_preserves_regeneration_clocks_and_projects_its_current_life_before_the_next_phase(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 0);
        Configure(f);
        f.Member.DerivedLifeMax = 480;
        f.Member.NpcHealth = new(row.GetProperty("inputLife").GetInt32(), row.GetProperty("inputCount").GetInt32(),
            row.GetProperty("inputTime").GetSingle(), true);
        f.Member.IsDead = row.GetProperty("dead").GetBoolean();
        short report = f.Member.Life;
        f.Players.TryApply(new PlayerRespawnRuntimeCommand(f.Connection,
            new(f.Session.Slot, 40, 30, row.GetProperty("afterDead").GetBoolean() ? 1 : 0,
                0, 0, 0, row.GetProperty("context").GetByte())));
        var state = Assert.IsType<PlayerNpcHealthState1458>(f.Member.NpcHealth);
        Assert.Equal(row.GetProperty("life").GetInt32(), state.Life);
        Assert.Equal(row.GetProperty("count").GetInt32(), state.RegenCount);
        Assert.Equal(row.GetProperty("time").GetSingle(), state.RegenTime);
        Assert.Equal(row.GetProperty("afterDead").GetBoolean(), f.Member.IsDead);
        Assert.Equal(state.Life, f.Member.CaptureSnapshot().NpcLife);
        Assert.Equal(report, f.Member.Life);
    }

    [Fact]
    public void Packet16_updates_life_without_resetting_the_original_poison_accumulator()
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 20);
        Configure(f);
        f.Member.PositionY = 438f;
        for (int i = 0; i < 29; i++) f.Players.TickHealthContext();
        Assert.Equal(-116, f.Member.NpcHealth!.Value.RegenCount);
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 100, 100)));
        f.Players.TickHealthContext();
        Assert.Equal(99, f.Member.CaptureSnapshot().NpcLife);
        Assert.Equal(100, f.Member.Life);
    }

    [Theory]
    [InlineData("buff")] [InlineData("liquid")] [InlineData("solid")]
    [InlineData("animation")] [InlineData("sitting")] [InlineData("sleeping")]
    [InlineData("unlock")] [InlineData("import")] [InlineData("armor")]
    public void Selected_unowned_phase_retires_continuous_clocks_and_a_fresh_report_cannot_fabricate_them(string unknown)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 0);
        Configure(f);
        f.Member.PositionY = 438f;
        switch (unknown)
        {
            case "buff": f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Session.Slot, new BuffTypeId[] { new(165) }))); break;
            case "liquid": var water = new WorldTile { LiquidAmount = 255 }; f.Tiles.Set(37, 28, water); break;
            case "solid": var stone = new WorldTile { Flags = WorldTileFlags.Active, Type = 1 }; f.Tiles.Set(37, 28, stone); break;
            case "animation": f.Member.ItemAnimation = 1; break;
            case "sitting": f.Member.MiscFlags1 = 1 << 2; break;
            case "sleeping": f.Member.MiscFlags2 = 1; break;
            case "unlock": f.Member.NpcHealth = f.Member.NpcHealth!.Value with { SourceProfileKnown = false }; break;
            case "import": f.Member.NpcHealth = new(33, null, null); break;
            case "armor": f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Session.Slot, 59, 1, 0, 49, 0))); break;
        }
        f.Players.TickHealthContext();
        Assert.False(f.Member.NpcLifeCurrent);
        Assert.Null(f.Member.NpcHealth);
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 50, 100)));
        Assert.Equal(50, f.Member.CaptureSnapshot().NpcLife);
        f.Players.TickHealthContext();
        Assert.Null(f.Member.CaptureSnapshot().NpcLife);
    }

    [Theory]
    [InlineData("health")] [InlineData("buff")] [InlineData("world")]
    [InlineData("inventory")] [InlineData("disconnect")]
    public void Capture_callbacks_cannot_commit_over_a_newer_life_buff_world_inventory_or_generation(string mutation)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 20);
        Configure(f);
        f.Member.PositionY = 438f;
        var member = f.Member;
        f.Players.SetNpcHealthWorldFacts(() =>
        {
            switch (mutation)
            {
                case "health": f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 70, 100))); break;
                case "buff": f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection, new(f.Session.Slot, new BuffTypeId[] { new(24) }))); break;
                case "world": f.Tiles.Set(37, 28, default); break;
                case "inventory": f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Connection, new(f.Session.Slot, 0, 1, 0, 2, 0))); break;
                case "disconnect": f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection)); break;
            }
            return new(false, false);
        });
        f.Players.TickHealthContext();
        Assert.Equal(0, member.NpcHealth!.Value.RegenCount);
        Assert.Equal(0f, member.NpcHealth.Value.RegenTime);
        if (mutation == "health") Assert.Equal(70, member.NpcHealth.Value.Life);
    }

    [Fact]
    public void Unknown_moving_grapple_is_fenced_only_when_the_source_natural_rate_can_read_it()
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 0);
        Configure(f);
        f.Member.PositionY = 438f;
        f.Member.VelocityX = 1f;
        f.Players.SetNpcHealthGrapplingFacts(_ => null);
        for (int i = 0; i < 299; i++) f.Players.TickHealthContext();
        Assert.True(f.Member.NpcLifeCurrent);
        f.Players.TickHealthContext();
        Assert.False(f.Member.NpcLifeCurrent);
    }

    [Fact]
    public void Unassigned_import_and_revision_exhaustion_cannot_invent_a_current_zero_life()
    {
        Assert.Null((default(PlayerStateSnapshot) with { NpcLifeCurrent = true }).NpcLife);
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 0);
        Configure(f);
        f.Member.Revision = ulong.MaxValue;
        var before = f.Member.CaptureSnapshot();
        f.Players.TickHealthContext();
        Assert.Equal(before, f.Member.CaptureSnapshot());
        Assert.Null(before.NpcLife);
        Assert.Null(VanillaRemotePlayerHealth1458.Step(new(100, int.MaxValue, 0f), 100,
            false, false, false, true, false, false, 0, false, false, false));
        Assert.Null(VanillaRemotePlayerHealth1458.Step(PlayerNpcHealthState1458.Constructor, int.MaxValue,
            false, false, false, true, false, false, 0, false, false, false));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Transfer_carries_exact_owned_clocks_and_force_respawn_does_not_fabricate_source_history(bool force)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 0);
        var state = new PlayerNpcHealthState1458(77, -119, 1234f, true);
        f.Member.NpcHealth = state;
        f.Member.NpcLifeCurrent = true;
        var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
        f.Players.TryApply(new PlayerTransferDetachRuntimeCommand(f.Connection, detached));
        var transfer = Assert.IsType<RuntimePlayerTransferState>(await detached.Task);
        var destination = new PlayerAuthority(null, f.Tiles);
        var attached = new TaskCompletionSource<bool>();
        destination.TryApply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 40, 30, true, force, attached));
        Assert.True(await attached.Task);
        Assert.True(destination.TryGet(f.Connection, out var member));
        Assert.Equal(force ? null : state, member.NpcHealth);
        Assert.Equal(force ? null : 77, member.CaptureSnapshot().NpcLife);
    }

    [Fact]
    public void Replacement_generation_owns_new_constructor_clocks_and_rejects_old_reports()
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 0);
        f.Member.NpcHealth = new(77, -119, 1234f, true);
        f.Players.TryApply(new PlayerDisconnectRuntimeCommand(f.Connection));
        f.Session.Dispose();
        Assert.True(f.Pool.TryAcquireConnection(out var lease));
        using var next = new PlayerJoinSession(lease!);
        next.ObserveWorldRequest();
        next.ObserveSectionRequest();
        var connection = new ConnectionHandle(f.Connection.Source, next.Handle);
        f.Players.TryApply(new PlayerSpawnRuntimeCommand(connection, next, new(next.Slot, 40, 30, 0, 0, 0, 0, 0)));
        f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(next.Slot, 99, 400)));
        Assert.True(f.Players.TryGet(connection, out var member));
        Assert.Equal(PlayerNpcHealthState1458.Constructor, member.NpcHealth);
        Assert.False(member.HasHealth);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Accepted_owned_hurt_preserves_count_and_resets_time_only_when_report_and_projection_agree(bool divergent)
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(100, 20);
        Configure(f);
        f.Member.PositionY = 438f;
        for (int tick = 0; tick < 29; tick++) f.Players.TickHealthContext();
        if (divergent) f.Member.NpcHealth = f.Member.NpcHealth!.Value with { Life = f.Member.Life - 1 };
        Assert.Equal(PlayerDamageCommitResult.Committed,
            f.Players.TryCommitAuthoritativeNpcContactDamage(100, new(0, new NpcGeneration(1)), f.Connection.Player,
                5, 0, TerraRuntime.Gameplay.Players.VanillaPlayerImmunityChannel1458.General, out var snapshot));
        if (divergent)
        {
            Assert.Null(snapshot.NpcHealth);
            Assert.Null(snapshot.NpcLife);
        }
        else
        {
            Assert.Equal(-116, snapshot.NpcHealth!.Value.RegenCount);
            Assert.Equal(0f, snapshot.NpcHealth.Value.RegenTime);
            Assert.Equal(snapshot.Life, snapshot.NpcHealth.Value.Life);
        }
    }

    [Fact]
    public void Fatal_pvp_retires_the_unowned_source_spawn_death_policy()
    {
        using var f = new PlayerDerivedHealth1458Tests.Fixture(400, 0);
        f.Players.TryApply(new PlayerPvpToggleRuntimeCommand(f.Connection, true));
        PlayerStateSnapshot attacker = f.Member.CaptureSnapshot() with
        {
            Player = new(new PlayerSlotId(1), new PlayerSessionGeneration(1)),
            Hostile = true
        };
        Assert.Equal(PlayerDamageCommitResult.Committed,
            f.Players.TryCommitAuthoritativePvpDamageFromSnapshot(10, in attacker, f.Connection.Player,
                DamageSource.FromPlayerItem(attacker.Player), 100000, false, 1, out var dead));
        Assert.True(dead.IsDead);
        Assert.False(dead.NpcHealth!.Value.SourceProfileKnown);
        f.Players.TryApply(new PlayerRespawnRuntimeCommand(f.Connection,
            new(f.Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
        Assert.Null(f.Member.NpcHealth);
        Assert.Null(f.Member.CaptureSnapshot().NpcLife);
    }

    private static void Configure(PlayerDerivedHealth1458Tests.Fixture f, bool expert = false)
    {
        f.Players.SetNpcHealthWorldFacts(() => new(expert, false));
        f.Players.SetNpcHealthGrapplingFacts(_ => false);
    }

    private sealed class Rolls(int seed) : INpcLootRollSource
    {
        internal VanillaUnifiedRandom1458 Random { get; } = new(seed);
        public int NextInt32(int minimum, int maximum) => Random.Next(minimum, maximum);
        public int RollLuck(int denominator) => Random.Next(denominator);
    }

    private sealed class HealSink : IBossRecoveryLootDeliverySink1458
    {
        private readonly VanillaNpcLootWorldItemMaterializer materializer = new(() => new(false, false, false));
        internal int Hearts { get; private set; }
        public bool CanDeliverWorldItem(ItemTypeId type) => true;
        public bool TryDeliverWorldItem(in NpcLootWorldItemOrigin origin, in NpcLootDrop drop, INpcLootRollSource random)
        {
            if (!materializer.TryMaterialize(in origin, in drop, random, out _)) return false;
            if (drop.ItemType.Value == 58) Hearts++;
            return true;
        }
    }
}
