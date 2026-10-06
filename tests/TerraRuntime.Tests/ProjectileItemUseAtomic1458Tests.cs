using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class ProjectileItemUseAtomic1458Tests
{
    [Fact]
    public void Packet5_then13_then27_commits_source_minishark_ammo_rng_and_trusted_generation()
    {
        using var f = new Fixture();
        var expected = f.Random.Clone();
        bool conserved = expected.Next(3) == 0;
        f.Shoot(f.Bullet());
        Assert.True(f.Store.TryGetActive(0, out var shot));
        Assert.True(f.Store.IsCombatTrusted(shot.Handle));
        Assert.Equal(new ProjectileGeneration(1), shot.Handle.Generation);
        Assert.Equal((short)13, shot.Damage);
        Assert.Equal(2f, shot.KnockBack);
        Assert.Equal(11f, shot.VelocityX);
        Assert.Equal(conserved ? 5 : 4, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(expected));
        Assert.Equal(1335025742, f.Random.Clone().Next());
    }

    [Fact]
    public void Full_small_pool_rejection_preserves_rng_ammo_generation_and_next_shot_cadence()
    {
        using var f = new Fixture(capacity: 1);
        ProjectileSnapshot occupied = f.Fill();
        var before = f.Random.Clone();
        var ammo = f.Ammo;
        f.Shoot(f.Bullet());
        Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
        Assert.True(f.Store.TryGet(occupied.Handle, out var retained));
        Assert.Equal(occupied, retained);
        Assert.Equal(ammo, f.Ammo);
        Assert.True(f.Random.HasSameState(before));
        Assert.True(f.Store.TryDespawn(occupied.Handle, out _));
        f.Shoot(f.Bullet());
        Assert.True(f.Store.TryGetActive(0, out var accepted));
        Assert.Equal(new ProjectileGeneration(2), accepted.Handle.Generation);
        Assert.True(f.Store.IsCombatTrusted(accepted.Handle));
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
    }

    [Fact]
    public void Tick_callback_changed_inventory_equipment_or_player_rejects_without_overwriting_new_state()
    {
        foreach (string dependency in new[] { "inventory", "equipment", "player" })
        {
            using var f = new Fixture();
            var random = f.Random.Clone();
            PlayerStateSnapshot? changedPlayer = null;
            f.BeforeTick = () =>
            {
                if (dependency == "inventory") f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 99);
                if (dependency == "equipment") f.SetItem(VanillaPlayerItemSlotCatalog.ArmorStart, 696, 1);
                if (dependency == "player") f.Move(101f);
                Assert.True(f.Players.TryCapture(f.Connection.Player, out var state));
                changedPlayer = state;
            };
            f.Shoot(f.Bullet());
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.True(f.Random.HasSameState(random));
            Assert.Equal(dependency == "inventory" ? 99 : 5, f.Ammo.Stack);
            Assert.True(f.Players.TryCapture(f.Connection.Player, out var final));
            Assert.Equal(changedPlayer, final);
            Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
            Assert.Equal(dependency == "equipment" ? 5 : 0, combat.Defense);
        }
    }

    [Fact]
    public void Tick_callback_reconnect_cannot_transfer_an_old_shot_into_reused_player_slot()
    {
        using var f = new Fixture();
        var before = f.Random.Clone();
        ConnectionHandle old = f.Connection;
        f.BeforeTick = f.Reconnect;
        f.Shoot(f.Bullet(), old);
        Assert.NotEqual(old.Player, f.Connection.Player);
        Assert.Equal(old.Player.Slot, f.Connection.Player.Slot);
        Assert.False(f.Players.IsCurrent(old));
        Assert.Equal(0, f.Store.ActiveCount);
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(before));
        f.Shoot(f.Bullet());
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
    }

    [Fact]
    public void Tick_callback_changed_shared_rng_is_preserved_instead_of_adopting_stale_conservation()
    {
        using var f = new Fixture();
        var expected = f.Random.Clone();
        expected.Next(100);
        f.BeforeTick = () => f.Random.Next(100);
        f.Shoot(f.Bullet());
        Assert.Equal(0, f.Store.ActiveCount);
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(expected));
    }

    [Fact]
    public void First_outward_inventory_callback_sees_complete_acceptance_and_cannot_overwrite_its_own_change()
    {
        using var f = new Fixture();
        var expected = f.Random.Clone();
        expected.Next(3);
        int observations = 0;
        f.Events.OnEquipment = () =>
        {
            observations++;
            Assert.Equal(4, f.Ammo.Stack);
            Assert.True(f.Random.HasSameState(expected));
            Assert.True(f.Store.TryGetActive(0, out var shot));
            Assert.True(f.Store.IsCombatTrusted(shot.Handle));
            f.Shoot(f.Bullet(702));
            Assert.Equal(1, f.Store.ActiveCount);
            f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 99);
        };
        f.Shoot(f.Bullet());
        Assert.Equal(1, observations);
        Assert.Equal(99, f.Ammo.Stack);
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.True(f.Random.HasSameState(expected));
    }

    [Fact]
    public void Celebration_only_first_child_picks_ammo_and_capacity_failure_does_not_consume_volley()
    {
        using var f = new Fixture(capacity: 1);
        f.SetItem(0, VanillaItemIds.CelebrationMk2.Value, 1);
        f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, VanillaItemIds.RocketIV.Value, 5);
        ProjectileSnapshot filler = f.Fill();
        var before = f.Random.Clone();
        f.Shoot(f.Celebration(701, 0));
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(before));
        Assert.True(f.Store.TryDespawn(filler.Handle, out _));
        var expected = before.Clone();
        bool conserved = expected.Next(2) == 0;
        f.Shoot(f.Celebration(701, 0));
        Assert.Equal(conserved ? 5 : 4, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(expected));
        Assert.True(f.Store.TryGetActive(0, out var first));
        f.Shoot(f.Celebration(702, 1));
        Assert.True(f.Random.HasSameState(expected));
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.True(f.Store.TryDespawn(first.Handle, out _));
        f.Shoot(f.Celebration(702, 1));
        Assert.Equal(2, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(conserved ? 5 : 4, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(expected));
    }

    [Fact]
    public void Endless_bullet_and_quiver_arrow_still_execute_their_source_conservation_draws()
    {
        foreach (bool quiver in new[] { false, true })
        {
            using var f = new Fixture();
            var expected = f.Random.Clone();
            TerrariaProjectileUpdateState packet;
            if (quiver)
            {
                f.SetItem(0, 39, 1);
                f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 3103, 1);
                f.SetItem((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), 1321, 1);
                packet = f.Packet(701, 1, 9, 2f, 10.01f);
                expected.Next(5);
            }
            else
            {
                f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 3104, 1);
                packet = f.Bullet();
                expected.Next(3);
            }
            f.Shoot(packet);
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(1, f.Ammo.Stack);
            Assert.True(f.Random.HasSameState(expected));
        }
    }

    [Fact]
    public void Source_born_and_reported_buffs_are_supported_but_imported_unknown_buffs_are_untrusted()
    {
        foreach (int buff in new[] { 0, 93, 112, -1 })
        {
            using var f = new Fixture();
            if (buff > 0)
                f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                    new PlayerBuffTypesCommitRequest(f.Connection.Player.Slot, new[] { new BuffTypeId(buff) })));
            if (buff == -1) f.ImportUnknownBuffs();
            var before = f.Random.Clone();
            f.Shoot(f.Bullet());
            Assert.True(f.Store.TryGetActive(0, out var shot));
            Assert.Equal(buff != -1, f.Store.IsCombatTrusted(shot.Handle));
            if (buff == -1)
            {
                Assert.Equal(5, f.Ammo.Stack);
                Assert.True(f.Random.HasSameState(before));
            }
            else
            {
                bool conserve = buff > 0 && before.Next(5) == 0;
                conserve |= before.Next(3) == 0;
                Assert.Equal(conserve ? 5 : 4, f.Ammo.Stack);
                Assert.True(f.Random.HasSameState(before));
            }
        }
    }

    [Fact]
    public void Standalone_and_mana_use_share_atomic_acceptance_without_ammo_rng_or_metal_attack_changes()
    {
        foreach (bool magic in new[] { false, true })
        {
            using var f = new Fixture();
            f.SetItem(0, magic ? VanillaItemIds.MagicMissile.Value : 42, magic ? (short)1 : (short)5);
            f.SetItem(VanillaPlayerItemSlotCatalog.ArmorStart, 696, 1);
            f.SetItem((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 1), 697, 1);
            f.SetItem((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 2), 698, 1);
            f.Move(100f, magic ? (byte)0x20 : (byte)0);
            f.Players.TryApply(new PlayerManaRuntimeCommand(f.Connection,
                new PlayerManaCommitRequest(f.Connection.Player.Slot, 50, 50)));
            var before = f.Random.Clone();
            f.Shoot(magic ? f.Packet(701, VanillaProjectileIds.MagicMissile.Value, 35, 7.5f, 6f) :
                f.Packet(701, 3, 10, 0f, 9f));
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
            Assert.True(f.Store.TryGetActive(0, out var shot));
            Assert.True(f.Store.IsCombatTrusted(shot.Handle));
            Assert.True(f.Random.HasSameState(before));
            Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
            Assert.Equal(20, combat.Defense);
            Assert.True(f.Players.TryCapture(f.Connection.Player, out var player));
            Assert.Equal(magic ? 36 : 50, player.Mana);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var weapon));
            Assert.Equal(magic ? 1 : 4, weapon.Stack);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession session = null!;
        private long sourceId = 8800;
        public readonly VanillaUnifiedRandom1458 Random = new(1458);
        public readonly Events Events = new();
        public readonly PlayerAuthority Players;
        public readonly RuntimeProjectileStore Store;
        public readonly ProjectileAuthority Authority;
        public ConnectionHandle Connection { get; private set; }
        public Action? BeforeTick;
        public RuntimePlayerInventoryItem Ammo
        {
            get
            {
                Assert.True(Players.TryGetInventoryItem(Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var ammo));
                return ammo;
            }
        }

        public Fixture(int capacity = 4)
        {
            Players = new PlayerAuthority(Events, null);
            var replication = new RuntimeProjectileReplicationRegistry();
            Store = new RuntimeProjectileStore(capacity, replication);
            Authority = new ProjectileAuthority(Store, Players, new RuntimeNpcStore(),
                new RuntimePlayerSnapshotLookup(Players, null), null, replication,
                () => { Action? callback = BeforeTick; BeforeTick = null; callback?.Invoke(); return 100; },
                projectileRandom: Random);
            Spawn();
        }

        private void Spawn()
        {
            Assert.True(slots.TryAcquireConnection(out var lease));
            session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new ConnectionHandle(GameCommandSourceId.FromConnection(++sourceId), session.Handle);
            Assert.True(Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0))));
            SetItem(VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0);
            SetItem(0, 98, 1);
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 5);
            Move(100f);
        }

        public void Reconnect()
        {
            Assert.True(Players.TryApply(new PlayerDisconnectRuntimeCommand(Connection)));
            session.Dispose();
            Spawn();
        }

        public void ImportUnknownBuffs()
        {
            var completion = new TaskCompletionSource<RuntimePlayerTransferState?>();
            Players.TryApply(new PlayerTransferDetachRuntimeCommand(Connection, completion));
            RuntimePlayerTransferState transfer = Assert.IsType<RuntimePlayerTransferState>(completion.Task.Result);
            var attached = new TaskCompletionSource<bool>();
            Players.TryApply(new PlayerTransferAttachRuntimeCommand(Connection, transfer with { BuffTypes = null },
                100, 100, false, true, attached));
            Assert.True(attached.Task.Result);
            Move(100f);
        }

        public void SetItem(short slot, int id, short stack) => Players.TryApply(new PlayerEquipmentRuntimeCommand(
            Connection, new PlayerEquipmentCommitRequest(Connection.Player.Slot, slot, stack, 0, checked((short)id), 0)));

        public void Move(float x, byte control = 0) => Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
            new PlayerMovementCommitRequest(Connection.Player.Slot, control, 0, 0, 0, 0, x, 100f,
                false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));

        public TerrariaProjectileUpdateState Packet(ushort key, int type, short damage, float knockBack, float speed) =>
            new(new TerrariaProjectileKeyState(Connection.Player.Slot.Value, key, 1),
                type, 120f, 100f, speed, 0f, 0f, 0f, 0f, 0, damage, knockBack, 0);

        public TerrariaProjectileUpdateState Bullet(ushort key = 701) => Packet(key, 14, 13, 2f, 11f);

        public TerrariaProjectileUpdateState Celebration(ushort key, float child) =>
            Packet(key, VanillaProjectileIds.CelebrationRocketIV.Value, 115, 16f,
                VanillaExplosiveProjectileFacts1458.GetCelebrationLaunchSpeed(4)) with { Ai0 = 4f, Ai1 = child };

        public void Shoot(TerrariaProjectileUpdateState packet, ConnectionHandle? connection = null) =>
            Assert.True(Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(connection ?? Connection, packet)));

        public ProjectileSnapshot Fill()
        {
            var state = new ProjectileStateUpdate(new ProjectileTypeId(1), 0, 200f, 200f, 0f, 0f, default, 0, 1, 0f, 0);
            Assert.True(Store.TrySpawn(0, state, out var shot));
            return shot;
        }

        public void Dispose() => session.Dispose();
    }

    private sealed class Events : IRuntimePlayerEventSink
    {
        public Action? OnEquipment;
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request)
        {
            Action? callback = OnEquipment;
            OnEquipment = null;
            callback?.Invoke();
        }
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
    }
}
