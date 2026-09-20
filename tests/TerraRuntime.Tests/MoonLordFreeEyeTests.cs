using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MoonLordFreeEyeTests
{
    [Fact]
    public void Hover_reacquires_the_closest_player_and_uses_source_above_player_steering()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(0f, 0f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 255);
        var random = new CountingRandom(1);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(3, 1500f, 820f, 0, true, false, false, false)
        {
            VelocityX = 3.25f,
            VelocityY = -1.75f
        }]);

        new RuntimeNpcAiStateExecutor(npcs).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal((ushort)3, next.Target);
        Assert.Equal(0f, next.Ai.Ai0);
        Assert.Equal(1f, next.Ai.Ai1);
        Assert.Equal(2.636120f, next.VelocityX, 5);
        Assert.Equal(-3.282218f, next.VelocityY, 5);
        Assert.Equal(-.012253493f, next.Simulation.LocalAi.Ai0, 5);
        Assert.Equal(.35f, next.Simulation.LocalAi.Ai1, 5);
        Assert.Equal(.52f, next.Simulation.LocalAi.Ai2, 5);
        Assert.True(next.Simulation.DontTakeDamage);
        Assert.Equal(1, random.Draws);
    }

    [Fact]
    public void Hover_separates_every_nearby_true_eye_in_physical_slot_order()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(0f, 0f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 255);
        Spawn(npcs, 2, VanillaNpcIds.MoonLordFreeEye, 950f, 940f, 0f, 0f,
            new NpcAiState(0f, 0f, 0f, 0f), default, 255);
        var random = new CountingRandom(1);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);

        new RuntimeNpcAiStateExecutor(npcs).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(1.386120f, next.VelocityX, 5);
        Assert.Equal(-4.532218f, next.VelocityY, 5);
        Assert.Equal(2, random.Draws);
    }

    [Fact]
    public void Retired_eye_keeps_its_marker_until_the_attack_table_reaches_hover()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 0f, 0f,
            new NpcAiState(-2f, 52f, 0f, 0f), default, 255);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);

        new RuntimeNpcAiStateExecutor(npcs).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(-2f, next.Ai.Ai0);
        Assert.Equal(53f, next.Ai.Ai1);
        Assert.Equal((ushort)0, next.Target);
    }

    [Fact]
    public void Bolt_window_damps_then_fires_from_the_source_pupil_ellipse()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(1f, 128f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore(4);

        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(1f, next.Ai.Ai0);
        Assert.Equal(129f, next.Ai.Ai1);
        Assert.Equal(1.9f, next.VelocityX, 5);
        Assert.Equal(-2.85f, next.VelocityY, 5);
        Assert.Equal(.2f, next.Simulation.LocalAi.Ai0, 5);
        Assert.Equal(.3f, next.Simulation.LocalAi.Ai1, 5);
        Assert.Equal(.4f, next.Simulation.LocalAi.Ai2, 5);

        var shots = new ProjectileSnapshot[4];
        Assert.Equal(1, projectiles.CopyActive(shots));
        Assert.Equal(VanillaProjectileIds.PhantasmalBolt, shots[0].Type);
        Assert.Equal(35, shots[0].Damage);
        Assert.Equal(0f, shots[0].Ai.Ai0);
        Assert.Equal(0f, shots[0].Ai.Ai1);
        Assert.Equal(0f, shots[0].Ai.Ai2);
        Assert.Equal(930.4103f, shots[0].PositionX, 4);
        Assert.Equal(926.894f, shots[0].PositionY, 3);
        Assert.Equal(0.2f, MathF.Atan2(shots[0].VelocityY, shots[0].VelocityX), 5);
    }

    [Fact]
    public void Sphere_spoke_uses_the_source_six_segment_geometry()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(2f, 210f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore(4);

        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(211f, next.Ai.Ai1);
        Assert.Equal(-MathF.PI / 2f, next.Simulation.LocalAi.Ai0, 5);
        Assert.Equal(.65f, next.Simulation.LocalAi.Ai1, 5);
        var shots = new ProjectileSnapshot[4];
        Assert.Equal(1, projectiles.CopyActive(shots));
        Assert.Equal(VanillaProjectileIds.PhantasmalSphere, shots[0].Type);
        Assert.Equal(40, shots[0].Damage);
        Assert.Equal(30f, shots[0].Ai.Ai0);
        Assert.Equal(eye.Handle.Slot, shots[0].Ai.Ai1);
        Assert.Equal(910f, shots[0].PositionX, 5);
        Assert.Equal(880f, shots[0].PositionY, 5);
        Assert.Equal(0f, shots[0].VelocityX, 5);
        Assert.Equal(-4f, shots[0].VelocityY, 5);
    }

    [Fact]
    public void Sphere_release_windup_adds_then_releases_only_unreleased_owned_spheres()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(2f, 270f, MathF.PI / 2f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore(4);
        SpawnSphere(projectiles, eye.Handle, 30f, eye.Handle.Slot, 1f, -2f);
        SpawnSphere(projectiles, eye.Handle, -1f, eye.Handle.Slot, 3f, 4f);
        SpawnSphere(projectiles, eye.Handle, 30f, 2f, 5f, 6f);

        var planned = new NpcStateUpdate(eye.Type, eye.NetId, eye.PositionX, eye.PositionY, eye.VelocityX, eye.VelocityY,
            eye.Target, eye.Ai with { Ai1 = 271f }, eye.Simulation);
        Span<NpcAiProjectileMutationIntent> mutations = stackalloc NpcAiProjectileMutationIntent[1];
        Assert.Equal(1, stepper.PlanProjectileMutations(in eye, in planned, mutations));
        Assert.Equal(NpcAiProjectileVelocityMutation.AddWhileUnreleased, mutations[0].VelocityMutation);

        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));

        var shots = new ProjectileSnapshot[4];
        Assert.Equal(3, projectiles.CopyActive(shots));
        Assert.Equal(1f, shots[0].VelocityX, 5);
        Assert.Equal(-9f, shots[0].VelocityY, 5);
        Assert.Equal(30f, shots[0].Ai.Ai0);
        Assert.Equal(3f, shots[1].VelocityX, 5);
        Assert.Equal(4f, shots[1].VelocityY, 5);
        Assert.Equal(-1f, shots[1].Ai.Ai0);
        Assert.Equal(5f, shots[2].VelocityX, 5);
        Assert.Equal(6f, shots[2].VelocityY, 5);

        var release = new NpcStateUpdate(eye.Type, eye.NetId, eye.PositionX, eye.PositionY, eye.VelocityX, eye.VelocityY,
            eye.Target, eye.Ai with { Ai1 = 300f, Ai2 = MathF.PI / 2f }, eye.Simulation);
        Assert.True(npcs.TryUpdate(eye.Handle, in release, out _));
        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));
        Assert.Equal(3, projectiles.CopyActive(shots));
        Assert.Equal(12f, shots[0].VelocityX, 5);
        Assert.Equal(0f, shots[0].VelocityY, 5);
        Assert.Equal(-1f, shots[0].Ai.Ai0);
    }

    [Fact]
    public void Rotating_eye_window_consumes_source_rng_and_fires_from_its_ellipse()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(3f, 428f, 0f, 0f), new NpcAiState(0f, .3f, .4f, 0f), 0);
        var random = new EyeAttackRandom();
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore(4);

        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));

        float turn = MathF.PI * 2f / 40f * .95f;
        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(429f, next.Ai.Ai1);
        Assert.Equal(turn, next.Ai.Ai2, 5);
        Assert.Equal(turn, next.Simulation.LocalAi.Ai0, 5);
        Assert.Equal(.35f, next.Simulation.LocalAi.Ai1, 5);
        Assert.Equal(turn, MathF.Atan2(next.VelocityY, next.VelocityX), 5);
        Assert.Equal(8f, MathF.Sqrt(next.VelocityX * next.VelocityX + next.VelocityY * next.VelocityY), 5);
        Assert.Equal((turn + MathF.PI / 2f) * .2f, next.Simulation.Rotation!.Value, 5);

        var shots = new ProjectileSnapshot[4];
        Assert.Equal(1, projectiles.CopyActive(shots));
        Assert.Equal(VanillaProjectileIds.PhantasmalEye, shots[0].Type);
        Assert.Equal(35, shots[0].Damage);
        Assert.Equal(turn, MathF.Atan2(shots[0].VelocityY, shots[0].VelocityX), 5);
        Assert.Equal(8f, MathF.Sqrt(shots[0].VelocityX * shots[0].VelocityX + shots[0].VelocityY * shots[0].VelocityY), 5);
        Assert.Equal((-MathF.PI / 60f) + MathF.PI / 180f * turn, shots[0].Ai.Ai1, 5);
        Assert.Equal(3, random.Draws);
    }

    [Fact]
    public void Deathray_window_launches_the_source_beam_at_tick_180()
    {
        var npcs = new RuntimeNpcStore(4);
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, default, 255);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            new NpcAiState(4f, 816f, 0f, 0f), new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: new CountingRandom(1));
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1500f, 820f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore(4);

        new RuntimeNpcAiStateExecutor(npcs, projectiles).Tick(new EyeOnly(stepper));

        Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot next));
        Assert.Equal(817f, next.Ai.Ai1);
        float angle = MathF.Atan2(820f - 930f, 1500f - 930f) + MathF.PI * 2f / 6f;
        Assert.Equal(angle - MathF.PI * 2f / 540f, next.Simulation.LocalAi.Ai0, 5);
        var shots = new ProjectileSnapshot[4];
        Assert.Equal(1, projectiles.CopyActive(shots));
        Assert.Equal(VanillaProjectileIds.PhantasmalDeathray, shots[0].Type);
        Assert.Equal(50, shots[0].Damage);
        Assert.Equal(eye.Handle.Slot, shots[0].Ai.Ai1);
        Assert.Equal(-MathF.PI * 2f / 540f, shots[0].Ai.Ai0, 5);
        Assert.Equal(angle, MathF.Atan2(shots[0].VelocityY, shots[0].VelocityX), 5);
        Assert.Equal(1f, MathF.Sqrt(shots[0].VelocityX * shots[0].VelocityX + shots[0].VelocityY * shots[0].VelocityY), 5);
    }

    [Fact]
    public void Full_attack_cycle_matches_original_state_projectiles_and_random_stream()
    {
        JsonElement[] rows = ReadContinuousCases();
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, new NpcAiState(0f, 0f, 0f, 1f), 0);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            default, new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var random = new ReferenceRandom(1458);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f, 821f, 0, true, false, false, false)]);
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var shots = new ProjectileSnapshot[projectiles.Capacity];

        foreach (JsonElement row in rows)
        {
            executor.Tick(new EyeOnly(stepper));
            Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot actual));
            AssertNear(row.GetProperty("x").GetSingle(), actual.PositionX);
            AssertNear(row.GetProperty("y").GetSingle(), actual.PositionY);
            AssertNear(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            AssertNear(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            AssertAiNear(row.GetProperty("ai"), actual.Ai);
            AssertAiNear(row.GetProperty("local"), actual.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
            Assert.Equal((ushort)row.GetProperty("target").GetInt32(), actual.Target);

            int count = projectiles.CopyActive(shots);
            JsonElement expectedShots = row.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                JsonElement expected = expectedShots[index];
                ProjectileSnapshot shot = shots[index];
                Assert.Equal(expected.GetProperty("type").GetInt32(), shot.Type.Value);
                AssertNear(expected.GetProperty("x").GetSingle(), shot.PositionX);
                AssertNear(expected.GetProperty("y").GetSingle(), shot.PositionY);
                AssertNear(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                AssertNear(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                AssertAiNear(expected.GetProperty("ai"), shot.Ai);
            }
        }

        Assert.Equal(rows[^1].GetProperty("nextRandom").GetInt32(), random.Next());
    }

    [Fact]
    public void Full_attack_cycle_with_a_moving_player_matches_original_state_projectiles_and_random_stream()
    {
        JsonElement[] rows = ReadCases("MoonLordFreeEyeMoving1458", "98fb1acdd8bf04546f3a07a572ab79b7dda643cfcddfdf555d6d88e7b091500e");
        var npcs = new RuntimeNpcStore();
        Spawn(npcs, 0, VanillaNpcIds.MoonLordCore, 1000f, 1000f, 0f, 0f, default, new NpcAiState(0f, 0f, 0f, 1f), 0);
        NpcSnapshot eye = Spawn(npcs, 1, VanillaNpcIds.MoonLordFreeEye, 900f, 900f, 2f, -3f,
            default, new NpcAiState(.2f, .3f, .4f, 0f), 0);
        var random = new ReferenceRandom(1458);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper(), random: random);
        var projectiles = new RuntimeProjectileStore();
        var executor = new RuntimeNpcAiStateExecutor(npcs, projectiles);
        var shots = new ProjectileSnapshot[projectiles.Capacity];
        for (int tick = 0; tick < rows.Length; tick++)
        {
            stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 1510f + tick * 3.25f, 821f - tick * 1.75f, 0,
                true, false, false, false) { VelocityX = 3.25f, VelocityY = -1.75f }]);
            Assert.Equal(1, executor.Tick(new EyeOnly(stepper)).Applied);
            JsonElement row = rows[tick];
            Assert.True(npcs.TryGet(eye.Handle, out NpcSnapshot actual));
            AssertNear(row.GetProperty("x").GetSingle(), actual.PositionX);
            AssertNear(row.GetProperty("y").GetSingle(), actual.PositionY);
            AssertNear(row.GetProperty("vx").GetSingle(), actual.VelocityX);
            AssertNear(row.GetProperty("vy").GetSingle(), actual.VelocityY);
            AssertAiNear(row.GetProperty("ai"), actual.Ai);
            AssertAiNear(row.GetProperty("local"), actual.Simulation.LocalAi);
            Assert.Equal(row.GetProperty("invulnerable").GetBoolean(), actual.Simulation.DontTakeDamage);
            Assert.Equal((ushort)row.GetProperty("target").GetInt32(), actual.Target);
            int count = projectiles.CopyActive(shots);
            JsonElement expectedShots = row.GetProperty("shots");
            Assert.Equal(expectedShots.GetArrayLength(), count);
            for (int index = 0; index < count; index++)
            {
                JsonElement expected = expectedShots[index]; ProjectileSnapshot shot = shots[index];
                Assert.Equal(expected.GetProperty("type").GetInt32(), shot.Type.Value);
                AssertNear(expected.GetProperty("x").GetSingle(), shot.PositionX);
                AssertNear(expected.GetProperty("y").GetSingle(), shot.PositionY);
                AssertNear(expected.GetProperty("vx").GetSingle(), shot.VelocityX);
                AssertNear(expected.GetProperty("vy").GetSingle(), shot.VelocityY);
                AssertAiNear(expected.GetProperty("ai"), shot.Ai);
            }
        }
        Assert.Equal(rows[^1].GetProperty("nextRandom").GetInt32(), random.Next());
    }

    private static JsonElement[] ReadContinuousCases()
    {
        using Stream stream = typeof(MoonLordFreeEyeTests).Assembly
            .GetManifestResourceStream("MoonLordFreeEyeContinuous1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal("51adc7a88fc15afbcacb7194a1ee530c0cd96219143f412d05aa80a69d315074",
            Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(static row => row.Clone()).ToArray();
    }

    private static JsonElement[] ReadCases(string resource, string hash)
    {
        using Stream stream = typeof(MoonLordFreeEyeTests).Assembly.GetManifestResourceStream(resource)!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var bytes = new MemoryStream();
        gzip.CopyTo(bytes);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(bytes.ToArray())));
        using JsonDocument json = JsonDocument.Parse(bytes.ToArray());
        return json.RootElement.EnumerateArray().Select(static row => row.Clone()).ToArray();
    }

    private static void AssertNear(float expected, float actual) =>
        Assert.InRange(MathF.Abs(expected - actual), 0f, .0001f);

    private static void AssertAiNear(JsonElement expected, NpcAiState actual)
    {
        AssertNear(expected[0].GetSingle(), actual.Ai0);
        AssertNear(expected[1].GetSingle(), actual.Ai1);
        AssertNear(expected[2].GetSingle(), actual.Ai2);
        AssertNear(expected[3].GetSingle(), actual.Ai3);
    }

    private static void AssertAiNear(JsonElement expected, ProjectileAiState actual)
    {
        AssertNear(expected[0].GetSingle(), actual.Ai0);
        AssertNear(expected[1].GetSingle(), actual.Ai1);
        AssertNear(expected[2].GetSingle(), actual.Ai2);
    }

    private static void SpawnSphere(RuntimeProjectileStore store, NpcHandle source, float ai0, float ai1, float vx, float vy)
    {
        var intent = new NpcAiProjectileIntent(VanillaProjectileIds.PhantasmalSphere, 900f, 900f, vx, vy, 40, 0f)
        {
            InitialAi = new ProjectileAiState(ai0, ai1, 0f)
        };
        Assert.True(RuntimeNpcProjectileIntentApplier.TryApply(store, source, in intent, out _));
    }

    private static NpcSnapshot Spawn(RuntimeNpcStore store, byte slot, NpcTypeId type, float x, float y,
        float vx, float vy, NpcAiState ai, NpcAiState local, ushort target)
    {
        var state = new NpcStateUpdate(type.Value, checked((short)type.Value), x, y, vx, vy, target, ai,
            NpcSimulationState.Initial with { LocalAi = local });
        Assert.True(store.TrySpawn(slot, in state, out NpcSnapshot npc));
        return npc;
    }

    private sealed class CountingRandom(int value) : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Assert.Equal(0, inclusiveMin);
            Assert.Equal(420, exclusiveMax);
            Draws++;
            return value;
        }
    }

    private sealed class EyeAttackRandom : IVanillaNpcRandom
    {
        public int Draws { get; private set; }
        public int NextInt32(int inclusiveMin, int exclusiveMax)
        {
            Draws++;
            return (inclusiveMin, exclusiveMax) switch
            {
                (0, 420) => 1,
                (0, 2) => 0,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected random range {inclusiveMin}..{exclusiveMax}.")
            };
        }

        public double NextDouble()
        {
            Draws++;
            return .25d;
        }
    }

    private sealed class ReferenceRandom(int seed) : IVanillaNpcRandom
    {
        private readonly Random random = new(seed);
        public int NextInt32(int inclusiveMin, int exclusiveMax) => random.Next(inclusiveMin, exclusiveMax);
        public double NextDouble() => random.NextDouble();
        public int Next() => random.Next();
    }

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }

    private sealed class EyeOnly(INpcAiStateStepper inner) : INpcAiStateStepper, INpcAiStateStepperWrapper
    {
        public INpcAiStateStepper InnerStepper => inner;

        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            if (npc.TypeIdentity == VanillaNpcIds.MoonLordFreeEye)
                return inner.TryStepState(in npc, out next);
            next = default;
            return false;
        }
    }
}
