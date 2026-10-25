using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Buffs;

namespace TerraRuntime.Tests;

public sealed class NpcContactHumanAuthority1458Tests
{
    [Fact]
    public void Real_contact_pass_adopts_multiple_effects_before_health_then_preserves_owned_timers_against_reports()
    {
        using var f = new Fixture();
        int healthCallbacks = 0;
        f.Events.OnHealth = () =>
        {
            Assert.Equal(300, f.Buff(VanillaBuffIds.Stinky));
            Assert.Equal(300, f.Buff(VanillaBuffIds.Poisoned));
            healthCallbacks++;
        };
        f.Contact(1, 5395f, 1);
        Assert.Equal(490, f.Member.Life);
        Assert.Equal(1, healthCallbacks);
        Assert.Equal(2, f.Random.Draws); // DamageVar, then carried-potion duration.
        Assert.Equal(new[] { VanillaBuffIds.Stinky, VanillaBuffIds.Poisoned }, f.Events.Confirmed.Select(x => x.Type));
        f.Players.AdvanceCombatTick(2);
        Assert.Equal(299, f.Buff(VanillaBuffIds.Poisoned));
        f.Report([VanillaBuffIds.Stinky, VanillaBuffIds.Poisoned]);
        f.Report([]);
        f.Report([VanillaBuffIds.Poisoned]);
        Assert.Equal(299, f.Buff(VanillaBuffIds.Poisoned));
        Assert.Equal(299, f.Buff(VanillaBuffIds.Stinky));
        f.Contact(1, 5395f, 2);
        Assert.Equal(2, f.Random.Draws); // General immunity prevents both variation and status draws.
        f.Events.OnHealth = null;
        f.Contact(1, 5395f, 41);
        Assert.Equal(300, f.Buff(VanillaBuffIds.Poisoned));
        Assert.Equal(4, f.Random.Draws);
        for (int tick = 42; tick <= 341; tick++) f.Players.AdvanceCombatTick(tick);
        Assert.Equal(0, f.Buff(VanillaBuffIds.Poisoned));
        Assert.Equal(0, f.Buff(VanillaBuffIds.Stinky));
        Assert.Empty(f.Events.Presented);
    }

    [Fact]
    public void Human_immunity_filters_effect_after_rng_and_lethal_zero_or_godmode_hits_cannot_earn_status()
    {
        using (var f = new Fixture())
        {
            f.EquipAccessory(1613);
            f.Contact(1, 5395f, 1);
            Assert.Equal(2, f.Random.Draws);
            Assert.Equal(0, f.Buff(VanillaBuffIds.Poisoned));
            Assert.Equal(300, f.Buff(VanillaBuffIds.Stinky));
        }
        using (var f = new Fixture())
        {
            f.Member.GodMode = true;
            f.Contact(1, 5395f, 1);
            Assert.Equal(0, f.Random.Draws);
            Assert.Equal(500, f.Member.Life);
            Assert.Empty(f.Events.Confirmed);
        }
        using (var f = new Fixture())
        {
            f.Member.Life = 1;
            f.Contact(1, 5395f, 1);
            Assert.True(f.Member.IsDead);
            Assert.Equal(1, f.Random.Draws);
            Assert.Empty(f.Events.Confirmed);
        }
        using (var f = new Fixture())
        {
            var source = new NpcContactBuffSource1458(1, 1, 5395f, false, f.Random);
            Assert.Equal(PlayerDamageCommitResult.Rejected, f.Players.TryCommitAuthoritativeNpcContactDamage(1,
                new NpcHandle(0, new NpcGeneration(1)), f.Connection.Player, 0, 0,
                VanillaPlayerImmunityChannel1458.General, out _, contactBuffs: source));
            Assert.Equal(0, f.Random.Draws);
            Assert.Empty(f.Events.Confirmed);
        }
    }

    [Fact]
    public void Disconnect_and_replacement_during_health_publication_suppress_old_generation_confirmation()
    {
        using var f = new Fixture();
        var old = f.Connection;
        f.Events.OnHealth = () =>
        {
            Assert.Equal(300, f.Buff(VanillaBuffIds.Poisoned));
            f.Players.TryApply(new PlayerDisconnectRuntimeCommand(old));
            f.ReleaseSession();
            f.Join(2);
        };
        f.Contact(1, 5395f, 1);
        Assert.NotEqual(old.Player.Generation, f.Connection.Player.Generation);
        Assert.Equal(old.Player.Slot, f.Connection.Player.Slot);
        Assert.Equal(0, f.Buff(VanillaBuffIds.Poisoned));
        Assert.Equal(0, f.Players.GetBuffDuration(old.Player, VanillaBuffIds.Poisoned));
        Assert.Empty(f.Events.Confirmed);
    }

