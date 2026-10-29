using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class ServerPlayerElectrified1458Tests
{
    [Theory]
    [InlineData(0, 0f, 15)]
    [InlineData(4, 0f, 3)]
    [InlineData(8, 0f, 3)]
    [InlineData(12, 0f, 3)]
    [InlineData(0, 2.5f, 15)]
    public void Owned_electrified_uses_actual_control_bits_and_preserves_neutral_regen_fraction(int flags, float velocity, int lossTick)
    {
        using var f = new Fixture();
        f.Controls((byte)flags, velocity);
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 60));
        for (int i = 1; i < lossTick; i++) f.Tick();
        Assert.Equal(500, f.Current.Life);
        f.Tick();
        Assert.Equal(499, f.Current.Life);
        Assert.True(f.Current.Debuffs is { Electrified: true });
        Assert.Equal(60 - lossTick, f.Owner.GetNpcContactBuffDuration(f.Player, VanillaBuffIds.Electrified));
    }

    [Fact]
    public void Released_controls_keep_motion_but_use_the_idle_rate_with_the_same_regen_remainder()
    {
        using var f = new Fixture();
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 60));
        f.Controls(8, 0f);
        f.Tick(); f.Tick(); //80/120 from two pressed updates.
        f.Controls(0, 2.5f);
        for (int i = 0; i < 4; i++) f.Tick();
        Assert.Equal(500, f.Current.Life);
        f.Tick(); //The fifth coasting update reaches120; velocity alone must not select40.
        Assert.Equal(499, f.Current.Life);
    }

    [Fact]
    public void Last_duration_update_still_owns_the_effect_before_the_following_update_clears_it()
    {
        using var f = new Fixture();
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 15));
        for (int i = 0; i < 14; i++) f.Tick();
        Assert.Equal(1, f.Owner.GetNpcContactBuffDuration(f.Player, VanillaBuffIds.Electrified));
        Assert.Equal(500, f.Current.Life);
        f.Tick();
        Assert.Equal(0, f.Owner.GetNpcContactBuffDuration(f.Player, VanillaBuffIds.Electrified));
        Assert.Equal(499, f.Current.Life);
        Assert.True(f.Current.Debuffs is { Electrified: true });
        f.Tick();
        Assert.True(f.Current.Debuffs is { Electrified: false });
        Assert.Equal(499, f.Current.Life);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Environment_death_uses_electrified_but_existing_poison_has_source_precedence(bool poisoned)
    {
        using var f = new Fixture();
        Assert.True(f.Owner.SetVitals(f.Id, new(1, 500, 200, 200)));
        f.Controls(12, 0f);
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 30));
        if (poisoned) Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Poisoned, 30));
        f.Tick(); f.Tick();
        Assert.False(f.Current.IsDead);
        f.Tick();
        Assert.True(f.Current.IsDead);
        Assert.Equal(DamageSourceKind.Environment, f.Events.Death.Kind);
        Assert.Equal(poisoned ? EnvironmentDamageCause.Poison : EnvironmentDamageCause.Electrified,
            f.Events.Death.EnvironmentCause);
        Assert.Equal(10, f.Events.DeathDamage);
    }

    [Fact]
    public void Owned_electrified_change_invalidates_a_prepared_hit_without_adopting_its_shadow_health()
    {
        using var f = new Fixture();
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 5));
        // SetVitals writes the base maximum; the actual player health phase owns
        // the derived500 maximum required before an incoming-Hurt preparation.
        f.Owner.TickHealthContext(f.Tiles);
        Assert.Equal(500, f.Current.DerivedLifeMax);
        var shot = new ProjectileSnapshot(new(0, new(1)), new(1), VanillaProjectileIds.MartianTurretBolt,
            255, 160f, 160f, 0f, 0f, default, 0, 30, 0f, 0);
        var world = new VanillaProjectileWorldStateStepper(f.Tiles);
        Assert.True(world.TryCaptureGroundCombatTerrain(in shot, out var terrain));
        Assert.True(f.Owner.TryPrepareGroundCombatHits(in shot, new(3, new(1)), 1, false, false, false,
            terrain, out var preparation));
        var prepared = preparation!;
        Assert.True(VanillaDefinitionCatalog.TryGet(shot.Type, out var definition));
        var random = new VanillaUnifiedRandom1458(1458);
        Assert.True(prepared.TryStageTarget(f.Player.Slot.Value, in shot, in definition, random, out bool contact));
        Assert.True(contact);
        var unchanged = f.Current;
        Assert.True(f.Owner.TryApplyNpcContactBuff(f.Player, VanillaBuffIds.Electrified, 60));
        Assert.False(prepared.IsCurrent);
        Assert.False(prepared.TryAdoptUnpublished());
        prepared.Publish();
        Assert.Equal(unchanged, f.Current);
        Assert.Equal(60, f.Owner.GetNpcContactBuffDuration(f.Player, VanillaBuffIds.Electrified));
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly ServerPlayerId Id = new("test:electrified-server");
        internal readonly Events Events = new();
        internal readonly WorldTileStore Tiles = new(new(80, 200));
        internal readonly ServerPlayerStateStore States;
        internal readonly ServerPlayerAuthority Owner;
        internal readonly PlayerHandle Player;
        private long tick;
        internal PlayerStateSnapshot Current { get { Assert.True(Owner.TryGet(Player, out var p)); return p; } }
        internal Fixture()
        {
            var slots = new PlayerSlotPool(2);
            var identities = new ServerPlayerSlotRegistry(slots);
            States = new(identities, slots.Capacity);
            Owner = new(States, identities, events: Events);
            var created = Owner.Create(Id, 160f, 160f);
            Assert.True(created.IsCreated);
            Player = created.Player;
            Assert.True(Owner.SetVitals(Id, new(500, 500, 200, 200)));
        }
        internal void Controls(byte flags, float velocity)
        {
            var before = Current;
            Assert.True(States.TrySetMotion(Player, before.PositionX, before.PositionY, velocity, 0f,
                (byte)(64 | flags), out _));
        }
        internal void Tick()
        {
            Owner.TickBuffs();
            Owner.TickLava(++tick, Tiles, false, false, false);
            Owner.TickHealthContext(Tiles);
        }
        public void Dispose() => Owner.Despawn(Id);
    }

    private sealed class Events : IRuntimeServerPlayerEventSink
    {
        internal DamageSource Death;
        internal int DeathDamage;
        public void ServerPlayerDied(PlayerHandle player, DamageSource source, ProjectileTypeId projectileType, int damage, int hitDirection)
        { Death = source; DeathDamage = damage; }
        public void ServerPlayerCreated(in PlayerStateSnapshot player) { }
        public void ServerPlayerMoved(in PlayerStateSnapshot player) { }
        public void ServerPlayerVitalsUpdated(PlayerHandle player, in ServerPlayerVitalsState vitals) { }
        public void ServerPlayerAppearanceUpdated(PlayerHandle player, in ServerPlayerAppearanceState appearance) { }
        public void ServerPlayerPvpUpdated(PlayerHandle player, bool hostile) { }
        public void ServerPlayerGodModeUpdated(PlayerHandle player, bool enabled) { }
        public void ServerPlayerItemUpdated(PlayerHandle player, in ServerPlayerItemState item) { }
        public void ServerPlayerBuffTypesUpdated(PlayerHandle player, ReadOnlySpan<BuffTypeId> buffs) { }
        public void ServerPlayerItemUsePresented(PlayerHandle player, float rotation, short animationTicks) { }
        public void ServerPlayerRecallPresented(in PlayerStateSnapshot player, short x, short y) { }
        public void ServerPlayerDespawned(PlayerHandle player) { }
    }
}
