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

public sealed class RemoteBowPlayerPhase1458Tests
{
    [Fact]
    public void Original_ordinary_bows_and_mixed_gun_census_match_full_remote_phases_without_shooting()
    {
        int updates = 0, mixed = 0;
        foreach (var (resource, count) in new[]
        {
            ("RemoteBowPlayerPhase1458", 25), ("RemoteBowRoundingPlayerPhase1458", 2)
        })
        {
            using var source = Read(resource);
            foreach (string platform in new[] { "Linux", "Windows" })
            {
                Assert.Equal(count, source.RootElement.GetProperty(platform).GetArrayLength());
                foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
                {
                    Assert.True(row.GetProperty("inventoryUnchanged").GetBoolean());
                    Assert.True(row.GetProperty("restoredOutside").GetBoolean());
                    Assert.Equal(row.GetProperty("beforeOutside").GetRawText(), row.GetProperty("afterOutside").GetRawText());
                    Assert.Equal(0, I(row, "projectileCount"));
                    using var f = new Fixture(row, platform == "Windows");
                    AssertInventory(row.GetProperty("beforeInventory"), f);
                    if (f.Members.Length == 2)
                    {
                        // The original mixed census deliberately has an active source255 Bow.
                        // Main.UpdateWorld_Players excludes255; normal authenticated slots stop254.
                        Assert.False(new PlayerSlotPool(255).TryAcquireConnection(new(255), out _));
                        Assert.False(f.Players.TryGet(255, out _));
                        Assert.Equal(new[] { 0, 2 }, f.Members.Select(member => (int)member.Slot.Value));
                        mixed++;
                    }
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
                }
            }
        }
        Assert.Equal(3456, updates);
        Assert.Equal(4, mixed);
    }

    [Fact]
    public void Source_rollable_prefix_family_and_retained_identity_bound_bow_admission()
    {
        using var source = Read("RemoteBowPrefixFamily1458");
        int requests = 0;
        foreach (string platform in new[] { "Linux", "Windows" })
        {
            bool windows = platform == "Windows";
            var arithmetic = windows ? VanillaBulletSourceArithmetic1458.WindowsClr4X86 :
                VanillaBulletSourceArithmetic1458.CoreClrSingle;
            int accepted = 0;
            int diagnosticRetained = 0;
            foreach (var bow in source.RootElement.GetProperty(platform).EnumerateArray())
            {
                var item = new ItemTypeId(I(bow, "id"));
                foreach (var request in bow.GetProperty("cases").EnumerateArray())
                {
                    int requested = I(request, "requested");
                    bool admitted = request.GetProperty("rollable").GetBoolean() &&
                        request.GetProperty("returned").GetBoolean() && I(request, "retained") == requested;
                    var prefix = new PrefixId(checked((byte)requested));
                    Assert.Equal(admitted, VanillaRemoteRangedItemCheck1458.IsSupported(item, prefix, windows));
                    Assert.Equal(admitted, VanillaSelectedItemCrit1458.TryResolve(item, prefix, arithmetic, out int crit));
                    if (admitted)
                    {
                        Assert.Equal(I(request, "rawCrit"), crit);
                        accepted++;
                    }
                    // Prefix43 can survive Item.Prefix on stronger bows but is absent from family35.
                    if (requested == 43 && I(request, "retained") == requested)
                    {
                        Assert.False(admitted);
                        diagnosticRetained++;
                    }
                    requests++;
                }
            }
            Assert.Equal(175, accepted);
            Assert.Equal(8, diagnosticRetained);
        }
        Assert.Equal(666, requests);
    }

    [Fact]
    public void Late_ammo_mutation_refuses_whole_bow_census_and_unsupported_actor_retires_shared_custody()
    {
        using var source = Read();
        var row = Find(source, "39-mixed-0-2");
        using var f = new Fixture(row, false);
        f.Prepare(row.GetProperty("steps")[0]);
        f.State.Tick();
        var items = f.Members.Select(member => member.ItemPhase).ToArray();
        var physics = f.Members.Select(member => member.PhysicsPhase).ToArray();
        var health = f.Members.Select(member => member.NpcHealth).ToArray();
        var random = f.Random.Clone();
        int reads = 0;
        f.Players.SetNpcHealthWorldFacts(() =>
        {
            reads++;
            f.Item(0, 54, 0, 0);
            return new(false, false);
        });
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.True(reads > 0);
        Assert.Equal(items, f.Members.Select(member => member.ItemPhase));
        Assert.Equal(physics, f.Members.Select(member => member.PhysicsPhase));
        Assert.Equal(health, f.Members.Select(member => member.NpcHealth));
        Assert.True(f.Random.HasSameState(random));
        Assert.True(f.Players.TryGetInventoryItem(f.Connections[0], 54, out var currentAmmo));
        Assert.True(currentAmmo.IsEmpty);

        f.Players.SetNpcHealthWorldFacts(() => new(false, false));
        // Muramasa155 is known inventory, but outside the bounded remote phase family.
        f.Item(2, 0, 155, 1);
        Assert.False(f.Players.TryTickRemotePlayerPhase());
        Assert.True(f.Random.HasSameState(random));
        f.State.Tick();
        Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
        f.Item(2, 0, 98, 1);
        f.State.Tick();
        Assert.All(f.Members, member => Assert.Null(member.ItemPhase));
        Assert.True(f.Random.HasSameState(random));
        Assert.Equal(0, f.Projectiles.ActiveCount);
    }

