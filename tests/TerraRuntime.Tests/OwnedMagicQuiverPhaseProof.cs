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

public static class OwnedMagicQuiverPhaseProof
{
    public static void Run()
    {
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            using Stream stream = typeof(OwnedMagicQuiverPhaseProof).Assembly.GetManifestResourceStream($"MagicQuiverWeaponInput{platform}First1458")!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var source = JsonDocument.Parse(gzip);
            int accepted = 0, refused = 0;
            foreach (JsonElement row in source.RootElement.EnumerateArray())
            {
                string mode = row.GetProperty("mode").GetString()!;
                if (mode == "isolated-stages") continue; // No equipped input; original isolated grant-method proof only.
                using var fixture = new Fixture(row.GetProperty("seed").GetInt32());
                fixture.Players.SetCombatEquipmentWorldModes(mode.StartsWith("expert", StringComparison.Ordinal), false);
                var appearance = default(PlayerAppearanceCommitRequest) with
                {
                    PlayerSlot = fixture.Connection.Player.Slot,
                    Name = "Quiver",
                    DifficultyFlags = mode == "expert-heart" ? VanillaPlayerAppearanceNormalizer.ExtraAccessoryDifficultyFlag : (byte)0
                };
                Assert.True(fixture.Players.TryApply(new PlayerAppearanceRuntimeCommand(fixture.Connection, appearance)));
                for (int index = 0; index < 20; index++) Install(fixture, row.GetProperty("armor")[index], (short)(59 + index));
                for (int loadout = 0; loadout < 3; loadout++)
                    for (int index = 0; index < 20; index++)
                        Install(fixture, row.GetProperty("loadouts")[loadout][index], (short)(900 + loadout * 30 + index));
                var playerRandom = PlayerRandom(fixture.Players)!.Clone();
                fixture.State.Tick();
                Assert.True(fixture.Players.TryGet(fixture.Connection, out var member));
                bool excluded = mode is "wrong-head" or "active-blocks-inherited" or "inherited-vanity" ||
                    row.GetProperty("prefix").GetInt32() != 0 && mode is "vanity" or "inherited-vanity";
                if (excluded)
                {
                    Assert.Null(member.ItemPhase);
                    Assert.True(PlayerRandom(fixture.Players)!.HasSameState(playerRandom));
                    refused++;
                    continue;
                }
                Assert.True(member.ItemPhase?.DerivedCombat is not null, $"ownedphase {mode}/prefix{row.GetProperty("prefix").GetInt32()}/seed{row.GetProperty("seed").GetInt32()}");
                var combat = member.ItemPhase!.Value.DerivedCombat!.Value;
                var expected = row.GetProperty("beforeCombat");
                Assert.Equal(expected.GetProperty("magicQuiver").GetBoolean(), combat.MagicQuiver);
                Assert.Equal(expected.GetProperty("statDefense").GetInt32(), combat.Defense);
                Assert.Equal(expected.GetProperty("meleeCrit").GetInt32(), combat.MeleeCrit);
                Assert.Equal(expected.GetProperty("rangedCrit").GetInt32(), combat.RangedCrit);
                Assert.Equal(expected.GetProperty("magicCrit").GetInt32(), combat.MagicCrit);
                foreach (var pair in new (string Name, float Value)[] { ("rangedDamage", combat.RangedDamage), ("meleeDamage", combat.MeleeDamage),
                    ("magicDamage", combat.MagicDamage), ("minionDamage", combat.MinionDamage), ("arrowDamage", combat.ArrowDamage),
                    ("arrowDamageAdditiveStack", combat.ArrowDamageAdditiveStack) })
                    Assert.Equal(expected.GetProperty("floatBits").GetProperty(pair.Name).GetInt32(), BitConverter.SingleToInt32Bits(pair.Value));
                bool resolved = VanillaOrdinaryBowLaunch1458.TryResolve(new(39), default, new(40), in combat, out var launch);
                if (!resolved) launch = new(new(row.GetProperty("projectile").GetInt32()), row.GetProperty("damage").GetInt32(), row.GetProperty("knockback").GetSingle(), row.GetProperty("speed").GetSingle(), 0);
                if (resolved)
                {
                    Assert.Equal(row.GetProperty("damage").GetInt32(), launch.Damage);
                    Assert.Equal(BitConverter.SingleToInt32Bits(row.GetProperty("speed").GetSingle()), BitConverter.SingleToInt32Bits(launch.Speed));
                    Assert.Equal(BitConverter.SingleToInt32Bits(row.GetProperty("knockback").GetSingle()), BitConverter.SingleToInt32Bits(launch.KnockBack));
                }
                short[] funding = new short[59]; funding[54] = 1;
                var inventory = Access.Inventory(fixture.Players);
                inventory.AdoptFundedQuantities(fixture.Connection, funding);
                fixture.Random.SetSeed(row.GetProperty("seed").GetInt32());
                var packet = fixture.Packet(801, row.GetProperty("projectile").GetInt32(), (short)launch.Damage, launch.KnockBack, launch.Speed)
                    with
                { PositionX = member.PositionX + 5f, PositionY = member.PositionY + 16f };
                fixture.Shoot(packet);
                Assert.Equal(1, fixture.Authority.PromotedClientProjectileSpawns);
                for (short slot = 0; slot < 59; slot++)
                {
                    Assert.True(inventory.TryGet(fixture.Connection, slot, out var item));
                    var wanted = row.GetProperty("after").GetProperty("inventory")[slot];
                    Assert.Equal(wanted.GetProperty("type").GetInt32(), (int)item.ItemType.Value);
                    Assert.Equal(wanted.GetProperty("stack").GetInt32(), (int)item.Stack);
                    Assert.Equal(wanted.GetProperty("prefix").GetInt32(), (int)item.Prefix.Value);
                }
                Assert.True(Access.SeedArray(fixture.Random).SequenceEqual(row.GetProperty("afterRandom").GetProperty("state").EnumerateArray().Select(value => value.GetInt32())));
                Assert.Equal(row.GetProperty("afterRandom").GetProperty("cursor").GetUInt32(), Access.Cursor(fixture.Random));
                Assert.Equal(row.GetProperty("afterNext").GetInt32(), fixture.Random.Clone().Next());
                bool consumed = row.GetProperty("before").GetProperty("inventory")[54].GetProperty("stack").GetInt32() != row.GetProperty("after").GetProperty("inventory")[54].GetProperty("stack").GetInt32();
                Assert.Equal((short)(consumed ? 0 : 1), inventory.CaptureFundedQuantities(fixture.Connection)[54]);
                accepted++;
            }
            Console.WriteLine($"Quiver {platform}: accepted={accepted}, bounded refusals={refused}; actual phase +Bow39 paid-boundary source");
        }
    }

    private static void Install(Fixture fixture, JsonElement item, short slot)
    {
        int type = item.GetProperty("type").GetInt32();
        if (type == 0) return;
        fixture.SetItem(slot, type, (short)item.GetProperty("stack").GetInt32(), (byte)item.GetProperty("prefix").GetInt32(),
            item.GetProperty("favorite").GetBoolean() ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "playerUpdateRandom")]
    private static extern ref VanillaUnifiedRandom1458? PlayerRandom(PlayerAuthority owner);

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
        public Action? BeforeTick = null;
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

        public void SetItem(short slot, int id, short stack, byte prefix = 0, byte flags = 0) => Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection,
            new PlayerEquipmentCommitRequest(Connection.Player.Slot, slot, stack, prefix, checked((short)id), flags)));

        public TerrariaProjectileUpdateState Packet(ushort key, int type, short damage, float knockBack, float speed) =>
            new(new TerrariaProjectileKeyState(Connection.Player.Slot.Value, key, 1),
                type, 1620f, 1600f, speed, 0f, 0f, 0f, 0f, 0, damage, knockBack, 0);

        public void Shoot(TerrariaProjectileUpdateState packet) =>
            Assert.True(Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(Connection, packet)));

        public void Dispose() => session.Dispose();
    }

}
