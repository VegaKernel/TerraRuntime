using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerNpcContactBuff1458Tests
{
    [Fact]
    public void Successful_contact_adopts_status_before_motion_and_health_observers()
    {
        using var f = new Fixture();
        int observations = 0;
        f.Events.Observe = () =>
        {
            Assert.Equal(240, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.CursedInferno));
            observations++;
        };
        var source = new NpcContactBuffSource1458(525, 525, 0f, false, new Random(1458));
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(source, 20, hitDirection: 1));
        Assert.Equal(2, observations);
        Assert.Contains(VanillaBuffIds.CursedInferno, f.Events.Buffs);
        Assert.Equal(0, f.Authority.GetMoonLeechDuration(f.Handle));
    }

    [Fact]
    public void Motion_observer_replacement_stops_old_generation_health_and_buff_publication()
    {
        using var f = new Fixture();
        int initialVitals = f.Events.VitalsPublications;
        PlayerHandle replacement = default;
        f.Events.Observe = () =>
        {
            Assert.Equal(240, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.CursedInferno));
            f.Events.Observe = null;
            Assert.True(f.Authority.Despawn(f.Id));
            var created = f.Authority.Create(f.Id, 160f, 160f);
            Assert.True(created.IsCreated);
            replacement = created.Player;
            Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(500, 500, 200, 200)));
        };
        var source = new NpcContactBuffSource1458(525, 525, 0f, false, new Random(1458));
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(source, 20, hitDirection: 1));
        Assert.NotEqual(f.Handle.Generation, replacement.Generation);
        Assert.Equal(initialVitals + 1, f.Events.VitalsPublications);
        Assert.Equal(0, f.Events.BuffPublications);
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(replacement, VanillaBuffIds.CursedInferno));
    }

    [Fact]
    public void Immunity_godmode_and_lethal_contact_do_not_mint_status_or_skip_required_rng()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.SetItem(f.Id, new ServerPlayerItemState(
            (short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), new ItemTypeId(1613), 1, default, 0)));
        var random = new MinimumRandom();
        var source = new NpcContactBuffSource1458(1, 1, 5395f, false, random);
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(source, 40));
        Assert.Equal(1, random.Draws);
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.Poisoned));
        Assert.Equal(300, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.Stinky));
        Assert.Equal(PlayerDamageCommitResult.Rejected, f.Hurt(source, 40));
        Assert.Equal(1, random.Draws);
        Assert.True(f.Authority.SetGodMode(f.Id, true));
        Assert.Equal(PlayerDamageCommitResult.AvoidedByGodMode, f.Hurt(source, 40, tick: 100));
        Assert.Equal(1, random.Draws);
        Assert.True(f.Authority.SetGodMode(f.Id, false));
        Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(1, 500, 200, 200)));
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Hurt(source, 100, tick: 200));
        Assert.Equal(1, random.Draws);
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.Stinky));
    }

    [Fact]
    public void Contact_buffs_refresh_age_merge_with_existing_effects_and_do_not_cross_generations()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.TryApplyMoonLeech(f.Handle, 100));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 5));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 2));
        Assert.Equal(5, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.Poisoned));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 7));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.OnFire, 9));
        Assert.Equal(3, f.Events.Buffs.Distinct().Count());
        Assert.Contains(VanillaBuffIds.MoonLeech, f.Events.Buffs);
        Assert.Contains(VanillaBuffIds.OnFire, f.Events.Buffs);
        Assert.Contains(VanillaBuffIds.Poisoned, f.Events.Buffs);
        f.Tick(7);
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.Poisoned));
        Assert.Equal(2, f.Authority.GetNpcContactBuffDuration(f.Handle, VanillaBuffIds.OnFire));
        Assert.DoesNotContain(VanillaBuffIds.Poisoned, f.Events.Buffs);
        Assert.True(f.Authority.Despawn(f.Id));
        var replacement = f.Authority.Create(f.Id, 160f, 160f);
        Assert.True(replacement.IsCreated);
        Assert.NotEqual(f.Handle.Generation, replacement.Player.Generation);
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(replacement.Player, VanillaBuffIds.OnFire));
        Assert.Equal(0, f.Authority.GetNpcContactBuffDuration(replacement.Player, VanillaBuffIds.Poisoned));
        Assert.False(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 60));
    }

    [Fact]
    public void Neutral_dot_is_additive_includes_final_tick_and_uses_poison_death_reason()
    {
        using var f = new Fixture();
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 30));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.CursedInferno, 30));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.OnFire, 30));
        f.Tick(30);
        Assert.Equal(491, f.Player.Life); // Player.UpdateLifeRegen: (4+24+8)*30/120.
        f.Tick(30);
        Assert.Equal(491, f.Player.Life);
        Assert.True(f.Authority.SetVitals(f.Id, new ServerPlayerVitalsState(1, 500, 200, 200)));
        Assert.True(f.Authority.TryApplyNpcContactBuff(f.Handle, VanillaBuffIds.Poisoned, 30));
        f.Tick(30);
        Assert.True(f.Player.IsDead);
        Assert.Equal(EnvironmentDamageCause.Poison, f.Events.DeathSource.EnvironmentCause);
        Assert.Equal(10, f.Events.DeathDamage);
        Assert.Empty(f.Events.Buffs);
    }

    private sealed class MinimumRandom : Random
    {
        internal int Draws { get; private set; }
        public override int Next(int minValue, int maxValue) { Draws++; return minValue; }
    }

    private sealed class Fixture : IDisposable
    {
        internal ServerPlayerId Id { get; } = new("test:npc-contact-buffs");
        internal EventSink Events { get; } = new();
        internal ServerPlayerAuthority Authority { get; }
        internal PlayerHandle Handle { get; }
        private readonly ServerRuntimeState runtime;
        internal PlayerStateSnapshot Player { get { Assert.True(Authority.TryGet(Handle, out var value)); return value; } }
        internal Fixture()
        {
            var slots = new PlayerSlotPool(2);
            var identities = new ServerPlayerSlotRegistry(slots);
            var states = new ServerPlayerStateStore(identities, slots.Capacity);
            Authority = new ServerPlayerAuthority(states, identities, events: Events);
            var created = Authority.Create(Id, 160f, 160f);
            Assert.True(created.IsCreated);
            Handle = created.Player;
            Assert.True(Authority.SetVitals(Id, new ServerPlayerVitalsState(500, 500, 200, 200)));
            runtime = new ServerRuntimeState(serverPlayers: Authority,
                worldTiles: new WorldTileStore(new WorldDimensions(80, 200)),
                townCommerceWorldFacts: default(RuntimeTownCommerceWorldFacts1458));
        }
        internal PlayerDamageCommitResult Hurt(NpcContactBuffSource1458 source, int damage, int hitDirection = 0, long tick = 1) =>
            Authority.TryCommitAuthoritativeNpcContactDamage(tick, new NpcHandle(0, new NpcGeneration(1)), Handle,
                damage, hitDirection, VanillaPlayerImmunityChannel1458.General, false, false, out _, source);
        internal void Tick(int count) { for (int i = 0; i < count; i++) runtime.Tick(); }
        public void Dispose() => Authority.Despawn(Id);
    }

    private sealed class EventSink : IRuntimeServerPlayerEventSink
    {
        internal Action? Observe { get; set; }
        internal BuffTypeId[] Buffs { get; private set; } = [];
        internal DamageSource DeathSource { get; private set; }
        internal int DeathDamage { get; private set; }
        internal int BuffPublications { get; private set; }
        internal int VitalsPublications { get; private set; }
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs) { Buffs = buffs.ToArray(); BuffPublications++; }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState vitals) { VitalsPublications++; Observe?.Invoke(); }
        public void ServerPlayerMoved(in PlayerStateSnapshot player) => Observe?.Invoke();
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId projectileType, int damage, int hitDirection)
        { DeathSource = source; DeathDamage = damage; }
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState appearance) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool hostile) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool enabled) { }
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) { }
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short animationTicks) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short floorX, short floorY) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
}
