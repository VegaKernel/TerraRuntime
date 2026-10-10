using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class AmmoBuffItemUseAtomic1458Tests
{
    [Fact]
    public void Packet50_then27_preserves_pickammo_goldens_and_matches_coupled_minishark_launch()
    {
        using JsonDocument facts = Facts();
        Assert.Equal(32, facts.RootElement.GetProperty("shots").GetArrayLength());
        foreach (JsonElement row in facts.RootElement.GetProperty("shots").EnumerateArray())
        {
            using var f = new Fixture(row.GetProperty("seed").GetInt32());
            f.Configure(row);
            f.Shoot(f.Packet(row));
            Assert.Equal(row.GetProperty("afterStack").GetInt32(), f.Ammo.Stack);
            Assert.Equal(row.GetProperty("weapon").GetInt32() == 98 && row.GetProperty("canShoot").GetBoolean()
                ? LaunchRow(row.GetProperty("seed").GetInt32(), row.GetProperty("ammo").GetInt32(),
                    row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean()).GetProperty("next").GetInt32()
                : row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            foreach (int buff in row.GetProperty("buffs").EnumerateArray().Select(v => v.GetInt32()).Distinct())
                Assert.Equal(60, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(buff)));
            if (!row.GetProperty("canShoot").GetBoolean())
            {
                Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
                // No matching ammo cannot produce an authoritative shot. The existing compatibility
                // ingress can still retain packet27 as untrusted presentation, without item-use effects.
                Assert.True(f.Store.TryGetActive(0, out var presentation));
                Assert.False(f.Store.IsCombatTrusted(presentation.Handle));
                continue;
            }
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
            Assert.True(f.Store.TryGetActive(0, out var shot));
            Assert.True(f.Store.IsCombatTrusted(shot.Handle));
            Assert.Equal(row.GetProperty("projectile").GetInt32(), shot.Type.Value);
            Assert.Equal(row.GetProperty("damage").GetInt32(), shot.Damage);
            Assert.Equal(row.GetProperty("knockBack").GetSingle(), shot.KnockBack);
            // The source PickAmmo fixture reports the launcher speed17. Accepted Celebration children
            // use the independently admitted aiStyle167 pattern4 launch speed8, not that launcher value.
            float speed = row.GetProperty("weapon").GetInt32() == 3930 ? 8f : row.GetProperty("speed").GetSingle();
            if (row.GetProperty("weapon").GetInt32() == 98)
            {
                JsonElement launch = LaunchRow(row.GetProperty("seed").GetInt32(), row.GetProperty("ammo").GetInt32(),
                    row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
                Assert.Equal(launch.GetProperty("shots")[0].GetProperty("velocity").GetProperty("X").GetSingle(), shot.VelocityX);
                Assert.Equal(launch.GetProperty("shots")[0].GetProperty("velocity").GetProperty("Y").GetSingle(), shot.VelocityY);
            }
            else Assert.Equal(speed, shot.VelocityX, 3);
        }
    }

    [Fact]
    public void Earlier_conserved_offer_and_endless_ammo_still_execute_all_selected_draws()
    {
        using JsonDocument facts = Facts();
        foreach (string profile in new[] { "minishark-both", "minishark-both-endless", "quiver-duplicates", "celebration-both" })
        {
            JsonElement row = Row(facts, profile, 1);
            using var f = new Fixture(1);
            f.Configure(row);
            f.Shoot(f.Packet(row));
            Assert.Equal(row.GetProperty("initialStack").GetInt32(), f.Ammo.Stack);
            Assert.Equal(row.GetProperty("weapon").GetInt32() == 98
                ? LaunchRow(1, row.GetProperty("ammo").GetInt32(), true, true).GetProperty("next").GetInt32()
                : 1657007234, f.Random.Clone().Next());
            Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        }
    }

    [Fact]
    public void Both_buffs_full_pool_failure_preserves_every_owner_and_does_not_start_cadence()
    {
        using var f = new Fixture(0, capacity: 1);
        f.Buffs(93, 112);
        var state = new ProjectileStateUpdate(new ProjectileTypeId(1), 0, 200f, 200f, 0f, 0f, default, 0, 1, 0f, 0);
        Assert.True(f.Store.TrySpawn(0, state, out var occupied));
        var before = f.Random.Clone();
        f.Shoot(f.Bullet());
        Assert.True(f.Random.HasSameState(before));
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Store.TryGet(occupied.Handle, out var retained));
        Assert.Equal(occupied, retained);
        Assert.True(f.Store.TryDespawn(occupied.Handle, out _));
        f.Shoot(f.Bullet());
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(4, f.Ammo.Stack);
        Assert.Equal(LaunchRow(0, 97, true, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
        Assert.True(f.Store.TryGetActive(0, out var accepted));
        Assert.Equal(new ProjectileGeneration(2), accepted.Handle.Generation);
    }

    [Fact]
    public void Changed_packet50_at_tick_callback_refuses_stale_flags_and_preserves_new_buff_slots()
    {
        using var f = new Fixture(0);
        f.Buffs(93);
        var before = f.Random.Clone();
        f.BeforeTick = () => f.Buffs(112, 112);
        f.Shoot(f.Bullet());
        Assert.Equal(0, f.Store.ActiveCount);
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(before));
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(93)));
        Assert.Equal(60, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(112)));
        f.Shoot(f.Bullet());
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(LaunchRow(0, 97, false, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
    }

    [Fact]
    public void Missing_imported_buff_owner_is_untrusted_until_real_packet50_establishes_known_slots()
    {
        using var f = new Fixture(0);
        f.Transfer(unknownBuffs: true);
        var before = f.Random.Clone();
        f.Shoot(f.Bullet());
        Assert.True(f.Store.TryGetActive(0, out var presentation));
        Assert.False(f.Store.IsCombatTrusted(presentation.Handle));
        Assert.Equal(5, f.Ammo.Stack);
        Assert.True(f.Random.HasSameState(before));
        f.Buffs(93, 112);
        f.Shoot(f.Bullet(702));
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(4, f.Ammo.Stack);
        Assert.Equal(LaunchRow(0, 97, true, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
    }

    [Fact]
    public void Same_generation_transfer_retains_known_flags_and_duplicates_do_not_multiply_draws()
    {
        using var f = new Fixture(1);
        f.Buffs(93, 93, 112, 112);
        ConnectionHandle before = f.Connection;
        f.Transfer(unknownBuffs: false);
        Assert.Equal(before, f.Connection);
        Assert.Equal(60, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(93)));
        Assert.Equal(60, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(112)));
        f.Shoot(f.Bullet());
        Assert.Equal(5, f.Ammo.Stack);
        Assert.Equal(LaunchRow(1, 97, true, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
    }

    [Fact]
    public void Death_clears_nonpersistent_offers_and_reconnect_rejects_old_generation_reports()
    {
        using var f = new Fixture(1);
        f.Buffs(93, 112);
        Assert.Equal(PlayerDamageCommitResult.Committed, f.Players.TryCommitAuthoritativeNpcContactDamage(
            10, new NpcHandle(0, new NpcGeneration(1)), f.Connection.Player, 1000, 1,
            VanillaPlayerImmunityChannel1458.General, out var dead));
        Assert.True(dead.IsDead);
        var before = f.Random.Clone();
        f.Shoot(f.Bullet());
        Assert.Equal(0, f.Store.ActiveCount);
        Assert.True(f.Random.HasSameState(before));
        f.Players.TickHealthContext();
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(93)));
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(112)));
        ConnectionHandle old = f.Connection;
        f.Reconnect();
        Assert.NotEqual(old.Player, f.Connection.Player);
        f.Players.TryApply(new PlayerBuffTypesRuntimeCommand(old,
            new PlayerBuffTypesCommitRequest(old.Player.Slot, new[] { new BuffTypeId(93), new BuffTypeId(112) })));
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(93)));
        f.Shoot(f.Bullet());
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(LaunchRow(1, 97, false, false).GetProperty("next").GetInt32(), f.Random.Clone().Next());
    }

    [Fact]
    public void Earlier_tungsten_ammo_keeps_inventory_priority_and_actual_launch_math()
    {
        using var f = new Fixture(0);
        f.SetItem(0, 95, 1);
        f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 4915, 5);
        f.SetItem((short)(VanillaPlayerItemSlotCatalog.AmmoSlotStart + 1), 97, 99);
        f.Buffs();
        var before = f.Random.Clone();
        f.Shoot(f.Packet(701, 14, 22, 5f, 10.5f));
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.Equal(4, f.Ammo.Stack);
        Assert.True(f.Players.TryGetInventoryItem(f.Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart + 1, out var later));
        Assert.Equal(99, later.Stack);
        Assert.True(f.Random.HasSameState(before));
    }

    [Fact]
    public void First_outward_callback_observes_both_buff_rng_ammo_and_cadence_already_committed()
    {
        using var f = new Fixture(0);
        f.Buffs(93, 112);
        int callbacks = 0;
        f.Events.OnEquipment = () =>
        {
            callbacks++;
            Assert.Equal(4, f.Ammo.Stack);
            Assert.Equal(LaunchRow(0, 97, true, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
            Assert.True(f.Store.TryGetActive(0, out var shot));
            Assert.True(f.Store.IsCombatTrusted(shot.Handle));
            f.Shoot(f.Bullet(702));
            Assert.Equal(1, f.Store.ActiveCount);
            f.Buffs(112);
            f.SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 99);
        };
        f.Shoot(f.Bullet());
        Assert.Equal(1, callbacks);
        Assert.Equal(99, f.Ammo.Stack);
        Assert.Equal(LaunchRow(0, 97, true, true).GetProperty("next").GetInt32(), f.Random.Clone().Next());
        Assert.Equal(0, f.Players.GetBuffDuration(f.Connection.Player, new BuffTypeId(93)));
    }

    private static JsonElement LaunchRow(int seed, int ammo, bool box, bool potion, int use = 1)
    {
        using Stream stream = typeof(AmmoBuffItemUseAtomic1458Tests).Assembly.GetManifestResourceStream("AmmoBuffMinisharkShoot1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using JsonDocument facts = JsonDocument.Parse(gzip);
        return facts.RootElement.GetProperty("shots").EnumerateArray().Single(row =>
            row.GetProperty("seed").GetInt32() == seed && row.GetProperty("ammo").GetInt32() == ammo &&
            row.GetProperty("ammoBox").GetBoolean() == box && row.GetProperty("ammoPotion").GetBoolean() == potion &&
            row.GetProperty("use").GetInt32() == use).Clone();
    }

    private static JsonDocument Facts()
    {
        using Stream stream = typeof(AmmoBuffItemUseAtomic1458Tests).Assembly.GetManifestResourceStream("AmmoConservationBuffs1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static JsonElement Row(JsonDocument facts, string profile, int seed) =>
        facts.RootElement.GetProperty("shots").EnumerateArray().Single(row => row.GetProperty("profile").GetString() == profile &&
            row.GetProperty("seed").GetInt32() == seed);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession session = null!;
        private long connectionId = 8900;
        private readonly int seed;
        public readonly VanillaUnifiedRandom1458 Random;
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
                Assert.True(Players.TryGetInventoryItem(Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var item));
                return item;
            }
        }

        public Fixture(int seed, int capacity = 4)
        {
            this.seed = seed;
            Random = new VanillaUnifiedRandom1458(seed);
            var tiles = new WorldTileStore(new WorldDimensions(300, 200));
            Players = new PlayerAuthority(Events, tiles);
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
            Connection = new ConnectionHandle(GameCommandSourceId.FromConnection(++connectionId), session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session,
                new PlayerSpawnCommitRequest(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new PlayerHealthCommitRequest(session.Slot, 100, 100)));
            SetItem(VanillaPlayerItemSlotCatalog.ArmorStart, 0, 0);
            SetItem(0, 98, 1);
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 5);
            Move();
        }

        private void Move() => Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
            new PlayerMovementCommitRequest(Connection.Player.Slot, 0, 0, 0, 0, 0, 1600f, 1600f,
                false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));

        public void Configure(JsonElement row)
        {
            SetItem(0, row.GetProperty("weapon").GetInt32(), 1);
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, row.GetProperty("ammo").GetInt32(),
                checked((short)row.GetProperty("initialStack").GetInt32()));
            if (row.GetProperty("magicQuiver").GetBoolean())
                SetItem((short)(VanillaPlayerItemSlotCatalog.ArmorStart + 3), 1321, 1);
            Buffs(row.GetProperty("buffs").EnumerateArray().Select(v => v.GetInt32()).ToArray());
            if (row.GetProperty("weapon").GetInt32() == 39 && row.GetProperty("ammo").GetInt32() == 40)
            {
                Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
                    new PlayerMovementCommitRequest(Connection.Player.Slot, 64, 16, 0, 0, 0, 1600f, 1600f,
                        false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));
            }
        }

        public void Buffs(params int[] types) => Players.TryApply(new PlayerBuffTypesRuntimeCommand(Connection,
            new PlayerBuffTypesCommitRequest(Connection.Player.Slot, types.Select(type => new BuffTypeId(type)).ToArray())));

        public void SetItem(short slot, int id, short stack) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new PlayerEquipmentCommitRequest(Connection.Player.Slot, slot, stack, 0, checked((short)id), 0)));

        public TerrariaProjectileUpdateState Packet(ushort key, int type, short damage, float knockBack, float speed) =>
            new(new TerrariaProjectileKeyState(Connection.Player.Slot.Value, key, 1),
                type, 1620f, 1600f, speed, 0f, 0f, 0f, 0f, 0, damage, knockBack, 0);

        public TerrariaProjectileUpdateState Packet(JsonElement row)
        {
            bool celebration = row.GetProperty("weapon").GetInt32() == 3930;
            var packet = Packet(701, row.GetProperty("projectile").GetInt32(),
                checked((short)row.GetProperty("damage").GetInt32()), row.GetProperty("knockBack").GetSingle(),
                celebration ? 8f : row.GetProperty("speed").GetSingle());
            if (row.GetProperty("weapon").GetInt32() == 98 && row.GetProperty("canShoot").GetBoolean())
            {
                JsonElement launch = LaunchRow(seed, row.GetProperty("ammo").GetInt32(),
                    row.GetProperty("ammoBox").GetBoolean(), row.GetProperty("ammoPotion").GetBoolean());
                var velocity = launch.GetProperty("shots")[0].GetProperty("velocity");
                packet = packet with { VelocityX = velocity.GetProperty("X").GetSingle(), VelocityY = velocity.GetProperty("Y").GetSingle() };
            }
            if (row.GetProperty("weapon").GetInt32() == 39 && row.GetProperty("ammo").GetInt32() == 40)
                packet = packet with { PositionX = 1605f, PositionY = 1616f };
            return celebration ? packet with { Ai0 = 4f } : packet;
        }

        public TerrariaProjectileUpdateState Bullet(ushort key = 701)
        {
            bool box = Players.GetBuffDuration(Connection.Player, new BuffTypeId(93)) > 0;
            bool potion = Players.GetBuffDuration(Connection.Player, new BuffTypeId(112)) > 0;
            JsonElement launch = LaunchRow(seed, 97, box, potion);
            var velocity = launch.GetProperty("shots")[0].GetProperty("velocity");
            return Packet(key, 14, 13, 2f, 11f) with
            {
                VelocityX = velocity.GetProperty("X").GetSingle(),
                VelocityY = velocity.GetProperty("Y").GetSingle()
            };
        }

        public void Shoot(TerrariaProjectileUpdateState packet) =>
            Assert.True(Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(Connection, packet)));

        public void Transfer(bool unknownBuffs)
        {
            var detached = new TaskCompletionSource<RuntimePlayerTransferState?>();
            Players.TryApply(new PlayerTransferDetachRuntimeCommand(Connection, detached));
            var transfer = Assert.IsType<RuntimePlayerTransferState>(detached.Task.Result);
            var attached = new TaskCompletionSource<bool>();
            Players.TryApply(new PlayerTransferAttachRuntimeCommand(Connection,
                unknownBuffs ? transfer with { BuffTypes = null } : transfer, 100, 100, false, true, attached));
            Assert.True(attached.Task.Result);
            Move();
        }

        public void Reconnect()
        {
            Players.TryApply(new PlayerDisconnectRuntimeCommand(Connection));
            session.Dispose();
            Spawn();
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
