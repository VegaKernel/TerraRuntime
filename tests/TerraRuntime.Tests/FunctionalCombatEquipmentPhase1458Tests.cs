using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Core.Worlds;
using TerraRuntime.Gameplay.Items;
using TerraRuntime.Gameplay.Players;
using TerraRuntime.Network;
using Xunit;

namespace TerraRuntime.Tests;

// Unchanged configured original UpdatePlayers and separate local Shoot/PVE component records.
// Windows CLR4/x86 source has no literal transport capture; full56 assertions below are managed-only.
// Scope is represented fields/cursors, not full Main, natural-client scheduling or shared stream ownership.
public sealed class FunctionalCombatEquipmentPhase1458Tests
{
    [Fact]
    public void Original_accessory_fields_and_owned_item_clocks_match_admitted_complete_phases()
    {
        int phases = 0, references = 0;
        foreach (string resource in new[] { "FunctionalCombatEquipmentPhase1458", "FunctionalCombatEquipmentSpeed1458", "FunctionalCombatEquipmentInherited1458" })
        {
            using var source = Read(resource);
            foreach (string platform in new[] { "linux", "windowsClr4x86" })
            foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
            {
                string name = row.GetProperty("spec").GetProperty("name").GetString()!;
                // Existing selected155 and wrong-armor-slot boundaries are explicit reference controls.
                // The new speed corpus uses admitted WoodenSword24, preserving all original132 rows.
                if (name.StartsWith("optional-melee-speed-") || name is "warrior-melee" or "invalid-head-accessory")
                {
                    references++;
                    continue;
                }
                using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(row, platform == "windowsClr4x86");
                if (resource == "FunctionalCombatEquipmentSpeed1458") f.Equipment(0, 24, 1);
                foreach (var step in row.GetProperty("steps").EnumerateArray())
                {
                    var spec = row.GetProperty("spec");
                    if (I(step, "tick") == 8 && spec.TryGetProperty("action", out var action))
                    {
                        int type = action.GetString() == "remove" ? 0 : I(spec, "actionType");
                        f.Equipment(62, (short)type, (short)(type == 0 ? 0 : 1));
                    }
                    f.PrepareStep(step);
                    AssertRandom(step.GetProperty("before"), f.Random);
                    f.State.Tick();
                    Assert.True(f.Member.ItemPhase.HasValue, $"Retired: {platform}/{name}/{I(step, "tick")}");
                    AssertPlayer(step.GetProperty("player"), f);
                    if (step.GetProperty("second").ValueKind != JsonValueKind.Null)
                        AssertPlayer(step.GetProperty("second"), f, true);
                    AssertRandom(step.GetProperty("after"), f.Random);
                    Assert.Equal(0, f.Projectiles.ActiveCount);
                    phases++;
                }
            }
        }
        Assert.Equal(4832, phases);
        Assert.Equal(12, references);
    }