    [Fact]
    public void Special_slime_contact_earns_no_damage_buff_without_damage_rng_and_death_clears_owned_status()
    {
        using var f = new Fixture();
        f.Contact(1, 1125f, 1);
        Assert.Equal(500, f.Member.Life);
        Assert.Equal(1800, f.Buff(new BuffTypeId(48)));
        Assert.Equal(0, f.Random.Draws);
        f.Member.IsDead = true;
        f.Players.AdvanceCombatTick(2);
        Assert.Equal(0, f.Buff(new BuffTypeId(48)));
        Assert.Empty(f.Events.Presented);
    }

    [Fact]
    public void Buff_slot_compaction_does_not_age_a_shifted_owned_effect_twice()
    {
        var types = new BuffTypeId[PlayerBuffState.Capacity];
        var durations = new int[PlayerBuffState.Capacity];
        types[3] = VanillaBuffIds.Poisoned; durations[3] = 1;
        types[4] = VanillaBuffIds.CursedInferno; durations[4] = 5;
        var state = PlayerBuffState.FromSlots(types, durations);
        Assert.True(state.TryApplyNpcContactBuff(VanillaBuffIds.Poisoned, 1));
        Assert.True(state.TryApplyNpcContactBuff(VanillaBuffIds.CursedInferno, 5));
        Assert.True(state.TickNpcContactBuffs());
        Assert.Equal(0, state.GetDuration(VanillaBuffIds.Poisoned));
        Assert.Equal(4, state.GetDuration(VanillaBuffIds.CursedInferno));
    }

