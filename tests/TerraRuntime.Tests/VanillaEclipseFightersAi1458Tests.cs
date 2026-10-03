using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Gameplay.Npcs;
namespace TerraRuntime.Tests;

public sealed class VanillaEclipseFightersAi1458Tests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (string name in new[] { "eclipse-fighters", "drmanfly-targets", "nailhead-volley" })
        {
            using var resource = typeof(VanillaEclipseFightersAi1458Tests).Assembly.GetManifestResourceStream(name + "Ai003Linux1458")!;
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gzip);
            foreach (var row in doc.RootElement.EnumerateArray()) yield return new object[] { row.Clone() };
        }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void Whole_AI_and_projectile_tail_match_original_server(JsonElement row)
    {
        float F(string name) => row.GetProperty(name).GetSingle();
        int I(string name) => row.GetProperty(name).GetInt32();
        int type = I("type"), max = type == 460 ? 700 : type == 463 ? 4000 : 500;
        var random = new SystemVanillaNpcRandom(I("seed"));
        var stepper = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        stepper.EnableZombieMotion(140d);
        stepper.SetWorldConditions(dayTime: row.GetProperty("day").GetBoolean(), slimeRainActive: false, eclipseActive: row.GetProperty("eclipse").GetBoolean());
        bool hasTargets = row.TryGetProperty("dx", out var dxNode);
        float dx = hasTargets ? dxNode.GetSingle() : 200f, dy = hasTargets ? F("dy") : 0f;
        var candidates = new VanillaNpcTargetCandidate[Math.Max(1, I("playerCount"))];
        for (int i = 0; i < candidates.Length; i++) candidates[i] = new((byte)i, 1009 + dx + 80 * i, 1020 + dy + 40 * i, 0, i < I("playerCount"), false, false, false)
        { Stealth = hasTargets ? F("stealth") : 1f, ItemAnimation = hasTargets ? I("animation") : 0 };
        stepper.SetCandidates(candidates);
        stepper.SetProjectileEnvironment(new Environment(!hasTargets || row.GetProperty("visible").GetBoolean()));
        var ai = new NpcAiState(0, type == 468 ? F("clock") : 0, type == 468 && F("clock") > 0 ? 3 : 0, 0);
        var input = new NpcSnapshot(new NpcHandle(199, new NpcGeneration(1)), new NpcRevision(1), (short)type, (short)type, 1000, 1000, F("vx"), F("vy"), 0, ai,
            NpcSimulationState.Initial with
            {
                Life = (int)(max * F("hpFraction")),
                LifeMax = max,
                OldPositionX = 999,
                DirectionX = 1,
                DirectionY = 1,
                SpriteDirection = 1,
                JustHit = row.GetProperty("hit").GetBoolean(),
                TimeLeft = 750,
                SpawnDifficulty = F("difficulty"),
                KnockBackResist = type == 460 ? .25f : type == 463 ? .1f : .6f,
                LocalAi = new NpcAiState(0, 0, 0, type == 463 ? F("clock") : 0)
            });
        Assert.True(stepper.TryStepState(in input, out var proposed));
        var next = proposed;
        Span<NpcAiProjectileIntent> shots = stackalloc NpcAiProjectileIntent[5];
        int count = 0;
        if (type != 460)
        {
            var accepted = new NpcSnapshot(input.Handle, new NpcRevision(2), proposed.Type, proposed.NetId, proposed.PositionX, proposed.PositionY, proposed.VelocityX, proposed.VelocityY, proposed.Target, proposed.Ai, proposed.Simulation);
            Assert.True(stepper.TryPlanBeforeWorldMotion(in input, in accepted, shots, out count, out next));
        }
        Assert.Equal(F("outVx"), next.VelocityX);
        Assert.Equal(F("outVy"), next.VelocityY);
        var outAi = row.GetProperty("outAi");
        Assert.Equal(new NpcAiState(outAi[0].GetSingle(), outAi[1].GetSingle(), outAi[2].GetSingle(), outAi[3].GetSingle()), next.Ai);
        var local = row.GetProperty("outLocal");
        Assert.Equal(new NpcAiState(local[0].GetSingle(), local[1].GetSingle(), local[2].GetSingle(), local[3].GetSingle()), next.Simulation.LocalAi);
        Assert.Equal(F("knockback"), next.Simulation.KnockBackResist);
        Assert.Equal(I("direction"), next.Simulation.DirectionX);
        Assert.Equal(I("directionY"), next.Simulation.DirectionY);
        Assert.Equal(I("sprite"), next.Simulation.SpriteDirection);
        Assert.Equal(I("target"), next.Target);
        Assert.Equal(I("timeLeft"), next.Simulation.TimeLeft);
        var expectedShots = row.GetProperty("shots");
        Assert.Equal(expectedShots.GetArrayLength(), count);
        for (int i = 0; i < count; i++)
        {
            var expected = expectedShots[i];
            float half = expected.GetProperty("type").GetInt32() == 498 ? 3f : 7.5f;
            Assert.Equal(expected.GetProperty("type").GetInt32(), shots[i].Type.Value);
            Assert.Equal(expected.GetProperty("x").GetSingle() + half, shots[i].PositionX);
            Assert.Equal(expected.GetProperty("y").GetSingle() + half, shots[i].PositionY);
            Assert.Equal(expected.GetProperty("vx").GetSingle(), shots[i].VelocityX);
            Assert.Equal(expected.GetProperty("vy").GetSingle(), shots[i].VelocityY);
            Assert.Equal(expected.GetProperty("damage").GetInt32(), shots[i].Damage);
            Assert.Equal(expected.GetProperty("knockback").GetSingle(), shots[i].KnockBack);
        }
        Assert.Equal(I("nextRandom"), random.NextInt32(0, int.MaxValue));
    }
    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
    private sealed class Environment(bool visible) : IVanillaNpcProjectileEnvironment, IVanillaNpcProjectileLineEnvironment
    {
        public bool CanHit(float x, float y, int w, int h, float tx, float ty, int tw, int th) => visible;
        public bool CanHitLine(float x, float y, int w, int h, float tx, float ty, int tw, int th) => visible;
    }
}
