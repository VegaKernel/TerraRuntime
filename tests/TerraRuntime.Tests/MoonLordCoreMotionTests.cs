using System.Buffers.Binary;
using System.Security.Cryptography;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Gameplay.Npcs;

namespace TerraRuntime.Tests;

public sealed class MoonLordCoreMotionTests
{
    // Captured from unmodified TerrariaServer 1.4.5.8 AI_077_MoonLordCore, exposed state1.
    // Each sample retains little-endian velocityX/velocityY float bits, target int32 and ai0 float bits.
    // Player movement/initial velocity are independent inputs; no runtime encoder generates the oracle.
    [Theory]
    [InlineData(0, "EC5029827B0A551BC205BC06DB3BE77D5DE06B6DCC17765F1888F88DB67578A0")]
    [InlineData(1, "3A43342EB93E5C69B2738FF1FA80E59AE2437BC1BD00A355C306BC4074343854")]
    [InlineData(2, "CE87E5A209ED071478696D424F58E30062F5DE3AA161E79B90EEB6825A1A58EE")]
    [InlineData(3, "482FF33DF82C318082E46DA4FF5B5185040907AB23755FA6A330F9A359EBB362")]
    [InlineData(4, "C10CBF0FEBDCA862EE88C2F260210650F59A7FB0B93CA38B096FB74BE809208F")]
    [InlineData(5, "A071A1AFA62A3FB7456CB0C78CF38239A67D3804B232B880F28DA9B8B0742A64")]
    [InlineData(6, "9D91F532D5E8E4AA5EDD5D8239087150791DF4DCAA22A2C92FBF3585B7C0D8DD")]
    [InlineData(7, "A51326B5CABE6AAEC7AAD7E9DBC52D73581C1E456E763AEF7A461A71DA2B900A")]
    [InlineData(8, "CCD146F73A2155355AF5CC4706CE0DA5344BAAB2A5F17B0DA06CECD04A826155")]
    [InlineData(9, "E0AA06069490B0B28BC9D38F9A677A2583BD5435785F25D43B7AE1CBAE7F875A")]
    public void Exposed_core_matches_official_motion_and_target_goldens(int seed, string expected)
    {
        var random = new Random(seed);
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        byte[] results = new byte[200 * 16];
        for (int i = 0; i < 200; i++)
        {
            float px = 123 + random.Next(-1000, 1001), py = 3 + random.Next(-1000, 1001);
            float vx = random.Next(-100, 101) / 8f, vy = random.Next(-100, 101) / 8f;
            float px2 = 123 + random.Next(-800, 801), py2 = 3 + random.Next(-800, 801);
            bool active2 = i % 3 == 0;
            NpcSnapshot core = Core(vx, vy, (ushort)(active2 ? 1 : 0));
            stepper.SetCandidates([
                new VanillaNpcTargetCandidate(0, px, py, 0, true, false, false, false),
                new VanillaNpcTargetCandidate(1, px2, py2, 0, active2, false, false, false)
            ]);
            Assert.True(stepper.TryStepState(in core, out NpcStateUpdate next));
            Span<byte> sample = results.AsSpan(i * 16, 16);
            BinaryPrimitives.WriteSingleLittleEndian(sample, next.VelocityX);
            BinaryPrimitives.WriteSingleLittleEndian(sample[4..], next.VelocityY);
            BinaryPrimitives.WriteInt32LittleEndian(sample[8..], next.Target);
            BinaryPrimitives.WriteSingleLittleEndian(sample[12..], next.Ai.Ai0);
        }
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(results)));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(20f, 0f)]
    [InlineData(-20f, 0f)]
    [InlineData(0f, 20f)]
    [InlineData(12f, 16f)]
    public void Within_twenty_pixels_core_keeps_existing_velocity(float dx, float dy)
    {
        var stepper = new VanillaNpcTargetingAiStepper(new RejectingStepper());
        NpcSnapshot core = Core(3f, -4f, 0);
        stepper.SetCandidates([new VanillaNpcTargetCandidate(0, 123f + dx, 3f + dy, 0, true, false, false, false)]);
        Assert.True(stepper.TryStepState(in core, out NpcStateUpdate next));
        Assert.Equal(core.VelocityX, next.VelocityX);
        Assert.Equal(core.VelocityY, next.VelocityY);
    }

    private static NpcSnapshot Core(float vx, float vy, ushort target) =>
        new(new NpcHandle(0, new NpcGeneration(1)), new NpcRevision(1), 398, 398,
            100f, 100f, vx, vy, target, new NpcAiState(1f, 0f, 0f, 0f),
            NpcSimulationState.Initial with
            {
                Life = 50_000, LifeMax = 50_000, DirectionX = 1,
                LocalAi = new NpcAiState(0f, 0f, 0f, 1f)
            });

    private sealed class RejectingStepper : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate next)
        {
            next = default;
            return false;
        }
    }
}