    [Fact]
    public void Original_retained_accessory_damage_and_crit_feed_common_launch_math_and_real_npc_hits()
    {
        using var phases = Read("FunctionalCombatEquipmentPhase1458");
        using var source = Read("FunctionalCombatEquipmentHit1458");
        var configured = phases.RootElement.GetProperty("linux")[0];
        int hits = 0;
        foreach (string platform in new[] { "linux", "windowsClr4x86" })
        foreach (var row in source.RootElement.GetProperty(platform).EnumerateArray())
        {
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(configured, platform == "windowsClr4x86");
            var spec = row.GetProperty("spec");
            f.Equipment(0, 219, 1); f.Equipment(1, 219, 1); f.Equipment(54, 97, 20);
            int gear = I(spec, "AccessoryType");
            f.Equipment(62, (short)gear, (short)(gear == 0 ? 0 : 1), (byte)I(spec, "AccessoryPrefix"));
            f.State.Tick();
            Assert.NotNull(f.Member.ItemPhase);
            Assert.NotNull(f.Member.ItemPhase.Value.DerivedCombat);
            var combat = f.Member.ItemPhase.Value.DerivedCombat.Value;
            var expected = row.GetProperty("phaseCrit");
            Assert.Equal(S(expected, "minionDamage"), combat.MinionDamage);
            Assert.Equal(I(expected, "melee"), combat.MeleeCrit);
            Assert.Equal(I(expected, "ranged"), combat.RangedCrit);
            Assert.Equal(I(expected, "magic"), combat.MagicCrit);
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetWeapon(new(219), out var weapon));
            Assert.True(VanillaProjectileWeaponCombatCatalog.TryGetBulletAmmo(new(97), out var ammo));
            Assert.True(VanillaItemCombatCatalog.TryGetRangedPrefixModifiers(default, out var prefix));
            // Common launch magnitude is compared to actual original Shoot output. Runtime Shoot parity is not claimed.
            Assert.Equal(I(row.GetProperty("launch"), "damage"),
                VanillaProjectileWeaponCombatCatalog.ResolveDamage(in weapon, in ammo, in prefix, in combat));
            using var arena = new Arena(f, I(row, "hitSeed"), row.GetProperty("launch"));
            arena.Pass.Tick();
            Assert.Equal(1, arena.Pass.CommittedHits);
            Assert.True(arena.Npcs.TryGet(arena.Actor.Handle, out var actor));
            Assert.Equal(I(row, "npcLife"), actor.Simulation.Life);
            AssertRandom(row.GetProperty("hitRng"), arena.Random);
            if (platform == "linux")
                Assert.Equal(row.GetProperty("frames").EnumerateArray().Select(x => x.GetString()),
                    arena.Drain().Where(x => x[2] == 28).Select(Convert.ToHexString));
            hits++;
        }
        Assert.Equal(60, hits);
    }

    [Fact]
    public void Default_clocks_and_whole_census_guards_preserve_existing_cursor_ownership()
    {
        // Default API compatibility across all existing101 identities; these are policy checks,
        // separate from the original phase corpus and its configured seeded cursor assertions.
        var initial = new PlayerSelectedConsumableState1458(0, 0, 0, 0, true, 0, 0);
        int supported = 0;
        for (int id = 0; id < VanillaItemIds.Count; id++)
        {
            if (!VanillaRemoteMeleeItemCatalog1458.TryGet(new(id), out _)) continue;
            var facts = new PlayerRemoteMeleeItemFacts1458(new(id), true, true, false, false, false);
            int implicitOffers = 0, explicitOffers = 0;
            Assert.True(VanillaRemoteMeleeItemCheck1458.TryStep(initial, in facts,
                (min, max) => { implicitOffers++; return min; }, out var implicitResult));
            Assert.True(VanillaRemoteMeleeItemCheck1458.TryStep(initial, in facts,
                (min, max) => { explicitOffers++; return min; }, out var explicitResult, 1f));
            Assert.Equal(implicitResult, explicitResult);
            Assert.Equal(implicitOffers, explicitOffers);
            supported++;
        }
        Assert.Equal(101, supported);
        var invalidFacts = new PlayerRemoteMeleeItemFacts1458(new(24), true, true, false, false, false);
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
        {
            int offers = 0;
            Assert.False(VanillaRemoteMeleeItemCheck1458.TryStep(initial, in invalidFacts,
                (min, max) => { offers++; return min; }, out _, invalid));
            Assert.Equal(0, offers);
        }
        using var source = Read("FunctionalCombatEquipmentPhase1458");
        var row = source.RootElement.GetProperty("linux").EnumerateArray()
            .Single(x => x.GetProperty("spec").GetProperty("name").GetString() == "mixed-two-players");
        foreach (bool mutateSecond in new[] { false, true })
        {
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(row, false);
            f.PrepareStep(row.GetProperty("steps")[0]);
            var first = f.Member.ItemPhase; var second = f.SecondMember.ItemPhase;
            var random = f.Random.Clone(); bool once = false;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (!once)
                {
                    once = true;
                    var connection = mutateSecond ? f.SecondConnection : f.Connection;
                    f.State.Apply(new PlayerEquipmentRuntimeCommand(connection,
                        new(connection.Player.Slot, 62, 1, 72, 491, 0)));
                }
                return new(false, false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.True(once); Assert.Equal(first, f.Member.ItemPhase); Assert.Equal(second, f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
        }
        var locked = source.RootElement.GetProperty("linux").EnumerateArray()
            .Single(x => x.GetProperty("spec").GetProperty("name").GetString() == "normal-heart-locked");
        // Local vanity is bound once by composition; no callback mutation is possible after
        // that binding. The same captured context also protects the mutable difficulty flags.
        foreach (bool master in new[] { false, true })
        {
            var mixed = System.Text.Json.Nodes.JsonNode.Parse(locked.GetRawText())!;
            mixed["spec"]!["mixed"] = true;
            using var mixedDocument = JsonDocument.Parse(mixed.ToJsonString());
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(mixedDocument.RootElement, false);
            var first = f.Member.ItemPhase; var second = f.SecondMember.ItemPhase;
            var random = f.Random.Clone(); int reads = 0;
            f.Players.SetNpcHealthWorldFacts(() =>
            {
                if (++reads == 3) f.Players.SetCombatEquipmentWorldModes(true, master);
                return new(false, false);
            });
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(first, f.Member.ItemPhase); Assert.Equal(second, f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random)); Assert.Equal(4, reads);
        }
        foreach (string name in new[] { "invalid-head-accessory", "warrior-melee" })
        {
            var reference = source.RootElement.GetProperty("linux").EnumerateArray()
                .Single(x => x.GetProperty("spec").GetProperty("name").GetString() == name);
            using var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(reference, false);
            var before = f.Member.ItemPhase; var random = f.Random.Clone();
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(before, f.Member.ItemPhase); Assert.True(f.Random.HasSameState(random));
            f.State.Tick(); Assert.Null(f.Member.ItemPhase); Assert.True(f.Random.HasSameState(random));
        }
        using (var f = new HumanDefensiveCritBuffPhase1458Tests.Fixture(row, false))
        {
            var first = f.Member.ItemPhase; var second = f.SecondMember.ItemPhase; var random = f.Random.Clone();
            // The magmaStone writer (Player.ApplyEquipFunctional 1.4.5.8) remains unsupported;
            // one unknown member cannot be skipped
            // while the other commits a phase from the same source-owned UpdatePlayers cursor.
            f.State.Apply(new PlayerEquipmentRuntimeCommand(f.SecondConnection,
                new(f.SecondConnection.Player.Slot, 62, 1, 0, 1322, 0)));
            Assert.False(f.Players.TryTickRemotePlayerPhase());
            Assert.Equal(first, f.Member.ItemPhase); Assert.Equal(second, f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
            f.State.Tick(); Assert.Null(f.Member.ItemPhase); Assert.Null(f.SecondMember.ItemPhase);
            Assert.True(f.Random.HasSameState(random));
        }
    }

    private static JsonDocument Read(string name)
    {
        using var stream = typeof(FunctionalCombatEquipmentPhase1458Tests).Assembly.GetManifestResourceStream(name)!;
        Assert.NotNull(stream);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }
    private static int I(JsonElement row, string field) => row.GetProperty(field).GetInt32();
    private static float S(JsonElement row, string field) => row.GetProperty(field).GetSingle();
    private static float F(JsonElement row, string field, string component) => row.GetProperty(field).GetProperty(component).GetSingle();
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
        WaterWalk = player.GetProperty("waterWalk").GetBoolean(),
        Archery = player.GetProperty("archery").GetBoolean(),
        MinionDamage = S(player,"minionDamage")
    };

    private static void AssertPlayer(JsonElement expected, HumanDefensiveCritBuffPhase1458Tests.Fixture f, bool second=false)
    {
        var member = second ? f.SecondMember : f.Member;
        var item = member.ItemPhase!.Value;
        Assert.Equal(Combat(expected) with { MeleeAttackSpeed = 1f },
            item.DerivedCombat!.Value with { MeleeAttackSpeed = 1f });
        Assert.Equal(S(expected, "meleeSpeed"), item.DerivedCombat.Value.MeleeAnimationMultiplier);
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
        f.Profiles.CaptureBuffState(second ? f.SecondConnection : f.Connection)!.CaptureSlots(out var types, out var times);
        Assert.Equal(expected.GetProperty("buffType").EnumerateArray().Select(x => x.GetInt32()), types.Select(x => x.Value));
        Assert.Equal(expected.GetProperty("buffTime").EnumerateArray().Select(x => x.GetInt32()), times);
    }

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random));
        Assert.Equal(expected.GetProperty("state").EnumerateArray().Select(x => x.GetInt32()),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(random)!);
    }

    private sealed class Arena : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs;
        internal readonly NpcSnapshot Actor;
        internal readonly ProjectileSnapshot Shot;
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly RuntimeNpcNetworkCombatPipeline Combat;
        internal readonly RuntimeProjectileNpcCombatPass Pass;
        internal Action? BeforeTick { get; set; }
        internal Action? DuringLoot { get; set; }
        private readonly TerrariaConnectionOutboundQueue outbound = new(new OutboundQueueOptions(128, 131072, 1024));

        internal Arena(HumanDefensiveCritBuffPhase1458Tests.Fixture f, int seed, JsonElement launch, int npcType = 1,
            int life = 1000)
        {
            Random = new(seed);
            var registry = new RuntimeNpcReplicationRegistry();
            Npcs = new(capacity: 8, commitSink: registry);
            var actor = new NpcStateUpdate(npcType, (short)npcType, 1800, 1606, 0, 0, 0, default,
                NpcSimulationState.Initial with { Life = life, LifeMax = 1000, MoneyValue = 0,
                    ExtraMoneyValue = 0, Immortal = false, ShimmerTransparency = 0 });
            Assert.True(Npcs.TrySpawn(0, actor, out Actor));
            var items = new RuntimeWorldItemStore();
            var lookup = new RuntimePlayerSnapshotLookup(f.Players, null);
            Combat = new(Npcs, items, lookup, f.Players, () => 100, registry, new(items), null,
                new(1000, false, default, 0, 0), new(), false, false, lootRandom: Random,
                seasonalItemContext: () => default,
                npcSpecificLowTiles: () => { DuringLoot?.Invoke(); return false; });
            var status = new RuntimeNpcBuffStatus1458(Npcs, registry.PublishNpcBuffs);
            registry.BindNpcBuffStatus(status);
            var velocity = launch.GetProperty("velocity");
            var shot = new ProjectileStateUpdate(new(launch.GetProperty("type").GetInt32()), 0,
                1800, 1606, velocity.GetProperty("X").GetSingle(), velocity.GetProperty("Y").GetSingle(),
                default, 0, checked((short)launch.GetProperty("damage").GetInt32()), launch.GetProperty("knockBack").GetSingle(),
                checked((short)launch.GetProperty("originalDamage").GetInt32()));
            Assert.True(f.Projectiles.TrySpawn(0, shot, out Shot));
            Assert.True(f.Projectiles.TryMarkCombatTrusted(Shot.Handle, f.Connection.Player));
            Pass = new(f.Projectiles, Npcs, Combat, f.Players,
                () => { BeforeTick?.Invoke(); return 100; }, status: status);
            Assert.True(registry.TryRegister(f.Connection.Source, outbound));
            registry.PlayerSpawned(f.Connection, new(f.Connection.Player.Slot, 100, 103, 0, 0, 0, 0, 0));
            _ = Drain();
        }

        internal byte[][] Drain()
        {
            var queue = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
                .GetField("_queue", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(outbound)!;
            var frames = new List<byte[]>();
            while (queue.TryRead(out var frame)) frames.Add(frame.Bytes.ToArray());
            return frames.ToArray();
        }

        public void Dispose() { }
    }
}