    [Fact]
    public void Final_update_flags_cloned_ownership_unearned_imports_and_goodworld_difficulty_keep_source_boundaries()
    {
        var state = new PlayerBuffState();
        Assert.True(state.TryApplyNpcContactBuff(VanillaBuffIds.Poisoned, 1));
        Assert.True(state.TryApplyNpcContactBuff(VanillaBuffIds.Frozen, 1));
        var clone = state.Clone();
        Assert.True(state.HasSameSlots(clone));
        Assert.True(clone.TickNpcContactBuffs());
        Assert.Equal(0, clone.GetDuration(VanillaBuffIds.Poisoned));
        Assert.True(clone.CaptureDebuffFlags().Poisoned);
        Assert.True(clone.IsNpcContactFrozen);
        Assert.Equal(1, state.GetDuration(VanillaBuffIds.Poisoned));
        Assert.False(state.HasSameSlots(clone));
        var finalUpdate = clone.Clone();
        Assert.True(finalUpdate.CaptureDebuffFlags().Poisoned);
        Assert.True(finalUpdate.IsNpcContactFrozen);
        finalUpdate.TickNpcContactBuffs();
        Assert.False(finalUpdate.CaptureDebuffFlags().Poisoned);
        Assert.False(finalUpdate.IsNpcContactFrozen);
        Assert.False(finalUpdate.HasOwnedNpcContactBuffs);

        var types = new BuffTypeId[PlayerBuffState.Capacity];
        var durations = new int[PlayerBuffState.Capacity];
        types[0] = VanillaBuffIds.Poisoned; durations[0] = 9999;
        types[1] = VanillaBuffIds.Frozen; durations[1] = 60;
        var imported = PlayerBuffState.FromSlots(types, durations);
        Assert.True(imported.Contains(VanillaBuffIds.Frozen));
        Assert.False(imported.IsNpcContactFrozen);
        Assert.True(imported.TryApplyNpcContactBuff(VanillaBuffIds.Poisoned, 2));
        Assert.Equal(2, imported.GetDuration(VanillaBuffIds.Poisoned));
        Span<NpcContactBuff1458> effects = stackalloc NpcContactBuff1458[4];
        var random = new MinimumRandom();
        Assert.Equal(2, VanillaNpcContactBuffs1458.Evaluate(150, 150, 0f, false,
            imported.IsNpcContactFrozen, false, random, effects));
        Assert.Equal(VanillaBuffIds.Frozen, effects[1].Type);
        Assert.Equal(2, random.Draws);

        foreach (bool expert in new[] { false, true })
        {
            using var f = new Fixture(expert);
            var source = new NpcContactBuffSource1458(525, 525, 0f, true, f.Random);
            Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(source));
            Assert.Equal(expert ? 600 : 480, f.Buff(VanillaBuffIds.CursedInferno));
        }
    }

    [Fact]
    public void Disconnect_during_status_rng_cannot_apply_or_publish_effects_to_the_replacement()
    {
        using var f = new Fixture();
        var old = f.Connection;
        int healthCallbacks = 0;
        f.Events.OnHealth = () => healthCallbacks++;
        var random = new CallbackRandom(() =>
        {
            f.Players.TryApply(new PlayerDisconnectRuntimeCommand(old));
            f.ReleaseSession();
            f.Join(2);
        });
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(new(1, 1, 5395f, false, random)));
        Assert.NotEqual(old.Player.Generation, f.Connection.Player.Generation);
        Assert.Equal(500, f.Member.Life);
        Assert.Equal(0, f.Buff(VanillaBuffIds.Poisoned));
        Assert.Equal(0, healthCallbacks);
        Assert.Empty(f.Events.Confirmed);
    }

    private sealed class CallbackRandom(Action callback) : Random
    {
        public override int Next(int minValue, int maxValue) { callback(); return minValue; }
    }

    private sealed class MinimumRandom : Random
    {
        internal int Draws;
        public override int Next(int minValue, int maxValue) { Draws++; return minValue < 0 ? 0 : minValue; }
        public override int Next(int maxValue) { Draws++; return 0; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession? session;
        internal readonly EventSink Events = new();
        internal readonly MinimumRandom Random = new();
        internal readonly PlayerAuthority Players;
        internal ConnectionHandle Connection { get; private set; }
        internal RuntimePlayerMember Member { get { Assert.True(Players.TryGet(Connection, out var member)); return member; } }
        internal Fixture(bool expert = false) { Players = new(Events, null, expertMode: expert); Join(1); }
        internal void Join(long id)
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(id), session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session,
                new(Connection.Player.Slot, 10, 10, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(Connection.Player.Slot, 500, 500)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, 59, 0, 0, 0, 0)));
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
                new(Connection.Player.Slot, 0, 0, 0, 0, 0, 100, 100, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
        }
        internal void Contact(int type, float ai1, long tick)
        {
            var npcs = new RuntimeNpcStore(1);
            var state = new NpcStateUpdate((ushort)type, (short)type, 100, 100, 0, 0, 255,
                new NpcAiState(0f, ai1, 0f, 0f), NpcSimulationState.Initial with { Friendly = false, DamageOverride = 10 });
            Assert.True(npcs.TrySpawn(0, in state, out _));
            new RuntimeNpcPlayerCombatPass(npcs, Players, Random).Tick(tick);
        }
        internal void EquipAccessory(short type) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new(Connection.Player.Slot, 62, 1, 0, type, 0)));
        internal PlayerDamageCommitResult Hurt(NpcContactBuffSource1458 source) =>
            Players.TryCommitAuthoritativeNpcContactDamage(1, new NpcHandle(0, new NpcGeneration(1)),
                Connection.Player, 10, 0, VanillaPlayerImmunityChannel1458.General, out _, contactBuffs: source);
        internal void Report(BuffTypeId[] types) => Players.TryApply(new PlayerBuffTypesRuntimeCommand(Connection,
            new(Connection.Player.Slot, types)));
        internal int Buff(BuffTypeId type) => Players.GetBuffDuration(Connection.Player, type);
        internal void ReleaseSession() { session?.Dispose(); session = null; }
        public void Dispose() => ReleaseSession();
    }

    private sealed class EventSink : IRuntimePlayerEventSink
    {
        internal Action? OnHealth;
        internal List<(BuffTypeId Type, int Duration)> Confirmed { get; } = [];
        internal BuffTypeId[] Presented = [];
        public void PlayerAuthoritativeHealthUpdated(ConnectionHandle connection, in PlayerHealthCommitRequest request) => OnHealth?.Invoke();
        public void PlayerNpcContactBuffApplied(PlayerHandle player, BuffTypeId buffType, int durationTicks) => Confirmed.Add((buffType, durationTicks));
        public void PlayerBuffTypesUpdated(ConnectionHandle connection, in PlayerBuffTypesCommitRequest request) => Presented = request.BuffTypes.ToArray();
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
