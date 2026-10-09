using System.IO.Compression;
using System.Reflection;
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
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class RemotePassivePlayerPhase1458Tests
{
    [Fact]
    public void Original_passive_family_matches_fresh_and_carried_whole_remote_phases()
    {
        using var source = Read();
        int profiles = 0, updates = 0, excluded = 0;
        foreach (string platform in new[] { "linux", "windows", "carried" })
        {
            var rows = source.RootElement.GetProperty(platform);
            Assert.Equal(1981, rows.GetArrayLength());
            foreach (var row in rows.EnumerateArray())
            {
                if (I(row.GetProperty("spec"), "weapon") == 5334)
                {
                    // The reference is preserved; its unowned special use branch is not neutral.
                    Assert.False(VanillaRemotePassiveItemCheck1458.IsSupported(new(5334)));
                    excluded++;
                    continue;
                }
                updates += Compare(row, platform == "windows");
                profiles++;
            }
        }
        Assert.Equal(5940, profiles);
        Assert.Equal(23760, updates);
        Assert.Equal(3, excluded);
    }

    [Fact]
    public void Orthogonal_source_switches_early_returns_and_mixed_census_preserve_exact_owned_fields()
    {
        using var source = Read();
        int profiles = 0, updates = 0, bufferedReferences = 0, mixed = 0;
        foreach (var row in source.RootElement.GetProperty("orthogonal").EnumerateArray())
        {
            var spec = row.GetProperty("spec");
            string mode = spec.GetProperty("mode").GetString()!;
            if (mode == "buffered")
            {
                // Source's private selection buffer has no packet-owned equivalent in this slice.
                Assert.True(row.GetProperty("initial")[0].GetProperty("selectionBuffered").GetBoolean());
                bufferedReferences++;
                continue;
            }
            updates += Compare(row, false);
            profiles++;
            if (mode.StartsWith("mixed", StringComparison.Ordinal)) mixed++;
        }
        foreach (var row in source.RootElement.GetProperty("variants").EnumerateArray())
        {
            if (I(row.GetProperty("spec"), "weapon") == 0) continue;
            updates += Compare(row, false);
            profiles++;
        }
        Assert.Equal(3, bufferedReferences);
        Assert.Equal(6, mixed);
        Assert.Equal(48, profiles);
        Assert.Equal(3072, updates);
    }

    [Fact]
    public void Selected_unknown_prefix_and_late_inventory_changes_refuse_without_reseeding_shared_phase()
    {
        using var source = Read();
        var row = source.RootElement.GetProperty("orthogonal").EnumerateArray().First(candidate =>
            candidate.GetProperty("spec").GetProperty("name").GetString() == "23-mixed-0-2-living-True-[]");
        foreach (short unsupported in new short[] { 5334, 155 })
        {
            using var f = new Fixture(row, false);
            f.Prepare(row.GetProperty("steps")[0]);
            f.State.Tick();
            Assert.All(f.Members, member => Assert.NotNull(member.ItemPhase));
            var random = f.Random.Clone();
            f.Item(2, 0, unsupported, 1);
            Assert.True(f.Players.TryGetInventoryItem(f.Connections[2], 0, out var actual));
            Assert.Equal(unsupported, actual.ItemType.Value);
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.True(f.Random.HasSameState(random));
            f.State.Tick();
            Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
            f.Item(2, 0, 23, 1);
            f.State.Tick();
            Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
            Assert.True(f.Random.HasSameState(random));
        }
        using var late = new Fixture(row, false);
        late.Prepare(row.GetProperty("steps")[0]);
        late.State.Tick();
        var phases = late.Members.Select(member => member.ItemPhase).ToArray();
        var physics = late.Members.Select(member => member.PhysicsPhase).ToArray();
        var health = late.Members.Select(member => member.NpcHealth).ToArray();
        var cursor = late.Random.Clone();
        int callbacks = 0;
        late.Players.SetNpcHealthWorldFacts(() =>
        {
            callbacks++;
            late.Item(0, 0, 3, 1);
            return new(false, false);
        });
        Assert.False(late.Players.TryTickRemotePlayerPhase());
        Assert.True(callbacks > 0);
        Assert.Equal(phases, late.Members.Select(member => member.ItemPhase));
        Assert.Equal(physics, late.Members.Select(member => member.PhysicsPhase));
        Assert.Equal(health, late.Members.Select(member => member.NpcHealth));
        Assert.True(late.Random.HasSameState(cursor));
        Assert.False(VanillaRemotePassiveItemCheck1458.IsSupported(new(23), new(1)));
        // Public content identities are int-valued; wrapping them must not alias genuine items.
        foreach (int id in new[] { 0, 6196, 65536 + 23, 65536 + 3, int.MaxValue })
            Assert.False(VanillaRemotePassiveItemCheck1458.IsSupported(new(id)));
    }

    internal static int Compare(JsonElement row, bool windows)
    {
        Assert.True(row.GetProperty("inventoryUnchanged").GetBoolean());
        Assert.True(row.GetProperty("restoredOutside").GetBoolean());
        Assert.Equal(row.GetProperty("beforeOutside").GetRawText(), row.GetProperty("afterOutside").GetRawText());
        Assert.Equal(0, I(row, "projectileCount"));
        using var f = new Fixture(row, windows);
        AssertInventory(row.GetProperty("beforeInventory"), f);
        if (f.Members.Length == 2)
        {
            Assert.False(new PlayerSlotPool(255).TryAcquireConnection(new(255), out _));
            Assert.False(f.Players.TryGet(255, out _));
        }
        int updates = 0;
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            f.Prepare(step);
            AssertRandom(step.GetProperty("before"), f.Random);
            f.State.Tick();
            AssertRandom(step.GetProperty("after"), f.Random);
            foreach (var player in step.GetProperty("players").EnumerateArray())
                AssertPlayer(player, f.Member(I(player, "whoAmI")), f);
            Assert.Equal(0, f.Projectiles.ActiveCount);
            updates++;
        }
        AssertInventory(row.GetProperty("afterInventory"), f);
        return updates;
    }

    internal static JsonDocument Read(string resource = "RemotePassiveItemPhase1458")
    {
        using var stream = typeof(RemotePassivePlayerPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private static JsonElement Find(JsonDocument source, string name) =>
        source.RootElement.GetProperty("Linux").EnumerateArray().First(row => row.GetProperty("spec").GetProperty("name").GetString() == name);

    private static int I(JsonElement row, string field) => row.GetProperty(field).GetInt32();
    private static float S(JsonElement row, string field) => row.GetProperty(field).GetSingle();
    private static float F(JsonElement row, string field, string component) => row.GetProperty(field).GetProperty(component).GetSingle();

    private static void AssertPlayer(JsonElement expected, RuntimePlayerMember member, Fixture f)
    {
        var item = member.ItemPhase!.Value;
        var combat = item.DerivedCombat!.Value;
        Assert.Equal(S(expected, "endurance"), combat.Endurance);
        Assert.Equal(S(expected, "rangedMultDamage"), combat.RangedMultDamage);
        Assert.Equal(S(expected, "arrowDamage"), combat.ArrowDamage);
        Assert.Equal(S(expected, "arrowDamageAdditiveStack"), combat.ArrowDamageAdditiveStack);
        Assert.Equal(S(expected, "bulletDamage"), combat.BulletDamage);
        Assert.Equal(I(expected, "meleeArmorPenetration"), combat.MeleeArmorPenetration);
        Assert.Equal(expected.GetProperty("magicQuiver").GetBoolean(), combat.MagicQuiver);
        Assert.Equal(I(expected, "lavaMax"), combat.LavaProtectionTicks);
        Assert.Equal(expected.GetProperty("lavaRose").GetBoolean(), combat.LavaRose);
        Assert.Equal(expected.GetProperty("waterWalk").GetBoolean(), combat.WaterWalk);
        Assert.Equal(I(expected, "statDefense"), combat.Defense);
        Assert.Equal(I(expected, "meleeCrit"), combat.MeleeCrit);
        Assert.Equal(I(expected, "rangedCrit"), combat.RangedCrit);
        Assert.Equal(I(expected, "magicCrit"), combat.MagicCrit);
        Assert.Equal(S(expected, "meleeDamage"), combat.MeleeDamage);
        Assert.Equal(S(expected, "rangedDamage"), combat.RangedDamage);
        Assert.Equal(S(expected, "magicDamage"), combat.MagicDamage);
        Assert.Equal(S(expected, "meleeSpeed"), combat.MeleeAttackSpeed);
        Assert.Equal(I(expected, "armorPenetration"), combat.ArmorPenetration);
        Assert.Equal(expected.GetProperty("noKnockback").GetBoolean(), combat.NoKnockback);
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
        Assert.Equal(I(expected, "toolTime"), item.ToolTime);
        Assert.Equal(I(expected, "attackCD"), item.AttackCD);
        Assert.Equal(I(expected, "deadTime"), item.DeadTime);
        Assert.Equal(I(expected, "respawnTimer"), item.RespawnTimer);
        Assert.Equal(I(expected, "revolverCritChanceBonus"), item.Selected.RevolverCritBonus);
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
        var profiles = f.Profiles;
        profiles.CaptureBuffState(member.Connection)!.CaptureSlots(out var types, out var times);
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), times);
    }

    private static void AssertInventory(JsonElement inventories, Fixture f)
    {
        for (int actor = 0; actor < inventories.GetArrayLength(); actor++)
            foreach (var expected in inventories[actor].EnumerateArray())
            {
                Assert.True(f.Players.TryGetInventoryItem(f.Connections[f.Members[actor].Slot.Value], I(expected, "slot"), out var actual));
                Assert.Equal(I(expected, "type"), actual.ItemType.Value);
                Assert.Equal(I(expected, "prefix"), actual.Prefix.Value);
                if (I(expected, "type") == 0)
                {
                    // Original reference Item0 may retain constructor stack1; both are empty.
                    Assert.True(actual.IsEmpty);
                }
                else Assert.Equal(I(expected, "stack"), actual.Stack);
            }
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    internal sealed class Fixture : IDisposable
    {
        internal readonly ServerRuntimeState State;
        internal readonly PlayerAuthority Players;
        internal readonly RuntimeProjectileStore Projectiles = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly ConnectionHandle[] Connections = new ConnectionHandle[3];
        internal readonly RuntimePlayerMember[] Members;
        private readonly PlayerJoinSession[] sessions;
        private readonly string mode;
        private readonly string branch;
        private readonly int mainSlot;
        private readonly bool success;
        internal RuntimePlayerTransferProfileStore Profiles =>
            (RuntimePlayerTransferProfileStore)typeof(PlayerAuthority).GetField("transferProfiles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;

        internal Fixture(JsonElement row, bool windows)
        {
            var spec = row.GetProperty("spec");
            mode = spec.GetProperty("mode").GetString()!;
            branch = spec.GetProperty("branch").GetString()!;
            mainSlot = mode == "mixed-2-0" ? 2 : 0;
            success = spec.GetProperty("success").GetBoolean();
            bool remix = spec.TryGetProperty("remix", out var remixFlag) && remixFlag.GetBoolean();
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, npcs: new RuntimeNpcStore(capacity: 8), projectiles: Projectiles,
                playerUpdateRandomSeed: new(I(spec, "seed")));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, remix, false)
                { WindowsItemPrefixArithmetic = windows });
            Players.SetRemotePlayerEnvironment(new(remix, false), Projectiles);
            var pool = new PlayerSlotPool(3);
            sessions = new PlayerJoinSession[3];
            for (int i = 0; i < 3; i++)
            {
                Assert.True(pool.TryAcquireConnection(new(checked((byte)i)), out var lease));
                sessions[i] = new(lease!);
            }
            var members = new List<RuntimePlayerMember>();
            for (int index = 0; index < row.GetProperty("initial").GetArrayLength(); index++)
            {
                var initial = row.GetProperty("initial")[index];
                int slot = I(initial, "whoAmI");
                var session = sessions[slot];
                session.ObserveWorldRequest();
                session.ObserveSectionRequest();
                var connection = Connections[slot] = new(GameCommandSourceId.FromConnection(99300 + slot), session.Handle);
                State.Apply(new PlayerHealthRuntimeCommand(connection, new(session.Slot, 400, 400)));
                State.Apply(new PlayerManaRuntimeCommand(connection, new(session.Slot, 10, 200)));
                foreach (var entry in row.GetProperty("beforeInventory")[index].EnumerateArray())
                    if (I(entry, "type") > 0)
                        Item(slot, (short)I(entry, "slot"), (short)I(entry, "type"), (short)I(entry, "stack"), (byte)I(entry, "prefix"));
                State.Apply(new PlayerSpawnRuntimeCommand(connection, session, new(session.Slot, (short)(100 + slot * 2), 103, 0, 0, 0, 0, 0)));
                State.Apply(new PlayerBuffTypesRuntimeCommand(connection, new(session.Slot,
                    spec.GetProperty("buffs").EnumerateArray().Select(x => new BuffTypeId(x.GetInt32())).ToArray())));
                Move(slot, false, 0, F(initial, "position", "X"), F(initial, "position", "Y"));
                Assert.True(Players.TryGet(connection, out var member));
                var phase = member.ItemPhase!.Value;
                member.ItemAnimation = I(initial, "itemAnimation");
                member.ItemRotation = S(initial, "itemRotation");
                member.ItemPhase = phase with
                {
                    Selected = phase.Selected with
                    {
                        Animation = I(initial, "itemAnimation"),
                        AnimationMax = I(initial, "itemAnimationMax"),
                        ItemTime = I(initial, "itemTime"),
                        ItemTimeMax = I(initial, "itemTimeMax"),
                        ReleaseUseItem = initial.GetProperty("releaseUseItem").GetBoolean()
                    },
                    ToolTime = I(initial, "toolTime"),
                    AttackCD = I(initial, "attackCD")
                };
                members.Add(member);
            }
            Members = members.ToArray();
        }

        internal RuntimePlayerMember Member(int slot) => Members.Single(member => member.Slot.Value == slot);
        internal void Item(int actor, short slot, short type, short stack, byte prefix = 0) =>
            State.Apply(new PlayerEquipmentRuntimeCommand(Connections[actor], new(new(checked((byte)actor)), slot, stack, prefix, type, 0)));

        private void Move(int actor, bool use, byte selected, float x, float y) =>
            State.Apply(new PlayerMovementRuntimeCommand(Connections[actor],
                new(new(checked((byte)actor)), (byte)(64 | (use ? 32 : 0)), 16, 0, success ? (byte)64 : (byte)0, selected,
                    x, y, false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));

        internal void Prepare(JsonElement step)
        {
            int tick = I(step, "tick");
            var actor = Member(mainSlot);
            if (tick == 1 && branch != "living")
            {
                if (branch == "dead")
                {
                    actor.IsDead = true;
                    actor.NpcHealth = actor.NpcHealth!.Value with { Life = 0 };
                    actor.ItemPhase = actor.ItemPhase!.Value with { DeadTime = 17, RespawnTimer = 500 };
                }
                if (branch == "ghost") actor.MovementFlags |= 64;
                if (branch == "outside") { actor.PositionX = 0; actor.PositionY = 0; }
            }
            if (tick == 4 && mode == "report41")
            {
                // The original profile supplies these represented fields directly, not GetData41.
                actor.ItemAnimation = 3;
                actor.ItemRotation = 0.25f;
                actor.ItemPhase = actor.ItemPhase!.Value with { Selected = actor.ItemPhase.Value.Selected with { Animation = 3 } };
            }
            foreach (var member in Members)
            {
                byte selected = (byte)(member.Slot.Value == mainSlot && tick >= 3 && mode.StartsWith("switch", StringComparison.Ordinal) ? 1 : 0);
                Move(member.Slot.Value, step.GetProperty("use").GetBoolean(), selected, member.PositionX, member.PositionY);
                if (branch == "ghost" && tick > 0 && member.Slot.Value == mainSlot) member.MovementFlags |= 64;
            }
        }

        public void Dispose() { foreach (var session in sessions) session.Dispose(); }
    }
}
