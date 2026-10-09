using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PlayerItemPhaseOwnership1458Tests
{
    [Fact]
    public void Real_join_owns_constructor_clocks_and_prejoin42_changes_only_source_reported_mana()
    {
        using JsonDocument source = Facts();
        // Official constructor declarations and fresh style1 captures own zero clocks.
        // The older fixture omitted these fields; its other retained imports stay nullable.
        var constructor = Phase(Row(source, "constructor").GetProperty("constructor")) with
        {
            ToolTime = 0, AttackCD = 0, DerivedCrit = new(4, 4, 4)
        };
        foreach (bool reportZero in new[] { false, true })
        {
            using var f = new Fixture(spawn: false);
            if (reportZero) f.Mana(0, 0);
            f.Spawn();
            Assert.Equal(reportZero ? constructor with { BaseManaMaximum = 0 } : constructor, f.Member.ItemPhase);
            Assert.Equal(reportZero, f.Member.HasMana);
            Assert.Equal(0, f.Member.Mana);
            var transfer = f.Detach();
            Assert.NotNull(transfer.BuffState);
            transfer.BuffState!.CaptureSlots(out var types, out var times);
            Assert.Equal(44, types.Length);
            Assert.All(types, type => Assert.Equal(0, type.Value));
            Assert.All(times, time => Assert.Equal(0, time));
        }
    }

    [Fact]
    public void Actual42_and41_preserve_retained_unreported_clocks_and_source_zero_maximum()
    {
        using var f = new Fixture();
        using JsonDocument source = Facts();
        RuntimePlayerItemPhase1458 retained = Phase(Row(source, "retained-spawn").GetProperty("before"));
        var transfer = f.Detach();
        f.Attach(transfer with { Player = transfer.Player with { ItemAnimation = retained.Selected.Animation }, ItemPhase = retained });
        Assert.Equal(retained, f.Member.ItemPhase);
        f.Mana(0, 0);
        retained = retained with { BaseManaMaximum = 0, Mana = retained.Mana with { Mana = 0 } };
        Assert.Equal(retained, f.Member.ItemPhase);
        f.Mana(11, 80);
        retained = retained with { BaseManaMaximum = 80, Mana = retained.Mana with { Mana = 11 } };
        Assert.Equal(retained, f.Member.ItemPhase);
        f.Animation(5);
        Assert.Equal(retained with { Selected = retained.Selected with { Animation = 5 } }, f.Member.ItemPhase);
        Assert.Equal(5, f.Member.ItemAnimation);
        Assert.Equal(80, f.Member.MaxMana);
        Assert.Equal(0, f.Member.ItemPhase!.Value.Mana.Maximum);
        Assert.True(f.Players.TryConsumeMana(f.Connection, 3));
        Assert.Equal(8, f.Member.Mana);
        Assert.Equal(retained with
        {
            Mana = retained.Mana with { Mana = 8 }, Selected = retained.Selected with { Animation = 5 }
        }, f.Member.ItemPhase);
    }

    [Fact]
    public void Real_detach_attach_preserves_source_buff_durations_and_copies_retained_slots()
    {
        using var f = new Fixture();
        using JsonDocument source = Facts();
        JsonElement state = Row(source, "constructor-release-press").GetProperty("phases").EnumerateArray().Last().GetProperty("state");
        RuntimePlayerItemPhase1458 phase = Phase(state);
        var types = state.GetProperty("buffTypes").EnumerateArray().Select(v => new BuffTypeId(v.GetInt32())).ToArray();
        var times = state.GetProperty("buffTimes").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var retainedBuffs = PlayerBuffState.FromSlots(types, times);
        var transfer = f.Detach();
        f.Attach(transfer with
        {
            Player = transfer.Player with { ItemAnimation = phase.Selected.Animation }, ItemPhase = phase,
            BuffTypes = retainedBuffs.CaptureTypes(), BuffState = retainedBuffs
        });
        Assert.Equal(3600, f.Players.GetBuffDuration(f.Connection.Player, new(21)));
        Assert.True(retainedBuffs.TryApplySelectedConsumable(new(21), 7200, immune: false));
        Assert.Equal(3600, f.Players.GetBuffDuration(f.Connection.Player, new(21)));
        var captured = f.Detach();
        Assert.Equal(phase, captured.ItemPhase);
        Assert.NotSame(retainedBuffs, captured.BuffState);
        captured.BuffState!.CaptureSlots(out var actualTypes, out var actualTimes);
        Assert.Equal(types, actualTypes);
        Assert.Equal(times, actualTimes);
        f.Attach(captured);
        Assert.Equal(phase, f.Member.ItemPhase);
        Assert.Equal(3600, f.Players.GetBuffDuration(f.Connection.Player, new(21)));
        Assert.True(captured.BuffState.TryApplySelectedConsumable(new(21), 9000, immune: false));
        Assert.Equal(3600, f.Players.GetBuffDuration(f.Connection.Player, new(21)));
    }

    [Fact]
    public void Manual_import_without_phase_or_buff_slots_stays_unknown_after42_and41()
    {
        using var f = new Fixture();
        var captured = f.Detach();
        var imported = new RuntimePlayerTransferState(captured.Player, captured.Inventory,
            captured.Appearance, captured.Equipment, null, captured.GodMode);
        f.Attach(imported);
        Assert.Null(f.Member.ItemPhase);
        f.Mana(11, 80);
        f.Animation(5);
        Assert.Null(f.Member.ItemPhase);
        Assert.Equal(11, f.Member.Mana);
        Assert.Equal(5, f.Member.ItemAnimation);
        var after = f.Detach();
        Assert.Null(after.ItemPhase);
        Assert.Null(after.BuffState);
        Assert.Null(after.BuffTypes);
    }

    [Fact]
    public void Reconnect_owns_new_constructor_state_and_stale_generation_reports_cannot_replace_it()
    {
        using var f = new Fixture();
        var old = f.Connection;
        f.Mana(11, 80);
        f.Animation(5);
        f.Reconnect();
        Assert.NotEqual(old.Player, f.Connection.Player);
        var fresh = f.Member.ItemPhase;
        Assert.Equal(RuntimePlayerItemPhase1458.Constructor, fresh);
        Assert.True(f.Players.TryApply(new PlayerManaRuntimeCommand(old, new(old.Player.Slot, 99, 200))));
        Assert.True(f.Players.TryApply(new PlayerItemAnimationRuntimeCommand(old, 1f, 17)));
        Assert.Equal(fresh, f.Member.ItemPhase);
        Assert.False(f.Member.HasMana);
        Assert.Equal(0, f.Member.ItemAnimation);
    }

    [Fact]
    public void Copied_transfer_with_absent_or_changed_compact_buffs_cannot_restore_stale_full_slots()
    {
        using var f = new Fixture();
        var captured = f.Detach();
        f.Attach(captured with { BuffTypes = null });
        var unknown = f.Detach();
        Assert.Null(unknown.BuffTypes);
        Assert.Null(unknown.BuffState);
        f.Attach(captured with { BuffTypes = [new BuffTypeId(21)] });
        var edited = f.Detach();
        Assert.Equal(new[] { new BuffTypeId(21) }, edited.BuffTypes);
        Assert.NotNull(edited.BuffState);
        Assert.Equal(60, edited.BuffState!.GetDuration(new(21)));
    }

    [Fact]
    public void Real_spawn_and_respawn_commands_follow_original_positive_timer_and_spawn_context_life()
    {
        using var source = ReadSource("PlayerSpawnCommand1458");
        var rows = source.RootElement.GetProperty("facts").GetProperty("rows");
        Assert.Equal(8, rows.GetArrayLength());
        foreach (bool respawn in new[] { false, true })
        foreach (var row in rows.EnumerateArray())
        {
            using var f = new Fixture(spawn: respawn);
            short hp = checked((short)row.GetProperty("hp").GetInt32());
            byte context = row.GetProperty("context").GetByte();
            int timer = row.GetProperty("timer").GetInt32();
            Assert.True(f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Connection.Player.Slot, hp, 400))));
            if (respawn) f.Respawn(timer, context); else f.Spawn(timer, context);
            var expected = row.GetProperty("after");
            Assert.Equal(expected.GetProperty("dead").GetBoolean(), f.Member.IsDead);
            Assert.Equal(expected.GetProperty("life").GetInt32(), f.Member.CaptureSnapshot().NpcLife);
            Assert.Equal(expected.GetProperty("derivedLife").GetInt32(), f.Member.DerivedLifeMax);
            Assert.Equal(expected.GetProperty("baseLife").GetInt32(), f.Member.BaseLifeMax);
            Assert.Equal(expected.GetProperty("respawnTimer").GetInt32(), f.Member.ItemPhase!.Value.RespawnTimer);
            Assert.Equal(hp, f.Member.Life);
        }
    }

    [Fact]
    public void Real_respawn_preserves_source_jump_gravity_and_honey_shimmer_but_clears_wet_lava()
    {
        using var source = ReadSource("PlayerSpawnContacts1458");
        var rows = source.RootElement.GetProperty("facts");
        Assert.Equal(3, rows.GetArrayLength());
        foreach (var row in rows.EnumerateArray())
        {
            using var f = new Fixture();
            var before = row.GetProperty("beforeSpawn");
            var expected = row.GetProperty("initial");
            f.Mana(10, 200);
            Assert.True(f.Players.TryApply(new PlayerHealthRuntimeCommand(f.Connection,
                new(f.Connection.Player.Slot, checked((short)before.GetProperty("life").GetInt32()), 400))));
            f.Member.DerivedLifeMax = 400;
            f.Member.IsDead = before.GetProperty("dead").GetBoolean();
            f.Member.ItemPhase = new RuntimePlayerItemPhase1458(200, new(10, 200, 0, 0, 0),
                new(7, 0, 16, 17, true, 77, 0), .35f)
                { DeadTime = 20, RespawnTimer = 17, ManaPotionDelay = 33 };
            f.Member.ItemAnimation = 16;
            f.Member.PhysicsPhase = new(new(4, false, 23), new(true, true, true, true)) { GravityDirection = -1 };
            f.Respawn(17, row.GetProperty("mode").GetString() == "spawn-world-dead" ? (byte)1 : (byte)0);
            var physics = f.Member.PhysicsPhase!.Value;
            Assert.Equal(expected.GetProperty("gravDir").GetSingle(), physics.GravityDirection);
            Assert.Equal(expected.GetProperty("jump").GetInt32(), physics.Jump.RemainingTicks);
            Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), physics.Jump.ReleaseReady);
            Assert.Equal(expected.GetProperty("wingTime").GetSingle(), physics.Jump.WingTime);
            Assert.Equal(expected.GetProperty("wet").GetBoolean(), physics.Contacts.Wet);
            Assert.Equal(expected.GetProperty("lavaWet").GetBoolean(), physics.Contacts.Lava);
            Assert.Equal(expected.GetProperty("honeyWet").GetBoolean(), physics.Contacts.Honey);
            Assert.Equal(expected.GetProperty("shimmerWet").GetBoolean(), physics.Contacts.Shimmer);
            var phase = f.Member.ItemPhase!.Value;
            Assert.Equal(expected.GetProperty("deadTime").GetInt32(), phase.DeadTime);
            Assert.Equal(expected.GetProperty("respawnTimer").GetInt32(), phase.RespawnTimer);
            Assert.Equal(expected.GetProperty("manaPotionDelay").GetInt32(), phase.ManaPotionDelay);
            Assert.Equal(expected.GetProperty("potionDelay").GetInt32(), phase.Selected.PotionDelay);
            Assert.Equal(expected.GetProperty("animation").GetInt32(), phase.Selected.Animation);
            Assert.Equal(expected.GetProperty("itemTime").GetInt32(), phase.Selected.ItemTime);
            Assert.Equal(expected.GetProperty("heat").GetSingle(), phase.ManaHeat);
        }
    }

    private static JsonDocument ReadSource(string name)
    {
        using var stream = typeof(PlayerItemPhaseOwnership1458Tests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        var document = JsonDocument.Parse(gzip);
        Assert.Equal("4b87890ac53d40f61db5f928693a379acf4ccbd8ed3b47eb32fb096f145df034",
            document.RootElement.GetProperty("sourceSha256").GetString());
        return document;
    }

    private static JsonDocument Facts()
    {
        using Stream source = typeof(PlayerItemPhaseOwnership1458Tests).Assembly.GetManifestResourceStream("ConsumableLifecycle1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static JsonElement Row(JsonDocument source, string kind) =>
        source.RootElement.EnumerateArray().Single(row => row.GetProperty("kind").GetString() == kind);

    private static RuntimePlayerItemPhase1458 Phase(JsonElement state) => new(
        state.GetProperty("manaMax").GetInt32(),
        new(state.GetProperty("mana").GetInt32(), state.GetProperty("manaMax2").GetInt32(),
            state.GetProperty("delay").GetSingle(), state.GetProperty("count").GetInt32(), state.GetProperty("nebulaCount").GetInt32()),
        new(state.GetProperty("time").GetInt32(), state.GetProperty("timeMax").GetInt32(),
            state.GetProperty("animation").GetInt32(), state.GetProperty("animationMax").GetInt32(),
            state.GetProperty("release").GetBoolean(), state.GetProperty("potionDelay").GetInt32(), state.GetProperty("crit").GetInt32()),
        state.GetProperty("heat").GetSingle());

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession session = null!;
        private uint id;
        internal readonly PlayerAuthority Players = new(null, new WorldTileStore(new WorldDimensions(300, 200)));
        internal ConnectionHandle Connection { get; private set; }
        internal RuntimePlayerMember Member
        {
            get { Assert.True(Players.TryGet(Connection, out var member)); return member; }
        }
        internal Fixture(bool spawn = true)
        {
            Acquire();
            if (spawn) Spawn();
        }
        private void Acquire()
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(++id), session.Handle);
        }
        internal void Spawn(int timer = 0, byte context = 0) => Assert.True(Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session,
            new(session.Slot, 100, 103, timer, 0, 0, 0, context))));
        internal void Respawn(int timer, byte context) => Assert.True(Players.TryApply(new PlayerRespawnRuntimeCommand(Connection,
            new(session.Slot, 100, 103, timer, 0, 0, 0, context))));
        internal void Mana(short value, short maximum) =>
            Assert.True(Players.TryApply(new PlayerManaRuntimeCommand(Connection, new(Connection.Player.Slot, value, maximum))));
        internal void Animation(short value) =>
            Assert.True(Players.TryApply(new PlayerItemAnimationRuntimeCommand(Connection, .5f, value)));
        internal RuntimePlayerTransferState Detach()
        {
            var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
            Assert.True(Players.TryApply(new PlayerTransferDetachRuntimeCommand(Connection, completion)));
            return Assert.IsType<RuntimePlayerTransferState>(completion.Task.Result);
        }
        internal void Attach(RuntimePlayerTransferState state)
        {
            var completion = new TaskCompletionSource<bool>();
            Assert.True(Players.TryApply(new PlayerTransferAttachRuntimeCommand(Connection, state, 100, 100,
                PreserveWorldPosition: true, ForceRespawn: false, completion)));
            Assert.True(completion.Task.Result);
        }
        internal void Reconnect()
        {
            Assert.True(Players.TryApply(new PlayerDisconnectRuntimeCommand(Connection)));
            session.Dispose();
            Acquire(); Spawn();
        }
        public void Dispose() => session.Dispose();
    }
}
