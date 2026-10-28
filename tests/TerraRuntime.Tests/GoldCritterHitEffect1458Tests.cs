using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class GoldCritterHitEffect1458Tests
{
    public static IEnumerable<object[]> HitEffects() => Rows("hitEffect");
    public static IEnumerable<object[]> DeathSequences() => Rows("deathSequence");

    [Theory, MemberData(nameof(HitEffects))]
    public void Dedicated_gold_hit_effects_match_actual_original_complete_random_state(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        int type = row.GetProperty("type").GetInt32();
        var f = new Fixture(type, seed);
        var npc = f.Npc with { Simulation = f.Npc.Simulation with {
            Life = row.GetProperty("before").GetProperty("npc").GetProperty("life").GetInt32() } };
        AssertRandom(row.GetProperty("before").GetProperty("rng"), f.Random);
        string method = row.GetProperty("lethal").GetBoolean()
            ? "ExecuteNpcDeathHitEffects" : "ExecuteNpcNonlethalHitEffects";
        typeof(RuntimeNpcNetworkCombatPipeline).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(f.Pipeline, row.GetProperty("lethal").GetBoolean() ? [npc] : [npc, 1]);
        AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
        Assert.Equal(f.Npc, f.Current());
        Assert.Equal(0, f.Items.ActiveCount);
    }

    [Theory, MemberData(nameof(DeathSequences))]
    public void Actual_lethal_owner_preserves_source_hit_money_healing_order_and_stale_retry(int seed, string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var f = new Fixture(row.GetProperty("type").GetInt32(), seed);
        // The source reference executes these three components in order with
        // value/extraValue zero and an uninjured zero-luck constructor player.
        // This checks the runtime composition, not a full original StrikeNPC.
        AssertRandom(row.GetProperty("before").GetProperty("rng"), f.Random);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.False(f.Npcs.TryGet(f.Npc.Handle, out _));
        AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
    }

    [Theory]
    [InlineData(445)] [InlineData(447)] [InlineData(539)]
    public void Environment_and_contact_use_the_same_shared_gold_hit_effect_owner(int type)
    {
        var f = new Fixture(type, 1458);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 0));
        AssertSourceHitRng(1458, lethal: false, f.Random, before: true);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 1));
        Assert.Equal(4, f.Current().Simulation.Life);
        AssertSourceHitRng(1458, lethal: false, f.Random);
        Assert.True(f.Npcs.TrySpawnVanilla(new(3, 3, 800, 400, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 100, LifeMax = 100, DefenseOverride = 0, MoneyValue = 0 }), out var attacker));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
            f.Pipeline.TryStrike(attacker.Handle, f.Npc.Handle, 1, 0, 1));
        Assert.Equal(3, f.Current().Simulation.Life);
        AssertSourceHitRng(1458, lethal: true, f.Random);
    }

    [Theory]
    [InlineData(445)] [InlineData(447)] [InlineData(539)]
    public void Prepared_projectile_adopts_gold_choices_once_before_observers_and_refusals_are_inert(int type)
    {
        var f = new Fixture(type, 1458, new KnownPlayer());
        var projectile = new ProjectileSnapshot(new(0, new(1)), new(1), new(1), 0,
            640, 400, 1, 0, default, 0, 1, 0, 1);
        var before = f.Random.Clone();
        int publications = 0;
        void Publish(NpcSnapshot _)
        {
            AssertSourceHitRng(1458, lethal: false, f.Random);
            publications++;
        }
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, before, before.Clone(), () => false, Publish));
        Assert.True(before.HasSameState(f.Random));
        Assert.Equal(f.Npc, f.Current());
        Assert.Equal(RuntimeProjectileNpcDamageResult.Committed, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, before, before.Clone(), () => true, Publish));
        Assert.Equal(1, publications);
        var accepted = f.Random.Clone();
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
            projectile, f.Npc, 1, 1, 0, false, accepted, accepted.Clone(), () => true, Publish));
        Assert.True(accepted.HasSameState(f.Random));
        Assert.Equal(1, publications);
    }

    [Theory]
    [InlineData(445)] [InlineData(447)] [InlineData(539)]
    public void Invulnerable_gold_actor_never_spends_hit_effect_choices(int type)
    {
        var f = new Fixture(type, 1458, blocked: true);
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.Equal(f.Npc, f.Current());
        Assert.True(before.HasSameState(f.Random));
    }

    internal static IEnumerable<object[]> Rows(string kind)
    {
        using var stream = typeof(GoldCritterHitEffect1458Tests).Assembly
            .GetManifestResourceStream("GoldCritterCatchLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var capture in document.RootElement.GetProperty("captures").EnumerateArray())
            foreach (var row in capture.GetProperty("rows").EnumerateArray())
                if (row.GetProperty("kind").GetString() == kind)
                    yield return [capture.GetProperty("seed").GetInt32(), row.GetRawText()];
    }

    internal static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 random)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", flags)!.GetValue(random)!);
        Assert.Equal(expected.GetProperty("slots").EnumerateArray().Select(v => v.GetInt32()).ToArray(),
            (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", flags)!.GetValue(random)!);
    }

    private static void AssertSourceHitRng(int seed, bool lethal, VanillaUnifiedRandom1458 random, bool before = false)
    {
        string json = (string)Rows("hitEffect").Single(data => (int)data[0] == seed &&
            JsonType((string)data[1], lethal))[1];
        using var document = JsonDocument.Parse(json);
        AssertRandom(document.RootElement.GetProperty(before ? "before" : "after").GetProperty("rng"), random);

        static bool JsonType(string json, bool lethal)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("type").GetInt32() == 443 &&
                doc.RootElement.GetProperty("lethal").GetBoolean() == lethal;
        }
    }

    private sealed class Fixture
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;

        internal Fixture(int type, int seed, IRuntimePlayerSlotSnapshotLookup? playerLookup = null, bool blocked = false)
        {
            Random = new(seed);
            Assert.True(Npcs.TrySpawnVanilla(new(type, (short)type, 1023.25f, 1560.75f, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 5, LifeMax = 5, DefenseOverride = 0,
                    MoneyValue = 0, ExtraMoneyValue = 0, Midas = false, DontTakeDamage = blocked,
                    SpawnedFromStatue = true }), out Npc));
            Pipeline = new(Npcs, Items, playerLookup ?? new EmptyPlayers(), new PlayerAuthority(null, null), () => 0,
                null, new(Items), null, null, new(), false, false, lootRandom: Random, npcSpecificLowTiles: () => false);
        }

        internal NpcSnapshot Current() { Assert.True(Npcs.TryGet(Npc.Handle, out var npc)); return npc; }
    }

    private sealed class EmptyPlayers : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot) { snapshot = default; return false; }
    }

    private sealed class KnownPlayer : IRuntimePlayerSlotSnapshotLookup
    {
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            snapshot = default(PlayerStateSnapshot) with { Player = new(new(0), new(1)), Revision = new(1),
                PositionX = 1000, PositionY = 1558, HasHealth = true, Life = 100, MaxLife = 100, DerivedLifeMax = 100 };
            return slot.Value == 0;
        }
    }
}
