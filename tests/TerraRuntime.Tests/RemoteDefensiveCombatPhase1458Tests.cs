using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Players;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemoteDefensiveCombatPhase1458Tests
{
    [Fact]
    public void Original_defensive_equipment_and_mana_writers_match_full_retained_remote_phase()
    {
        using var source = Read();
        Assert.Equal(58, source.RootElement.GetProperty("rows").GetArrayLength());
        int compared = 0, rejected = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            using var f = new Fixture(row);
            Assert.Equal(Combat(source.RootElement.GetProperty("constructor")), f.Member.ItemPhase!.Value.DerivedCombat);
            string name = row.GetProperty("spec").GetProperty("name").GetString()!;
            if (name.StartsWith("excluded-", StringComparison.Ordinal))
            {
                var item = f.Member.ItemPhase;
                var physical = f.Member.PhysicsPhase;
                var health = f.Member.NpcHealth;
                var random = f.Random.Clone();
                Assert.False(f.Players.TryTickRemotePlayerPhase());
                Assert.Equal(item, f.Member.ItemPhase);
                Assert.Equal(physical, f.Member.PhysicsPhase);
                Assert.Equal(health, f.Member.NpcHealth);
                Assert.True(f.Random.HasSameState(random));
                f.State.Tick();
                Assert.Null(f.Member.ItemPhase);
                Assert.True(f.Random.HasSameState(random));
                rejected++;
                continue;
            }
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                f.PrepareStep(step);
                AssertRandom(step.GetProperty("before"), f.Random);
                f.State.Tick();
                AssertPlayer(step.GetProperty("player"), f);
                AssertRandom(step.GetProperty("after"), f.Random);
                Assert.Equal(0, f.Projectiles.ActiveCount);
                Assert.True(f.Players.TryGetInventoryItem(f.Connection, 54, out var ammo));
                Assert.Equal(row.GetProperty("ammoAfter").GetInt32(), ammo.Stack);
                compared++;
            }
        }
        Assert.Equal(648, compared);
        Assert.Equal(4, rejected);
    }

    [Fact]
    public void Actual_equipment_reports_preserve_phase_combat_until_the_next_owned_player_update()
    {
        using var source = Read();
        var row = Find(source, "combined-prefixes");
        using var f = new Fixture(row);
        f.PrepareStep(row.GetProperty("steps")[0]);
        f.State.Tick();
        var retained = Combat(row.GetProperty("steps")[0].GetProperty("player"));
        Assert.Equal(retained, f.Member.ItemPhase!.Value.DerivedCombat);
        Assert.Equal(5, retained.ArmorPenetration);
        Assert.Equal(15, retained.RangedCrit);
        f.Equipment(63, 0, 0);
        Assert.True(f.Players.TryCaptureProjectileCombatSnapshot(f.Connection.Player, out var projectile));
        Assert.Equal(retained, projectile);
        Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var queriedEquipment));
        Assert.Equal(0, queriedEquipment.ArmorPenetration);
        Assert.Equal(8, queriedEquipment.RangedCrit);
        Assert.Equal(retained, f.Member.ItemPhase!.Value.DerivedCombat);

        // Independent source shield68 row supplies the resulting equipment-only fields.
        var shield = Find(source, "shield-prefix-68").GetProperty("steps")[0].GetProperty("player");
        f.Equipment(0, 679, 1, 82);
        f.Equipment(1, 679, 1);
        f.Move(false, 0, true);
        f.State.Tick();
        Assert.Equal(Combat(shield), f.Member.ItemPhase!.Value.DerivedCombat);
        Assert.True(f.Players.TryCaptureProjectileCombatSnapshot(f.Connection.Player, out projectile));
        Assert.Equal(Combat(shield), projectile);
    }

    [Fact]
    public async Task Transfer_preserves_known_and_unknown_whole_combat_and_rejects_invalid_composites()
    {
        using var source = Read();
        var row = Find(source, "combined-prefixes");
        var valid = Combat(row.GetProperty("steps")[0].GetProperty("player"));
        foreach (VanillaPlayerCombatSnapshot? candidate in new VanillaPlayerCombatSnapshot?[]
        {
            valid, null,
            valid with { Endurance = float.NaN },
            valid with { ArmorPenetration = int.MaxValue, MeleeArmorPenetration = 1 },
            valid with { RangedDamage = float.MaxValue, BulletDamage = float.MaxValue }
        })
        {
            using var f = new Fixture(row);
            f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DerivedCombat = candidate };
            var detach = new TaskCompletionSource<RuntimePlayerTransferState?>();
            f.State.Apply(new PlayerTransferDetachRuntimeCommand(f.Connection, detach));
            var transfer = Assert.IsType<RuntimePlayerTransferState>(await detach.Task);
            Assert.Equal(candidate, transfer.ItemPhase!.Value.DerivedCombat);
            var inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority)
                .GetField("inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Players)!;
            ulong serial = inventory.Serial;
            var attach = new TaskCompletionSource<bool>();
            f.State.Apply(new PlayerTransferAttachRuntimeCommand(f.Connection, transfer, 100, 103, true, false, attach));
            bool accepted = candidate is null || candidate == valid;
            Assert.Equal(accepted, await attach.Task);
            Assert.Equal(accepted, f.Players.TryGet(f.Connection, out var member));
            if (accepted) Assert.Equal(candidate, member.ItemPhase!.Value.DerivedCombat);
            else
            {
                Assert.Equal(serial, inventory.Serial);
                Assert.False(f.Players.TryGetInventoryItem(f.Connection, 0, out _));
                Assert.False(f.Profiles.TryCapture(f.Connection, out _, out _, out _));
            }
        }
    }

    [Fact]
    public void Unknown_custody_and_late_profile_mutation_refuse_without_reconstructing_or_rewinding()
    {
        using var source = Read();
        var row = Find(source, "combined-prefixes");
        using var f = new Fixture(row);
        f.Member.ItemPhase = f.Member.ItemPhase!.Value with { DerivedCombat = null };
        f.Member.PositionX = 0;
        f.Member.PositionY = 0;
        f.State.Tick();
        Assert.Null(f.Member.ItemPhase!.Value.DerivedCombat);
        Assert.False(f.Players.TryCaptureProjectileCombatSnapshot(f.Connection.Player, out _));
        f.Move(false, 0, true, 1600, 1606);
        f.State.Tick();
        Assert.NotNull(f.Member.ItemPhase!.Value.DerivedCombat);
        var retained = f.Member.ItemPhase;
        var physical = f.Member.PhysicsPhase;
        var random = f.Random.Clone();
        int callbacks = 0;
        f.Players.SetNpcHealthWorldFacts(() =>
        {
            callbacks++;
            f.Equipment(63, 0, 0);
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(2, callbacks);
        Assert.Equal(retained, f.Member.ItemPhase);
        Assert.Equal(physical, f.Member.PhysicsPhase);
        Assert.True(f.Random.HasSameState(random));
        Assert.True(f.Players.TryCaptureCombatSnapshot(f.Connection, out var current));
        Assert.Equal(0, current.ArmorPenetration);

        // Source94 may exceed the representable nonnegative magic-damage slice.
        f.Players.SetNpcHealthWorldFacts(() => new(false, false));
        var types = new BuffTypeId[44];
        var times = new int[44];
        types[0] = new(94);
        times[0] = 1201;
        f.Profiles.RestoreBuffState(f.Connection, PlayerBuffState.FromSlots(types, times));
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.Equal(retained, f.Member.ItemPhase);
        Assert.True(f.Random.HasSameState(random));
    }

    private static JsonElement Find(JsonDocument source, string name) =>
        source.RootElement.GetProperty("rows").EnumerateArray().First(row =>
            row.GetProperty("platform").GetString() == "Linux" &&
            row.GetProperty("spec").GetProperty("name").GetString() == name);

    private static VanillaPlayerCombatSnapshot Combat(JsonElement player) => new(
        I(player, "statDefense"), S(player, "endurance"), S(player, "meleeDamage"),
        S(player, "rangedDamage"), S(player, "magicDamage"), S(player, "rangedMultDamage"),
        S(player, "arrowDamage"), S(player, "arrowDamageAdditiveStack"), S(player, "bulletDamage"),
        I(player, "meleeCrit"), I(player, "rangedCrit"), I(player, "magicCrit"), S(player, "meleeSpeed"),
        I(player, "armorPenetration"), I(player, "meleeArmorPenetration"),
        player.GetProperty("noKnockback").GetBoolean(), player.GetProperty("magicQuiver").GetBoolean())
    {
        LavaProtectionTicks = I(player, "lavaMax"),
        LavaRose = player.GetProperty("lavaRose").GetBoolean(),
        WaterWalk = player.GetProperty("waterWalk").GetBoolean()
    };

    private static void AssertPlayer(JsonElement expected, Fixture f)
    {
        var member = f.Member;
        var item = member.ItemPhase!.Value;
        Assert.Equal(Combat(expected), item.DerivedCombat);
        Assert.Equal(I(expected, "statLife"), member.CaptureSnapshot().NpcLife);
        Assert.Equal(I(expected, "statLifeMax2"), member.DerivedLifeMax);
        Assert.Equal(I(expected, "lifeRegenCount"), member.NpcHealth!.Value.RegenCount);
        Assert.Equal(S(expected, "lifeRegenTime"), member.NpcHealth.Value.RegenTime);
        Assert.Equal(I(expected, "statMana"), member.Mana);
        Assert.Equal(I(expected, "statManaMax2"), item.Mana.Maximum);
        Assert.Equal(I(expected, "manaRegenCount"), item.Mana.Count);
        Assert.Equal(S(expected, "manaRegenDelay"), item.Mana.Delay);
        Assert.Equal(S(expected, "manaHeat"), item.ManaHeat);
        Assert.Equal(I(expected, "itemAnimation"), item.Selected.Animation);
        Assert.Equal(I(expected, "itemAnimationMax"), item.Selected.AnimationMax);
        Assert.Equal(I(expected, "itemTime"), item.Selected.ItemTime);
        Assert.Equal(I(expected, "itemTimeMax"), item.Selected.ItemTimeMax);
        Assert.Equal(I(expected, "potionDelay"), item.Selected.PotionDelay);
        Assert.Equal(I(expected, "manaPotionDelay"), item.ManaPotionDelay);
        Assert.Equal(I(expected, "toolTime"), item.ToolTime);
        Assert.Equal(I(expected, "attackCD"), item.AttackCD);
        Assert.Equal(I(expected, "deadTime"), item.DeadTime);
        Assert.Equal(I(expected, "respawnTimer"), item.RespawnTimer);
        Assert.Equal(expected.GetProperty("releaseUseItem").GetBoolean(), item.Selected.ReleaseUseItem);
        Assert.Equal(expected.GetProperty("pendingItemReuse").GetBoolean(), item.PendingItemReuse);
        Assert.Equal(I(expected, "selectedItem"), member.SelectedItem);
        Assert.Equal(S(expected, "itemRotation"), member.ItemRotation);
        Assert.Equal(F(expected, "position", "X"), member.PositionX);
        Assert.Equal(F(expected, "position", "Y"), member.PositionY);
        Assert.Equal(F(expected, "velocity", "X"), member.VelocityX);
        Assert.Equal(F(expected, "velocity", "Y"), member.VelocityY);
        Assert.Equal(I(expected, "jump"), member.PhysicsPhase!.Value.Jump.RemainingTicks);
        Assert.Equal(I(expected, "wingTime"), member.PhysicsPhase.Value.Jump.WingTime);
        Assert.Equal(expected.GetProperty("releaseJump").GetBoolean(), member.PhysicsPhase.Value.Jump.ReleaseReady);
        f.Profiles.CaptureBuffState(f.Connection)!.CaptureSlots(out var types, out var times);
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), times);
    }

    private static JsonDocument Read()
    {
        using var stream = typeof(RemoteDefensiveCombatPhase1458Tests).Assembly.GetManifestResourceStream("RemoteDefensiveCombatPhase1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static int I(JsonElement row, string field) => row.GetProperty(field).GetInt32();
    private static float S(JsonElement row, string field) => row.GetProperty(field).GetSingle();
    private static float F(JsonElement row, string field, string component) => row.GetProperty(field).GetProperty(component).GetSingle();

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly PlayerJoinSession session;
        private readonly string branch;
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly ConnectionHandle Connection;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal RuntimePlayerMember Member
        {
            get { Assert.True(Players.TryGet(Connection, out var member)); return member; }
        }
        internal RuntimePlayerTransferProfileStore Profiles =>
            (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority)
                .GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;

        internal Fixture(JsonElement row)
        {
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++)
                tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, projectiles: Projectiles, playerUpdateRandomSeed: new(0));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            var spec = row.GetProperty("spec");
            branch = spec.GetProperty("branch").GetString()!;
            bool windows = row.GetProperty("platform").GetString() == "Windows";
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = windows });
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
            Assert.True(new PlayerSlotPool(1).TryAcquireConnection(out var lease));
            session = new(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(99809), session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(session.Slot, 10, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 100, 103, 0, 0, 0, 0, 0)));
            Move(false, 0, false, 1600, 1606);
            int mode = I(spec, "mode");
            bool good = spec.GetProperty("good").GetBoolean();
            Players.SetCombatEquipmentWorldModes(mode > 0 || good, mode >= 2 || mode == 1 && good);
            byte heart = spec.GetProperty("heart").GetBoolean() ? (byte)4 : (byte)0;
            State.Apply(new PlayerAppearanceRuntimeCommand(Connection,
                new(Connection.Player.Slot, 0, 0, 0f, 0, "Defensive", 0, 0, 0,
                    default, default, default, default, default, default, default, heart, 0, 0)));
            State.Apply(new PlayerManaRuntimeCommand(Connection, new(Connection.Player.Slot, 10, 200)));
            Equipment(0, (short)I(spec, "weapon"), row.GetProperty("initial").GetProperty("lastItemUseAttemptSuccess").GetBoolean() ? (short)20 : (short)1,
                (byte)I(spec, "prefix"));
            Equipment(1, (short)I(spec, "weapon"), 1);
            Equipment(54, 97, 50);
            foreach (var entry in spec.GetProperty("armor").EnumerateArray())
            {
                int loadout = I(entry, "loadout");
                short slot = checked((short)((loadout < 0 ? VanillaPlayerItemSlotCatalog.ArmorStart :
                    VanillaPlayerItemSlotCatalog.LoadoutArmorStart + loadout * VanillaPlayerItemSlotCatalog.LoadoutStride) + I(entry, "slot")));
                Equipment(slot, (short)I(entry, "type"), 1, (byte)I(entry, "prefix"),
                    entry.GetProperty("favorite").GetBoolean() ? (byte)1 : (byte)0);
            }
            var initial = row.GetProperty("initial");
            var types = initial.GetProperty("buffType").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray();
            var times = initial.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            Profiles.RestoreBuffState(Connection, PlayerBuffState.FromSlots(types, times));
            Member.ItemPhase = Member.ItemPhase!.Value with { ManaHeat = S(initial, "manaHeat") };
        }

        internal void Equipment(short slot, short type, short stack, byte prefix = 0, byte flags = 0) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connection,
                new(Connection.Player.Slot, slot, stack, prefix, type, flags)));

        internal void Move(bool use, byte selected, bool success, float? x = null, float? y = null) => State.Apply(new PlayerMovementRuntimeCommand(Connection,
            new(Connection.Player.Slot, (byte)(64 | (use ? 32 : 0)), 16, 0, success ? (byte)64 : (byte)0, selected,
                x ?? Member.PositionX, y ?? Member.PositionY, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        internal void PrepareStep(JsonElement step)
        {
            int tick = I(step, "tick");
            if (tick == 1 && branch != "living")
            {
                if (branch == "dead")
                {
                    Member.IsDead = true;
                    Member.NpcHealth = Member.NpcHealth!.Value with { Life = 0 };
                    Member.ItemPhase = Member.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
                }
                if (branch == "ghost") Member.MovementFlags |= 64;
                if (branch == "outside") { Member.PositionX = 0; Member.PositionY = 0; }
            }
            bool use = branch == "living" && tick > 0 && tick != 5;
            Move(use, (byte)(tick >= 3 && branch == "living" ? 1 : 0),
                step.GetProperty("beforePlayer").GetProperty("lastItemUseAttemptSuccess").GetBoolean());
            if (branch == "ghost" && tick > 0) Member.MovementFlags |= 64;
        }

        public void Dispose() => session.Dispose();
    }
}
