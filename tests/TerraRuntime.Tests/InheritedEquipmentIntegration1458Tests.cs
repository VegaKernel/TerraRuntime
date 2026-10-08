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
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class InheritedEquipmentIntegration1458Tests
{
    [Fact]
    public void Packet5_and_owned_server_equipment_project_all_source_inheritance_profiles()
    {
        using Stream source = typeof(InheritedEquipmentIntegration1458Tests).Assembly.GetManifestResourceStream("InheritedEquipment1458")!;
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using JsonDocument document = JsonDocument.Parse(gzip);
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            using var f = new Fixture();
            int mode = row.GetProperty("mode").GetInt32();
            bool good = row.GetProperty("good").GetBoolean();
            bool expert = mode > 0 || good, master = mode == 2 || mode == 1 && good;
            f.Players.SetCombatEquipmentWorldModes(expert, master);
            var identities = new ServerPlayerSlotRegistry(new PlayerSlotPool(1));
            var bots = new ServerPlayerAuthority(new ServerPlayerStateStore(identities, 1), identities);
            bots.SetCombatEquipmentWorldModes(expert, master);
            var id = new ServerPlayerId("test:inherited-source");
            var bot = bots.Create(id, 100, 100);
            Assert.True(bot.IsCreated);
            byte heart = row.GetProperty("heart").GetBoolean() ? (byte)4 : (byte)0;
            f.Players.TryApply(new PlayerAppearanceRuntimeCommand(f.Connection, Appearance(f.Connection.Player.Slot, heart)));
            Assert.True(bots.SetAppearance(id, new ServerPlayerAppearanceState(0, 0, 0f, 0, "Inheritance", 0, 0, 0,
                default, default, default, default, default, default, default, heart, 0, 0)));
            ItemTypeId head = default, body = default, legs = default;
            foreach (JsonElement item in row.GetProperty("equipment").EnumerateArray())
            {
                int loadout = item.GetProperty("loadout").GetInt32(), index = item.GetProperty("slot").GetInt32();
                int type = item.GetProperty("item").GetInt32();
                if (loadout == -2 || loadout == -1 && row.GetProperty("localSlot").GetInt32() == 0)
                {
                    if (index == 10) head = new(type);
                    if (index == 11) body = new(type);
                    if (index == 12) legs = new(type);
                }
                if (loadout == -2 && row.GetProperty("localSlot").GetInt32() != 0) continue;
                short slot = checked((short)(loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart + index :
                    VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride + index));
                byte prefix = item.GetProperty("prefix").GetByte();
                byte flags = item.GetProperty("favorite").GetBoolean() ? PlayerEquipmentCommitRequest.FavoriteItemFlag : (byte)0;
                f.Item(slot, type, 1, prefix, flags);
                Assert.True(bots.SetItem(id, new(slot, new(type), 1, new(prefix), flags)));
            }
            var local = new VanillaPlayerLocalVanityArmor1458(head, body, legs);
            f.Players.SetCombatEquipmentLocalVanityArmor(local);
            bots.SetCombatEquipmentLocalVanityArmor(local);
            Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var human), row.GetProperty("name").GetString());
            Assert.True(bots.TryCaptureCombatSnapshot(bot.Player, out var server));
            Assert.Equal(human, server);
            Assert.Equal(row.GetProperty("defense").GetInt32(), human.Defense);
            Assert.Equal(row.GetProperty("rangedDamage").GetSingle(), human.RangedDamage);
            Assert.Equal(row.GetProperty("magicQuiver").GetBoolean(), human.MagicQuiver);
            Assert.Equal(row.GetProperty("arrowDamageAdditiveStack").GetSingle(), human.ArrowDamageAdditiveStack);
        }
    }

    [Fact]
    public void Dedicated_local_slot255_empty_is_owned_and_unknown_or_matching_local_vanity_is_distinct()
    {
        foreach (int kind in new[] { 0, 1, 2 })
        {
            using var f = new Fixture();
            f.Item(VanillaPlayerItemSlotCatalog.LoadoutArmorStart, 89, 1, flags: 1);
            if (kind != 0) f.Players.SetCombatEquipmentLocalVanityArmor(kind == 1
                ? VanillaPlayerLocalVanityArmor1458.Empty : new(new(89), default, default));
            bool accepted = f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat);
            Assert.Equal(kind != 0, accepted);
            if (accepted) Assert.Equal(kind == 1 ? 1 : 0, combat.Defense);
        }
    }

    [Fact]
    public void Inherited_accessory_prefix_reaches_the_actual_strict_projectile_use_commit()
    {
        using var f = new Fixture();
        f.PrepareShot();
        Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var combat));
        Assert.Equal(1.15f, combat.RangedDamage);
        Assert.Equal(1, combat.Defense);
        f.Shoot();
        Assert.Equal(1, f.Authority.PromotedClientProjectileSpawns);
        Assert.True(f.Store.TryGetActive(0, out var shot));
        Assert.True(f.Store.IsCombatTrusted(shot.Handle));
        Assert.Equal(14, shot.Damage);
        Assert.Equal(19, f.Ammo.Stack);
        Assert.Equal(1198642031, f.Random.Clone().Next());
    }

    [Fact]
    public void Late_favorite_identity_or_prefix_change_preserves_the_new_profile_and_refuses_old_shot()
    {
        foreach (bool changePrefix in new[] { false, true })
        {
            using var f = new Fixture();
            f.PrepareShot();
            var before = f.Random.Clone();
            Assert.True(f.Players.TryCaptureProjectileUse(f.Connection, out var capture));
            f.BeforeTick = () => f.Item((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 3),
                changePrefix ? 491 : 1321, 1, changePrefix ? (byte)72 : (byte)62, 1);
            f.Shoot();
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.Equal(0, f.Authority.PromotedClientProjectileSpawns);
            Assert.Equal(20, f.Ammo.Stack);
            Assert.True(f.Random.HasSameState(before));
            Assert.False(f.Players.IsCurrentProjectileUse(capture!));
            Assert.True(f.Players.TryCaptureProjectileUse(f.Connection, out var current));
            var changed = Assert.Single(current!.Equipment, item => item.SlotId == VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 3);
            Assert.Equal(changePrefix ? 491 : 1321, changed.ItemNetId);
            Assert.Equal(changePrefix ? 72 : 62, changed.Prefix);
        }
    }

    private static PlayerAppearanceCommitRequest Appearance(PlayerSlotId slot, byte difficulty) =>
        new(slot, 0, 0, 0f, 0, "Inheritance", 0, 0, 0, default, default, default, default, default, default, default, difficulty, 0, 0);

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        internal readonly PlayerAuthority Players = new(null, null);
        internal readonly VanillaUnifiedRandom1458 Random = new(0);
        internal readonly RuntimeProjectileStore Store;
        internal readonly ProjectileAuthority Authority;
        internal readonly ConnectionHandle Connection;
        internal Action? BeforeTick;
        internal RuntimePlayerInventoryItem Ammo
        {
            get { Assert.True(Players.TryGetInventoryItem(Connection, VanillaPlayerItemSlotCatalog.AmmoSlotStart, out var item)); return item; }
        }
        internal Fixture()
        {
            Assert.True(new PlayerSlotPool(1).TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(7001), session.Handle);
            Players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 100, 0, 0, 0, 0, 0)));
            Players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 100, 100)));
            var replication = new RuntimeProjectileReplicationRegistry();
            Store = new(4, replication);
            Authority = new(Store, Players, new RuntimeNpcStore(), new RuntimePlayerSnapshotLookup(Players, null), null,
                replication, () => { var action = BeforeTick; BeforeTick = null; action?.Invoke(); return 100; }, projectileRandom: Random);
        }
        internal void Item(short slot, int type, short stack, byte prefix = 0, byte flags = 0) =>
            Players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(Connection.Player.Slot, slot, stack, prefix, checked((short)type), flags)));
        internal void PrepareShot()
        {
            Players.SetCombatEquipmentLocalVanityArmor(VanillaPlayerLocalVanityArmor1458.Empty);
            Item(0, 98, 1);
            Item(VanillaPlayerItemSlotCatalog.AmmoSlotStart, 97, 20);
            Item((short)(VanillaPlayerItemSlotCatalog.LoadoutArmorStart + 3), 491, 1, 62, 1);
            Players.TryApply(new PlayerMovementRuntimeCommand(Connection, new(Connection.Player.Slot, 0, 0, 0, 0, 0,
                1600f, 1600f, false, 0f, 0f, false, 0, false, 0f, 0f, 0f, 0f, false, 0f, 0f)));
        }
        internal void Shoot() => Assert.True(Authority.TryApply(new ClientProjectileUpdateRuntimeCommand(Connection,
            new(new(Connection.Player.Slot.Value, 700, 1), 14, 1620f, 1600f, 11.26f, .22f, 0f, 0f, 0f, 0, 14, 2f, 0))));
        public void Dispose() => session.Dispose();
    }
}
