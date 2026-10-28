using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class GhoulHitEffect1458Tests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static IEnumerable<JsonElement> OriginalCases()
    {
        using var stream = typeof(GhoulHitEffect1458Tests).Assembly.GetManifestResourceStream("GhoulHitEffectsLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray()) yield return row.Clone();
    }

    public static IEnumerable<object[]> Components() => OriginalCases()
        .Where(row => row.GetProperty("defense").ValueKind == JsonValueKind.Null)
        .Select(row => new object[] { row.GetRawText() });

    public static IEnumerable<object[]> StrikeRoutes()
    {
        foreach (var row in OriginalCases().Where(row => row.GetProperty("defense").ValueKind != JsonValueKind.Null))
            foreach (string route in new[] { "environment", "contact", "client", "prepared-projectile" })
                yield return [row.GetRawText(), route];
    }

    // These literals came from two identical executions of the unmodified original.
    // The component matrix includes fractional thresholds and plain Ghoul524,
    // whose color-zero visual guard follows the random choice.
    [Theory, MemberData(nameof(Components))]
    public void Dedicated_component_matches_original_full_physical_random_state(string json)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(row.GetProperty("type").GetInt32(), row.GetProperty("seed").GetInt32(),
            row.GetProperty("lifeMax").GetInt32());
        bool lethal = row.GetProperty("lethal").GetBoolean();
        var component = f.Npc with { Simulation = f.Npc.Simulation with { Life = lethal ? 0 : f.Npc.Simulation.Life } };
        AssertRandom(row.GetProperty("before").GetProperty("rng"), f.Random);
        Invoke(f.Pipeline, lethal ? "ExecuteNpcDeathHitEffects" : "ExecuteNpcNonlethalHitEffects",
            lethal ? [component] : [component, row.GetProperty("damage").GetInt32()]);
        AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged)); Assert.Equal(f.Npc, unchanged);
        Assert.Equal(0, f.Items.ActiveCount);
    }

    // Actual original StrikeNPC_Inner resolves raw35 to25 with defense19 or20.
    // Every admitted route must pass that rounded value to the shared HitEffect.
    // Lava/Eskimo/Gold rows protect existing damage-sensitive callers too.
    [Theory, MemberData(nameof(StrikeRoutes))]
    public void Accepted_routes_pass_resolved_damage_once_before_publication(string json, string route)
    {
        using var document = JsonDocument.Parse(json); var row = document.RootElement;
        using var f = new Fixture(row.GetProperty("type").GetInt32(), row.GetProperty("seed").GetInt32(),
            row.GetProperty("lifeMax").GetInt32(), row.GetProperty("defense").GetInt32());
        int damage = row.GetProperty("damage").GetInt32();
        Assert.Equal(25, row.GetProperty("returned").GetDouble());
        AssertRandom(row.GetProperty("before").GetProperty("rng"), f.Random);
        if (route == "environment")
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, damage));
        else if (route == "contact")
        {
            Assert.True(f.Npcs.TrySpawnVanilla(new(3, 3, 800, 480, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = 100, LifeMax = 100, MoneyValue = 0 }), out var attacker));
            Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Committed,
                f.Pipeline.TryStrike(attacker.Handle, f.Npc.Handle, damage, 0, 1));
        }
        else if (route == "client")
            Assert.Equal(RuntimeNpcNetworkDamageResult.Committed, f.Pipeline.TryApply(f.Connection,
                new TerrariaNpcDamageState(f.Npc.Handle.Slot,
                    RuntimeNpcPacketProjection.ToProtocolGeneration(f.Npc.Handle.Generation), (short)damage, 0, 1, 0)));
        else
        {
            var before = f.Random.Clone(); var after = before.Clone(); int publications = 0;
            Assert.Equal(RuntimeProjectileNpcDamageResult.Committed, f.Pipeline.TryStrikePreparedProjectile(
                f.Projectile(damage), f.Npc, 1, damage, 0, false, before, after, () => true,
                _ => { AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random); publications++; }));
            Assert.Equal(1, publications);
            var current = f.Random.Clone();
            Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
                f.Projectile(damage), f.Npc, 1, damage, 0, false, current, current.Clone(), () => true, _ => publications++));
            Assert.Equal(1, publications);
        }
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var actual));
        Assert.Equal(row.GetProperty("after").GetProperty("npc").GetProperty("life").GetInt32(), actual.Simulation.Life);
        AssertRandom(row.GetProperty("after").GetProperty("rng"), f.Random);
    }

    [Theory]
    [InlineData(524)] [InlineData(525)] [InlineData(526)] [InlineData(527)]
    public void Refused_zero_invulnerable_stale_and_projectile_capabilities_are_inert(int type)
    {
        using var f = new Fixture(type, 1458, 500);
        var before = f.Random.Clone();
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 0));
        var stale = new NpcHandle(f.Npc.Handle.Slot, new(f.Npc.Handle.Generation.Value + 1));
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(stale, 35));
        int publications = 0;
        Assert.Equal(RuntimeProjectileNpcDamageResult.Rejected, f.Pipeline.TryStrikePreparedProjectile(
            f.Projectile(35), f.Npc, 1, 35, 0, false, before, before.Clone(), () => false, _ => publications++));
        Assert.True(f.Random.HasSameState(before)); Assert.Equal(0, publications);
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged)); Assert.Equal(f.Npc, unchanged);
        using var blocked = new Fixture(type, 1458, 500, blocked: true);
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected,
            blocked.Pipeline.TryStrikeEnvironment(blocked.Npc.Handle, 9999));
        Assert.True(blocked.Random.HasSameState(before));
        Assert.True(blocked.Npcs.TryGet(blocked.Npc.Handle, out var retained)); Assert.Equal(blocked.Npc, retained);
    }

    [Theory]
    [InlineData(524)] [InlineData(525)] [InlineData(526)] [InlineData(527)]
    public void Detached_death_preview_keeps_original_hit_effect_boundary_and_publishes_once(int type)
    {
        using var f = new Fixture(type, 1458, 500);
        var before = f.Random.Clone();
        var dead = f.Npc with { Simulation = f.Npc.Simulation with { Life = 0 } };
        Assert.True((bool)Invoke(f.Pipeline, "TryPrepareDeathPlan", [dead])!);
        object plan = typeof(RuntimeNpcNetworkCombatPipeline).GetField("pendingDeathPlan", PrivateInstance)!.GetValue(f.Pipeline)!;
        var phaseStarts = (VanillaUnifiedRandom1458?[])plan.GetType().GetField("beforePhaseRandom", PrivateInstance)!.GetValue(plan)!;
        var source = OriginalCases().Single(row => row.GetProperty("type").GetInt32() == type &&
            row.GetProperty("seed").GetInt32() == 1458 && row.GetProperty("profile").GetString() == "lethal-max500-damage0");
        AssertRandom(source.GetProperty("after").GetProperty("rng"), phaseStarts[0]!);
        var plannedFinal = ((VanillaUnifiedRandom1458)plan.GetType().GetProperty("Random")!.GetValue(plan)!).Clone();
        Assert.True(f.Random.HasSameState(before)); Assert.Equal(0, f.Items.ActiveCount);
        Assert.True(f.Npcs.TryGet(f.Npc.Handle, out var unchanged)); Assert.Equal(f.Npc, unchanged);
        Invoke(f.Pipeline, "CancelPendingDeathPlan", []);
        Assert.True(f.Random.HasSameState(before));
        // The final checkpoint is a transaction assertion against its retained preview;
        // only the preceding20-choice HitEffect boundary is an original component oracle.
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Killed, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.True(f.Random.HasSameState(plannedFinal));
        Assert.False(f.Npcs.TryGet(f.Npc.Handle, out _)); int items = f.Items.ActiveCount;
        Assert.Equal(RuntimeTownNpcMeleeDamageResult1458.Rejected, f.Pipeline.TryStrikeEnvironment(f.Npc.Handle, 9999));
        Assert.True(f.Random.HasSameState(plannedFinal)); Assert.Equal(items, f.Items.ActiveCount);
    }

    private static object? Invoke(object owner, string method, object[] args) => owner.GetType()
        .GetMethod(method, PrivateInstance)!.Invoke(owner, args);

    private static void AssertRandom(JsonElement expected, VanillaUnifiedRandom1458 actual)
    {
        Assert.Equal(expected.GetProperty("cursor").GetUInt32(),
            (uint)typeof(VanillaUnifiedRandom1458).GetField("inext", PrivateInstance)!.GetValue(actual)!);
        int[] expectedSlots = expected.GetProperty("slots").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        Assert.Equal(56, expectedSlots.Length);
        Assert.Equal(expectedSlots, (int[])typeof(VanillaUnifiedRandom1458).GetField("seedArray", PrivateInstance)!.GetValue(actual)!);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeNpcStore Npcs = new();
        internal readonly RuntimeWorldItemStore Items = new();
        internal readonly VanillaUnifiedRandom1458 Random;
        internal readonly NpcSnapshot Npc;
        internal readonly RuntimeNpcNetworkCombatPipeline Pipeline;
        internal readonly ConnectionHandle Connection;
        private readonly PlayerJoinSession session;

        internal Fixture(int type, int seed, int lifeMax, int defense = 0, bool blocked = false)
        {
            Random = new(seed);
            var players = new PlayerAuthority(null, null); var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease)); session = new(lease!);
            session.ObserveWorldRequest(); session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(88172), session.Handle);
            players.TryApply(new PlayerHealthRuntimeCommand(Connection, new(session.Slot, 400, 400)));
            players.TryApply(new PlayerSpawnRuntimeCommand(Connection, session, new(session.Slot, 40, 30, 0, 0, 0, 0, 0)));
            players.TryApply(new PlayerEquipmentRuntimeCommand(Connection, new(session.Slot, 0, 1, 0, 39, 0)));
            players.TryApply(new PlayerItemAnimationRuntimeCommand(Connection, 0, 20));
            Assert.True(Npcs.TrySpawnVanilla(new(type, (short)type, 640, 480, 0, 0, 255, default,
                NpcSimulationState.Initial with { Life = lifeMax, LifeMax = lifeMax, DefenseOverride = defense,
                    MoneyValue = 0, ExtraMoneyValue = 0, Midas = false, DontTakeDamage = blocked }), out Npc));
            Pipeline = new(Npcs, Items, new RuntimePlayerSnapshotLookup(players, null), players, () => 0,
                null, new(Items), null, null, new(), false, false, lootRandom: Random, npcSpecificLowTiles: () => false);
        }

        internal ProjectileSnapshot Projectile(int damage) => new(new(0, new(1)), new(1), new(1), 0,
            640, 480, 1, 0, default, 0, checked((short)damage), 0, checked((short)damage));

        public void Dispose() => session.Dispose();
    }
}
