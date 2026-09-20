using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Projectiles;
using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Gameplay.Projectiles;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

/// <summary>Continuous source traces for authoritative Skeletron Prime encounter ticks.</summary>
public sealed class PrimeEncounterContinuousTests
{
    private static readonly JsonElement[] Rows = Read();

    [Fact]
    public void Head_arms_and_projectiles_match_the_first_twelve_hundred_continuous_encounter_ticks_per_phase()
    {
        RuntimeNpcStore? npcs = null;
        RuntimeProjectileStore? projectiles = null;
        RuntimeNpcAiStateExecutor? executor = null;
        INpcAiStateStepper? motion = null;
        RuntimeProjectileStateExecutor? projectileExecutor = null;
        IProjectileStateStepper? projectileMotion = null;
        CapturedRandom? random = null;
        foreach (JsonElement row in Rows)
        {
            if (row.GetProperty("tick").GetInt32() == 0)
                (npcs, projectiles, executor, motion, projectileExecutor, projectileMotion, random) = Create(row, projectiles);

            Assert.NotNull(npcs); Assert.NotNull(projectiles); Assert.NotNull(executor); Assert.NotNull(motion); Assert.NotNull(projectileExecutor); Assert.NotNull(projectileMotion); Assert.NotNull(random);
            Assert.Equal(5, executor.Tick(motion).Applied);
            random.AssertState(row.GetProperty("randomAfterNpc"));
            Assert.Equal(projectiles.ActiveCount, projectileExecutor.Tick(projectileMotion).Applied);
            foreach (JsonElement expected in row.GetProperty("after").EnumerateArray())
                AssertNpc(npcs, expected);
            AssertProjectiles(projectiles, row.GetProperty("projectiles"));
            random.SetState(row.GetProperty("randomAfter"));
        }
    }

