using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class GoldBunnyHitEffect1458Tests
{
    // Independently executed, unmodified original NPC.HitEffect, netMode2,
    // seed1458. Each hash binds little-endian uint cursor plus all56 int slots.
    // This is a HitEffect component oracle, not a whole StrikeNPC/loot capture.
    private const string Initial = "5ccc3069036c6d54f3ca441fd8c36c6a4491f8ce77eaeb20e42df6c870ed87c7";
    private const string TenChoices = "72fe0d789a34b81f2b54f093b076fb6d10feae5a5c2ab951514e80dc1b49bad3";
    private const string TwentyChoices = "93bc7e1f6fedfa76a28d9c34600f4a78da6661785b960250c1b84a967f9cbac8";
    // Separate original component sequence: lethal HitEffect20, zero-value
    // NPCLoot_DropMoney9, then DropHeals2. Value0 still makes money offers.
    private const string DeathMoneyAndHealing = "71801082c14abfec4f8a4fc0779e6210c644a287bcda9b3c4de1de5cfd2fa6d4";

    [Theory]
    [InlineData(443, false)] [InlineData(443, true)]
    [InlineData(46, false)] [InlineData(46, true)]
    [InlineData(303, false)] [InlineData(303, true)]
    [InlineData(337, false)] [InlineData(337, true)]
    [InlineData(540, false)] [InlineData(540, true)]
    [InlineData(646, false)] [InlineData(646, true)]
    public void Dedicated_hit_effect_matches_original_complete_random_state(int type, bool lethal)
    {
        var f = new Fixture(type);
        var postHit = f.Npc with { Simulation = f.Npc.Simulation with { Life = lethal ? 0 : 4 } };
        string method = lethal ? "ExecuteNpcDeathHitEffects" : "ExecuteNpcNonlethalHitEffects";
        typeof(RuntimeNpcNetworkCombatPipeline).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Pipeline, lethal ? [postHit] : [postHit, 1]);
        Assert.Equal(type == 443 ? lethal ? TwentyChoices : TenChoices : Initial, Digest(f.Random));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged));
        Assert.Equal(f.Npc, unchanged);
        Assert.Equal(0, f.Items.ActiveCount);
    }

    [Fact]
    public void Accepted_nonlethal_environment_and_contact_each_advance_the_shared_owner_once()
    {
        var f = new Fixture(443);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 0));
        Assert.Equal(Initial, Digest(f.Random));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 1));
        Assert.Equal(TenChoices, Digest(f.Random));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var first)); Assert.Equal(4, first.Simulation.Life);
        Assert.True(f.Npcs.TrySpawnVanilla(new(3, 3, 800, 400, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DefenseOverride = 0, MoneyValue = 0 }), out var attacker));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrike(attacker.Handle, f.Npc.Handle, 1, 0, 1));
        Assert.Equal(TwentyChoices, Digest(f.Random));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var second)); Assert.Equal(3, second.Simulation.Life);
    }

    [Fact]
    public void Accepted_death_preserves_original_hit_effect_money_healing_order_and_stale_retry_is_inert()
    {
        var f = new Fixture(443);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.False(f.Npcs.TryGet(f.Npc.Handle, out _));
        Assert.Equal(DeathMoneyAndHealing, Digest(f.Random));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.Equal(DeathMoneyAndHealing, Digest(f.Random));
    }

    [Fact]
    public void Prepared_projectile_adopts_hit_effect_once_before_its_observer_and_refusals_are_inert()
    {
        var f = new Fixture(443, playerLookup: new KnownPlayer());
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(1), 0,
            640, 400, 1, 0, default, 0, 1, 0, 1);
        var before = f.Random.Clone(); var after = before.Clone();
        int publications = 0;
        void Publish(NpcSnapshot _) { Assert.Equal(TenChoices, Digest(f.Random)); publications++; }
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, before, after, () => false, Publish));
        Assert.Equal(Initial, Digest(f.Random)); Assert.Equal(0, publications);
        Assert.Equal(RuntimeProjectileNpcDamageResult.Committed, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, before, after, () => true, Publish));
        Assert.Equal(TenChoices, Digest(f.Random)); Assert.Equal(1, publications);
        var currentRandom = f.Random.Clone();
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, currentRandom, currentRandom.Clone(), () => true, Publish));
        Assert.Equal(TenChoices, Digest(f.Random)); Assert.Equal(1, publications);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Refused_invulnerable_strikes_never_advance_hit_effect_random(bool lethal)
    {
        var f = new Fixture(443, blocked: true);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, lethal ? 9999 : 1));
        Assert.Equal(Initial, Digest(f.Random));
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged)); Assert.Equal(f.Npc, unchanged);
    }

    private static string Digest(VanillaUnifiedRandom1458 random)
    {
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        uint cursor = (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", fields)!.GetValue(random)!;
        var slots = (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", fields)!.GetValue(random)!;
        Assert.Equal(56, slots.Length);
        Span<byte> bytes = stackalloc byte[4 + 56 * 4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, cursor);
        for (int i = 0; i < slots.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes[(4 + i * 4)..], slots[i]);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class Fixture
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly NpcSnapshot Npc;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;

        internal Fixture(int type, bool blocked = false, IRuntimePlayerSlotSnapshotLookup? playerLookup = null)
        {
            Assert.True(Npcs.TrySpawnVanilla(new(type, (short)type, 640, 400, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 5, LifeMax = 5, DefenseOverride = 0,
                    MoneyValue = 0, ExtraMoneyValue = 0, Midas = false, DontTakeDamage = blocked,
                    SpawnedFromStatue = true }), out Npc));
            Pipeline = new(Npcs, Items, playerLookup ?? new EmptyPlayers(), new PlayerAuthority(null, null), () => 0,
                null, new(Items), null, null, new(), false, false, lootRandom: Random, npcSpecificLowTiles: () => false);
        }
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        { snapshot = default; return false; }
    }

    private sealed class KnownPlayer : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = 600, PositionY = 400, HasHealth = true, Life = 100, MaxLife = 100, DerivedLifeMax = 100 };
            return slot.Value == 0;
        }
    }
}