    [Fact]
    public void Pending_gun_children_survive_another_owned_bow_actor_phase_but_keep_input_epoch_guards()
    {
        using var source = PendingBulletPlayerPhase1458Tests.Source();
        var row = PendingBulletPlayerPhase1458Tests.Row(source);
        foreach (bool mutateInput in new[] { false, true })
        {
            using var f = new PendingBulletPlayerPhase1458Tests.Fixture(row);
            Assert.True(new PlayerSlotPool(3).TryAcquireConnection(new(2), out var lease));
            using var session = new PlayerJoinSession(lease!);
            session.ObserveWorldRequest();
            session.ObserveSectionRequest();
            var bow = new ConnectionHandle(GameCommandSourceId.FromConnection(99202), session.Handle);
            f.State.Apply(new PlayerHealthRuntimeCommand(bow, new(session.Slot, 400, 400)));
            f.State.Apply(new PlayerManaRuntimeCommand(bow, new(session.Slot, 10, 200)));
            f.State.Apply(new PlayerEquipmentRuntimeCommand(bow, new(session.Slot, 0, 1, 0, 39, 0)));
            f.State.Apply(new PlayerEquipmentRuntimeCommand(bow, new(session.Slot, 54, 50, 0, 40, 0)));
            f.State.Apply(new PlayerSpawnRuntimeCommand(bow, session, new(session.Slot, 104, 103, 0, 0, 0, 0, 0)));
            f.State.Apply(new PlayerMovementRuntimeCommand(bow,
                new(session.Slot, 64, 16, 0, 64, 0, 1664, 1606,
                    false, 0, 0, false, 0, false, 0, 0, 0, 0, false, 0, 0)));
            Assert.True(f.Players().TryGet(bow, out var bowMember));
            var projectileRandom = f.Random.Clone();
            f.Report(0);
            f.State.Tick();
            Assert.NotNull(bowMember.ItemPhase);
            Assert.True(f.Players().TryGet(f.Connection, out var gunMember));
            Assert.NotNull(gunMember.ItemPhase);
            Assert.Equal(0, f.Store.ActiveCount);
            Assert.True(f.Random.HasSameState(projectileRandom));
            if (mutateInput) { f.Move(1601); f.Move(1600); }
            f.Complete();
            Assert.Equal(mutateInput ? 0 : 5, f.Store.ActiveCount);
            Assert.Equal(mutateInput ? 20 : row.GetProperty("afterAmmo").GetInt32(), f.Ammo());
            if (mutateInput) Assert.True(f.Random.HasSameState(projectileRandom));
            else Assert.Equal(row.GetProperty("next").GetInt32(), f.Random.Clone().Next());
            Assert.True(f.State.TryCapturePlayerInventoryItem(bow.Player, 54, out var arrows));
            Assert.Equal(50, arrows.Stack);
        }
        // This combines the independently captured gun launch with supported Bow census;
        // it is a pending-command compatibility test, not a new original Shoot composition.
    }

    private static JsonDocument Read(string resource = "RemoteBowPlayerPhase1458")
    {
        using var stream = typeof(RemoteBowPlayerPhase1458Tests).Assembly.GetManifestResourceStream(resource)!;
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
                Assert.Equal(I(expected, "stack"), actual.Stack);
            }
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Fixture : IDisposable
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
            var tiles = new WorldTileStore(new WorldDimensions(400, 300));
            for (int x = 0; x < 400; x++) tiles.Set(x, 103, new WorldTile { Type = 1, Flags = WorldTileFlags.Active });
            State = new(worldTiles: tiles, npcs: new RuntimeNpcStore(capacity: 8), projectiles: Projectiles,
                playerUpdateRandomSeed: new(I(spec, "seed")));
            Players = ((ServerRuntimeComposition)typeof(ServerRuntimeState)
                .GetField("_runtime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(State)!).Players;
            Random = (VanillaUnifiedRandom1458)typeof(PlayerAuthority)
                .GetField("playerUpdateRandom", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Players)!;
            Players.SetPlayerUpdateWorldFacts(new RuntimePlayerUpdateWorld1458(400, 300, 80, false, false)
                { WindowsItemPrefixArithmetic = windows });
            Players.SetRemotePlayerEnvironment(new(false, false), Projectiles);
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
