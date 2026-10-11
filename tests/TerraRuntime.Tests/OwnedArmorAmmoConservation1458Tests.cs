using System.IO.Compression;
using System.Text.Json;
using System.Runtime.CompilerServices;
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
using Xunit;

namespace TerraRuntime.Tests;

public sealed class OwnedArmorAmmoConservation1458Tests
{
    [Fact]
    public void Equipped_armor_roles_reach_owned_projectile_conservation_without_claiming_generic_ammo_funding()
    {
        // Actual owned phase and accepted projectile;
        // Independent original PickAmmo rows prove debit/RNG only.
        // Projectile RNG is configured separately at this paid-shoot boundary, not original whole Main.
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var compressed = typeof(OwnedArmorAmmoConservation1458Tests).Assembly.GetManifestResourceStream("ArmorAmmoUnassisted" + platform + "First1458")!;
            using var stream = new GZipStream(compressed, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(stream);
            foreach (var row in source.RootElement.EnumerateArray().Where(r => r.GetProperty("piece").GetInt32() > 0 && r.GetProperty("weapon").GetInt32() == 39 && r.GetProperty("configuredFlagMask").GetInt32() == -1))
            {
                int piece = row.GetProperty("piece").GetInt32();
                short role = (short)(piece == 1002 ? 59 : 60);
                using var f = new Fixture(row.GetProperty("seed").GetInt32());
                f.SetItem(role, piece, 1);
                f.State.Tick();
                Assert.True(f.Players.TryGet(f.Connection, out var member));
                Assert.NotNull(member.ItemPhase?.DerivedCombat);
                var expected = row.GetProperty("beforeCombat");
                var combat = member.ItemPhase!.Value.DerivedCombat!.Value;
                Assert.Equal(expected.GetProperty("huntressAmmoCost90").GetBoolean(), combat.HuntressAmmoCost90);
                Assert.Equal(expected.GetProperty("chloroAmmoCost80").GetBoolean(), combat.ChloroAmmoCost80);
                Assert.Equal(expected.GetProperty("ammoCost80").GetBoolean(), combat.AmmoCost80);
                Assert.Equal(expected.GetProperty("ammoCost75").GetBoolean(), combat.AmmoCost75);
                Assert.Equal(expected.GetProperty("rangedDamage").GetSingle(), combat.RangedDamage);
                Assert.Equal(expected.GetProperty("rangedCrit").GetInt32(), combat.RangedCrit);
                var inventory = Access.Inventory(f.Players);
                short[] funding = new short[59];
                funding[54] = 1;
                inventory.AdoptFundedQuantities(f.Connection, funding);
                Assert.True(VanillaOrdinaryBowLaunch1458.TryResolve(new ItemTypeId(39), default, new ItemTypeId(40), in combat, out var launch));
                var packet = f.Packet(701, row.GetProperty("projectile").GetInt32(), (short)launch.Damage, launch.KnockBack, launch.Speed) with { PositionX = member.PositionX + 5f, PositionY = member.PositionY + 16f };
                f.Shoot(packet);
                Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                Assert.Equal(row.GetProperty("after").GetProperty("inventory")[54].GetProperty("stack").GetInt32(), f.Ammo.Stack);
                Assert.Equal(row.GetProperty("afterRandom").GetProperty("state").EnumerateArray().Select(v => v.GetInt32()).ToArray(), Access.SeedArray(f.Random));
                Assert.Equal(row.GetProperty("afterRandom").GetProperty("cursor").GetUInt32(), Access.Cursor(f.Random));
                Assert.Equal(row.GetProperty("afterNext").GetInt32(), f.Random.Clone().Next());
                short remainingFunding = inventory.CaptureFundedQuantities(f.Connection)[54];
                Assert.True(remainingFunding <= 1);
                Assert.True(remainingFunding <= f.Ammo.Stack);
            }
        }
        // Standalone packets are validated against live equipment; their remote item clocks are outside this proof.
        // The independent original packet27 publication supplies the exact pre-consumption RNG checkpoint.
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using var compressed = typeof(OwnedArmorAmmoConservation1458Tests).Assembly.GetManifestResourceStream("StandaloneArmorCheckpoint" + platform + "1458")!;
            using var stream = new GZipStream(compressed, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(stream);
            foreach (var row in source.RootElement.EnumerateArray())
            {
                using var f = new Fixture(row.GetProperty("seed").GetInt32());
                int piece = row.GetProperty("piece").GetInt32();
                if (piece != 0)
                    f.SetItem((short)(piece == 1002 ? 59 : 60), piece, 1);
                f.State.Tick();
                int itemId = row.GetProperty("itemId").GetInt32();
                f.SetItem(0, itemId, 3);
                f.SetItem(54, 0, 0);
                Assert.True(f.Players.TryGet(f.Connection, out var member));
                Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
                Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetStandaloneWeapon(new ItemTypeId(itemId), out var weapon));
                int damage = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneDamage(in weapon, in combat);
                Assert.Equal(row.GetProperty("shots")[0].GetProperty("damage").GetInt32(), damage);
                var envelope = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneLaunchSpeedEnvelope(in weapon);
                var checkpoint = row.GetProperty("consumptionBeforeRandom");
                checkpoint.GetProperty("state").EnumerateArray().Select(v => v.GetInt32()).ToArray().CopyTo(Access.SeedArray(f.Random), 0);
                Access.Cursor(f.Random) = checkpoint.GetProperty("cursor").GetUInt32();
                Assert.Equal(row.GetProperty("consumptionBeforeNext").GetInt32(), f.Random.Clone().Next());
                var inventory = Access.Inventory(f.Players);
                short[] funding = new short[59];
                funding[0] = 1;
                inventory.AdoptFundedQuantities(f.Connection, funding);
                var packet = f.Packet(703, weapon.ProjectileType.Value, (short)damage, weapon.BaseKnockBack, envelope.CanonicalMagnitude)
                    with
                { PositionX = member.PositionX + 10f, PositionY = member.PositionY + 21f };
                f.Shoot(packet);
                Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var selected));
                int expectedStack = row.GetProperty("after").GetProperty("inventory")[0].GetProperty("stack").GetInt32();
                Assert.Equal(expectedStack, selected.Stack);
                Assert.Equal((short)(expectedStack == 3 ? 1 : 0), inventory.CaptureFundedQuantities(f.Connection)[0]);
                Assert.Equal(row.GetProperty("afterRandom").GetProperty("state").EnumerateArray().Select(v => v.GetInt32()).ToArray(), Access.SeedArray(f.Random));
                Assert.Equal(row.GetProperty("afterRandom").GetProperty("cursor").GetUInt32(), Access.Cursor(f.Random));
                Assert.Equal(row.GetProperty("afterNext").GetInt32(), f.Random.Clone().Next());
            }
        }
        foreach (var (piece, role) in new[] { (1002, 59), (3804, 60), (4983, 60), (3878, 60), (1549, 60), (2758, 60) })
        {
            using var f = new Fixture(7);
            f.SetItem((short)role, piece, 1);
            f.State.Tick();
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            Assert.NotNull(member.ItemPhase?.DerivedCombat);
            f.SetItem((short)role, 0, 0);
            f.State.Tick();
            Assert.True(f.Players.TryGet(f.Connection, out member));
            var reset = member.ItemPhase!.Value.DerivedCombat!.Value;
            Assert.False(reset.HuntressAmmoCost90 || reset.ChloroAmmoCost80 || reset.AmmoCost80 || reset.AmmoCost75);
            f.SetItem((short)(role == 59 ? 60 : 59), piece, 1);
            f.State.Tick();
            Assert.True(f.Players.TryGet(f.Connection, out member));
            Assert.Null(member.ItemPhase);
        }
        using (var f = new Fixture(7))
        {
            f.SetItem(59, 2757, 1);
            f.SetItem(60, 2758, 1);
            f.SetItem(61, 2759, 1);
            f.State.Tick();
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            Assert.Null(member.ItemPhase);
            Assert.True(f.Random.HasSameState(new VanillaUnifiedRandom1458(7)));
        }
        using (var f = new Fixture(0))
        {
            f.SetItem(60, 3804, 1);
            f.State.Tick();
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            var combat = member.ItemPhase!.Value.DerivedCombat!.Value;
            Assert.True(VanillaOrdinaryBowLaunch1458.TryResolve(new ItemTypeId(39), default, new ItemTypeId(40), in combat, out var launch));
            var packet = f.Packet(702, 1, (short)launch.Damage, launch.KnockBack, launch.Speed) with { PositionX = member.PositionX + 5f, PositionY = member.PositionY + 16f };
            f.BeforeTick = () => f.SetItem(60, 0, 0);
            f.Shoot(packet);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal((short)3, f.Ammo.Stack);
            Assert.True(f.Random.HasSameState(new VanillaUnifiedRandom1458(0)));
            Assert.False(f.Players.TryGetInventoryItem(f.Connection, 60, out _));
        }
        using (var f = new Fixture(0))
        {
            f.SetItem(60, 3804, 1);
            f.SetItem(0, 279, 3);
            Assert.True(f.Players.TryGet(f.Connection, out var member));
            Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetStandaloneWeapon(new ItemTypeId(279), out var weapon));
            int damage = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneDamage(in weapon, in combat);
            var envelope = VanillaProjectileWeaponCombatCatalog.ResolveStandaloneLaunchSpeedEnvelope(in weapon);
            var packet = f.Packet(704, weapon.ProjectileType.Value, (short)damage, weapon.BaseKnockBack, envelope.CanonicalMagnitude)
                with
            { PositionX = member.PositionX + 10f, PositionY = member.PositionY + 21f };
            var beforeRandom = f.Random.Clone();
            var inventory = Access.Inventory(f.Players);
            short[] funding = new short[59];
            funding[0] = 1;
            inventory.AdoptFundedQuantities(f.Connection, funding);
            f.BeforeTick = () => f.SetItem(60, 0, 0);
            f.Shoot(packet);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.True(f.Players.TryGetInventoryItem(f.Connection, 0, out var selected));
            Assert.Equal((short)3, selected.Stack);
            Assert.Equal((short)1, inventory.CaptureFundedQuantities(f.Connection)[0]);
            Assert.True(f.Random.HasSameState(beforeRandom));
            Assert.False(f.Players.TryGetInventoryItem(f.Connection, 60, out _));
        }
    }

    private static class Access
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "seedArray")]
        internal static extern ref int[] SeedArray(VanillaUnifiedRandom1458 random);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inext")]
        internal static extern ref uint Cursor(VanillaUnifiedRandom1458 random);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_runtime")]
        internal static extern ref ServerRuntimeComposition Runtime(ServerRuntimeState state);
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "inventory")]
        internal static extern ref RuntimePlayerInventoryStore Inventory(PlayerAuthority players);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerSlotPool slots = new(1);
        private PlayerJoinSession session = null!;
        private long connectionId = 8900;
        public readonly VanillaUnifiedRandom1458 Random;
        public readonly PlayerAuthority Players;
        public readonly ServerRuntimeState State;
        public readonly RuntimeProjectileStore Store;
        public readonly ProjectileAuthority Authority;
        public ConnectionHandle Connection
        {
            get;
            private set;
        }
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
            Random = new VanillaUnifiedRandom1458(seed);
            var tiles = new WorldTileStore(new WorldDimensions(300, 200));
            State = new ServerRuntimeState(worldTiles: tiles, playerUpdateRandomSeed: new(0));
            Players = Access.Runtime(State).Players;
            Players.SetPlayerUpdateWorldFacts(new(300, 200, 80, false, false));
            var replication = new RuntimeProjectileReplicationRegistry();
            Store = new RuntimeProjectileStore(capacity, replication);
            Authority = new ProjectileAuthority(Store, Players, new RuntimeNpcStore(),
                new RuntimePlayerSnapshotLookup(Players, null), null, replication,
                () =>
                {
                    Action? callback = BeforeTick;
                    BeforeTick = null;
                    callback?.Invoke();
                    return 100;
                },
                projectileRandom: Random);
            Players.SetRemotePlayerEnvironment(new(false, false), Store);
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
            SetItem(0, 39, 1);
            SetItem(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 40, 3);
            Move();
        }

        private void Move() => Players.TryApply(new PlayerMovementRuntimeCommand(Connection,
            new PlayerMovementCommitRequest(Connection.Player.Slot, 0, 0, 0, 0, 0, 1600f, 1600f,
                false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));

        public void SetItem(short slot, int id, short stack) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new PlayerEquipmentCommitRequest(Connection.Player.Slot, slot, stack, 0, checked((short)id), 0)));

        public TerrariaProjectileUpdateState Packet(ushort key, int type, short damage, float knockBack, float speed) =>
            new(new TerrariaProjectileKeyState(Connection.Player.Slot.Value, key, 1),
                type, 1620f, 1600f, speed, 0f, 0f, 0f, 0f, 0, damage, knockBack, 0);

        public void Shoot(TerrariaProjectileUpdateState packet) =>
            Assert.True(Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(Connection, packet)));

        public void Dispose() => session.Dispose();
    }

}
