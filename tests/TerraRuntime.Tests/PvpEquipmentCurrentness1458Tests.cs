using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.HostContracts;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class PvpEquipmentCurrentness1458Tests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Projectile_hit_rejects_appearance_or_equipment_changed_during_damage_roll(bool explosion, bool equipment)
    {
        using var f = new Fixture();
        var store = new RuntimeProjectileStore(2);
        var state = new ProjectileStateUpdate(new ProjectileTypeId(explosion ? 134 : 1), f.Owner.Player.Slot.Value,
            100, 100, 1, 0, default, 0, 20, 0, 20);
        Assert.True(store.TrySpawn(0, in state, out var shot));
        Assert.True(store.TryMarkCombatTrusted(shot.Handle, f.Owner.Player));
        var random = new CallbackRandom(() => f.ChangeOwner(equipment));
        var pass = new RuntimeProjectilePlayerCombatPass(store, new RuntimeNpcStore(), f.Players, () => 1, random);
        var before = f.TargetSnapshot;
        if (explosion)
        {
            Assert.True(store.TryDespawn(shot.Handle, out _));
            pass.Tick([new RuntimeProjectileExplosionEvent(shot, f.Owner.Player, default, 90, 90, 100, 100)]);
        }
        else pass.Tick([]);
        Assert.True(random.Called);
        Assert.Equal(before, f.TargetSnapshot);
        Assert.Equal(0, pass.CommittedHits);
        if (!explosion)
        {
            Assert.True(store.TryGet(shot.Handle, out var retained));
            Assert.Equal(shot, retained);
        }
        pass.Tick([]); // A refused offer must not consume the target's projectile immunity.
        if (!explosion) Assert.Equal(1, pass.CommittedHits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Direct_melee_rejects_changed_owner_without_consuming_attack_cadence(bool equipment)
    {
        using var f = new Fixture();
        f.Players.TryApply(new PlayerEquipmentRuntimeCommand(f.Owner,
            new PlayerEquipmentCommitRequest(f.Owner.Player.Slot, 0, 1, 0, 3508, 0)));
        var random = new CallbackRandom(() => f.ChangeOwner(equipment));
        var integrity = new RuntimePvpCombatIntegrity(f.Players, random);
        var reason = new TerrariaPlayerDeathReasonState(f.Owner.Player.Slot.Value, -1, -1, -1, 0, 0, 0, null);
        var wire = new TerrariaPlayerHurtState(f.Target.Player.Slot.Value, reason, 1, 2, 2, -1);
        var before = f.TargetSnapshot;
        Assert.Equal(PvpCombatResolveResult.Rejected, integrity.ResolveClientItemHit(1, f.Owner, in wire, out _));
        Assert.True(random.Called);
        Assert.Equal(before, f.TargetSnapshot);
        Assert.Equal(PvpCombatResolveResult.Accepted, integrity.ResolveClientItemHit(1, f.Owner, in wire, out _));
    }

    [Fact]
    public void Bot_equipment_revision_is_checked_after_the_projectile_damage_roll()
    {
        using var f = new Fixture();
        var registry = new ServerPlayerSlotRegistry(f.Slots);
        var server = new ServerPlayerAuthority(new ServerPlayerStateStore(registry, 3), registry);
        var id = new ServerPlayerId("pvp-currentness");
        var created = server.Create(id, 100, 100);
        Assert.Equal(ServerPlayerCreateStatus.Created, created.Status);
        Assert.True(server.SetHostile(id, true));
        Assert.True(server.TryGet(created.Player, out var owner));
        var store = new RuntimeProjectileStore(2);
        var state = new ProjectileStateUpdate(new ProjectileTypeId(1), owner.Player.Slot.Value,
            100, 100, 1, 0, default, 0, 20, 0, 20);
        Assert.True(store.TrySpawn(0, in state, out var shot));
        Assert.True(store.TryMarkCombatTrusted(shot.Handle, owner.Player));
        var random = new CallbackRandom(() => Assert.True(server.SetItem(id,
            new ServerPlayerItemState(67, VanillaItemIds.None, 0, default, 0))));
        var pass = new RuntimeProjectilePlayerCombatPass(store, new RuntimeNpcStore(), f.Players, () => 1,
            random, serverPlayers: server);
        var before = f.TargetSnapshot;
        pass.Tick([]);
        Assert.True(random.Called);
        Assert.Equal(before, f.TargetSnapshot);
        Assert.Equal(0, pass.CommittedHits);
    }

    private sealed class CallbackRandom(Action callback) : Random
    {
        public bool Called;
        public override int Next(int minValue, int maxValue)
        {
            if (!Called) { Called = true; callback(); }
            return minValue < 0 ? 0 : maxValue - 1;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly PlayerSlotPool Slots = new(3);
        private readonly List<PlayerJoinSession> sessions = [];
        public readonly PlayerAuthority Players = new(null, null);
        public readonly ConnectionHandle Owner;
        public readonly ConnectionHandle Target;
        public PlayerStateSnapshot TargetSnapshot
        { get { Assert.True(Players.TryCapture(Target.Player, out var state)); return state; } }

        public Fixture() { Owner = Spawn(1); Target = Spawn(2); }
        private ConnectionHandle Spawn(long id)
        {
            Assert.True(Slots.TryAcquireConnection(out var lease));
            var session = new PlayerJoinSession(lease!); sessions.Add(session);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(id), session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 10, 10, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerHealthRuntimeCommand(connection, new PlayerHealthCommitRequest(session.Slot, 1000, 1000)));
            Players.TryApply(new PlayerEquipmentRuntimeCommand(connection, new PlayerEquipmentCommitRequest(session.Slot, 59, 0, 0, 0, 0)));
            Players.TryApply(new PlayerPvpToggleRuntimeCommand(connection, true));
            Players.TryApply(new PlayerMovementRuntimeCommand(connection, new PlayerMovementCommitRequest(session.Slot,
                0, 0, 0, 0, 0, 100, 100, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            return connection;
        }
        public void ChangeOwner(bool equipment)
        {
            if (equipment)
                Players.TryApply(new PlayerEquipmentRuntimeCommand(Owner, new PlayerEquipmentCommitRequest(Owner.Player.Slot, 67, 0, 0, 0, 0)));
            else
                Players.TryApply(new PlayerAppearanceRuntimeCommand(Owner,
                    new PlayerAppearanceCommitRequest(Owner.Player.Slot, 0, 0, 0, 0, "owner", 0, 0, 0,
                        default, default, default, default, default, default, default, 0, 0, 1)));
        }
        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
}