    private static (RuntimeNpcStore Npcs, RuntimeProjectileStore Projectiles, RuntimeNpcAiStateExecutor Executor, INpcAiStateStepper Motion, RuntimeProjectileStateExecutor ProjectileExecutor, IProjectileStateStepper ProjectileMotion, CapturedRandom Random) Create(
        JsonElement row, RuntimeProjectileStore? existingProjectiles)
    {
        var npcs = new RuntimeNpcStore();
        var projectiles = existingProjectiles ?? new RuntimeProjectileStore();
        npcs.SetVanillaSpawnContextSource(() => new VanillaNpcSpawnContext(1, 1, false));
        Assert.True(npcs.TrySpawnIntent(new NpcAiSpawnIntent(
            VanillaNpcIds.SkeletronPrime, 1000, 1000, 0f, 0f, 0)
        {
            StartSlot = 0,
            InitialAi = new NpcAiState(0f, row.GetProperty("phase").GetSingle(), row.GetProperty("timer").GetSingle(), 0f)
        }, out _));

        var random = new CapturedRandom(row.GetProperty("randomBefore"));
        var targeting = new VanillaNpcTargetingAiStepper(new VanillaDemonEyeAiStepper(), random: random);
        targeting.SetWorldConditions(dayTime: false, slimeRainActive: false);
        targeting.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f, 1021f, 0, true, false, false, false)]);
        var tiles = new WorldTileStore(new WorldDimensions(400, 400));
        var motion = new VanillaNpcWorldMotionAiStepper(targeting, tiles);
        IProjectileStateStepper projectileMotion = new RuntimeProjectileBehaviorStateStepper(
            new VanillaProjectileWorldStateStepper(tiles),
            new RuntimeGameplayBehaviorRegistry<ProjectileTypeId, IProjectileStateStepper>());
        return (
            npcs,
            projectiles,
            new RuntimeNpcAiStateExecutor(npcs, projectiles),
            motion,
            new RuntimeProjectileStateExecutor(projectiles),
            projectileMotion,
            random);
    }

    private static void AssertNpc(RuntimeNpcStore npcs, JsonElement expected)
    {
        Assert.True(npcs.TryGetActive(expected.GetProperty("slot").GetByte(), out var actual));
        Assert.Equal(expected.GetProperty("type").GetInt32(), actual.Type);
        AssertFloat(expected.GetProperty("x").GetSingle(), actual.PositionX); AssertFloat(expected.GetProperty("y").GetSingle(), actual.PositionY);
        AssertFloat(expected.GetProperty("vx").GetSingle(), actual.VelocityX); AssertFloat(expected.GetProperty("vy").GetSingle(), actual.VelocityY);
        Assert.Equal(expected.GetProperty("target").GetInt32(), actual.Target);
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("directionY").GetInt32(), actual.Simulation.DirectionY);
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        float expectedRotation = expected.GetProperty("rotation").GetSingle();
        AssertFloat(expectedRotation, actual.Simulation.Rotation ?? 0f);
        Assert.Equal(expected.GetProperty("damage").GetInt32(), actual.Simulation.DamageOverride ?? actual.Simulation.BaseDamage);
        Assert.Equal(expected.GetProperty("defense").GetInt32(), actual.Simulation.DefenseOverride ?? actual.Simulation.BaseDefense);
        Assert.Equal(ReadAi(expected.GetProperty("ai")), actual.Ai);
        Assert.Equal(ReadAi(expected.GetProperty("localAi")), actual.Simulation.LocalAi);
    }

    private static void AssertProjectiles(RuntimeProjectileStore projectiles, JsonElement expected)
    {
        string expectedSlots = string.Join(',', expected.EnumerateArray().Select(projectile =>
            $"{projectile.GetProperty("slot").GetUInt16()}:{projectile.GetProperty("type").GetInt32()}"));
        var active = new ProjectileSnapshot[projectiles.ActiveCount];
        projectiles.CopyActive(active);
        string actualSlots = string.Join(',', active.Select(projectile => $"{projectile.Handle.Slot}:{projectile.Type.Value}"));
        Assert.True(expected.GetArrayLength() == projectiles.ActiveCount, $"Expected [{expectedSlots}], actual [{actualSlots}].");
        foreach (JsonElement projectile in expected.EnumerateArray())
        {
            Assert.True(projectiles.TryGetActive(projectile.GetProperty("slot").GetUInt16(), out var actual));
            Assert.Equal(projectile.GetProperty("type").GetInt32(), actual.Type.Value);
            AssertFloat(projectile.GetProperty("x").GetSingle(), actual.PositionX); AssertFloat(projectile.GetProperty("y").GetSingle(), actual.PositionY);
            AssertFloat(projectile.GetProperty("vx").GetSingle(), actual.VelocityX); AssertFloat(projectile.GetProperty("vy").GetSingle(), actual.VelocityY);
            Assert.Equal(projectile.GetProperty("damage").GetInt32(), actual.Damage); AssertFloat(projectile.GetProperty("knockBack").GetSingle(), actual.KnockBack);
            Assert.Equal(new ProjectileAiState(projectile.GetProperty("ai")[0].GetSingle(), projectile.GetProperty("ai")[1].GetSingle(), projectile.GetProperty("ai")[2].GetSingle()), actual.Ai);
            Assert.True(projectiles.TryGetLifecycle(actual.Handle, out var lifecycle));
            Assert.Equal(projectile.GetProperty("timeLeft").GetInt32(), lifecycle.TimeLeft);
        }
    }

    private static NpcAiState ReadAi(JsonElement values) => new(
        values[0].GetSingle(), values[1].GetSingle(), values[2].GetSingle(), values[3].GetSingle());

    private static void AssertFloat(float expected, float actual)
    {
        if (OperatingSystem.IsWindows())
            Assert.InRange(actual, expected - .0001f, expected + .0001f);
        else
            Assert.Equal(expected, actual);
    }

    private sealed class CapturedRandom : IVanillaNpcRandom
    {
        private readonly VanillaUnifiedRandom1458 _random = new(0);
        private static readonly FieldInfo Index = typeof(VanillaUnifiedRandom1458).GetField("inext", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo Seeds = typeof(VanillaUnifiedRandom1458).GetField("seedArray", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public CapturedRandom(JsonElement state)
        {
            Index.SetValue(_random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(value => value.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(_random)!, 0);
        }

        public int NextInt32(int min, int max) => _random.Next(min, max);

        public void AssertState(JsonElement state)
        {
            Assert.Equal(state.GetProperty("index").GetUInt32(), (uint)Index.GetValue(_random)!);
            Assert.Equal(state.GetProperty("seed").EnumerateArray().Select(value => value.GetInt32()), (int[])Seeds.GetValue(_random)!);
        }

        public void SetState(JsonElement state)
        {
            Index.SetValue(_random, state.GetProperty("index").GetUInt32());
            state.GetProperty("seed").EnumerateArray().Select(value => value.GetInt32()).ToArray().CopyTo((int[])Seeds.GetValue(_random)!, 0);
        }
    }

    private static JsonElement[] Read()
    {
        string name = OperatingSystem.IsWindows() ? "PrimeEncounterWindows1458" : "PrimeEncounterLinux1458";
        string hash = OperatingSystem.IsWindows()
            ? "f6f3e4270d04c5d9d4617b3e2b4541a2973534a7d0af5c5bf63e6f796c3b9c72"
            : "1c828e3e96b681d9a51aa625d5e8c4e6a1da85900c16302524981e4c4d5332cb";
        using var resource = typeof(PrimeEncounterContinuousTests).Assembly.GetManifestResourceStream(name)!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using var json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(row => row.Clone()).ToArray();
    }
}
