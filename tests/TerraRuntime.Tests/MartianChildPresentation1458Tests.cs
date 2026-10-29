using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class MartianChildPresentation1458Tests
{
    [Fact]
    public void Child_frames_match_independent_original_turret_edges_rider_facing_and_maggot_slopes()
    {
        using var resource = typeof(MartianChildPresentation1458Tests).Assembly.GetManifestResourceStream("MartianChildFramesLinux1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        int count = 0;
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("input");
            var expected = row.GetProperty("after");
            var motion = new NpcStateUpdate(I(input, "type"), (short)I(input, "netId"),
                F(input.GetProperty("position")[0]), F(input.GetProperty("position")[1]),
                F(input.GetProperty("velocity")[0]), F(input.GetProperty("velocity")[1]), 0, A(input.GetProperty("ai")),
                NpcSimulationState.Initial with { Life = I(input, "life"), LifeMax = I(input, "lifeMax"),
                    DirectionX = I(input, "direction"), SpriteDirection = I(input, "spriteDirection"),
                    FrameIndex = I(input, "frameIndex"), FrameCounter = D(input.GetProperty("frameCounter")),
                    LocalAi = A(input.GetProperty("localAi")), JustHit = input.GetProperty("justHit").GetBoolean(),
                    Rotation = F(input.GetProperty("rotation")), Wet = input.GetProperty("wet").GetBoolean() });
            // Aliased input/output is a real caller shape; snapshot before clearing any out carrier.
            Assert.True(VanillaNpcPhaseEnvironment1458.TryFinishChildFrame(in motion,
                I(row, "primarySlope"), I(row, "belowSlope"), out motion));
            Assert.Equal(I(expected, "frameIndex"), motion.Simulation.FrameIndex);
            Assert.Equal(BitConverter.DoubleToUInt64Bits(D(expected.GetProperty("frameCounter"))),
                BitConverter.DoubleToUInt64Bits(motion.Simulation.FrameCounter));
            Assert.Equal(BitConverter.SingleToUInt32Bits(F(expected.GetProperty("rotation"))),
                BitConverter.SingleToUInt32Bits(motion.Simulation.Rotation!.Value));
            Assert.Equal(A(expected.GetProperty("localAi")), motion.Simulation.LocalAi);
            Assert.Equal(I(expected, "spriteDirection"), motion.Simulation.SpriteDirection);
            Assert.Equal(I(expected, "direction"), motion.Simulation.DirectionX);
            Assert.Equal(A(expected.GetProperty("ai")), motion.Ai);
            Assert.Equal(expected.GetProperty("justHit").GetBoolean(), motion.Simulation.JustHit);
            Assert.Equal(expected.GetProperty("velocity").EnumerateArray().Select(x => x.GetString()).ToArray(),
                new[] { BitConverter.SingleToUInt32Bits(motion.VelocityX).ToString("x8"), BitConverter.SingleToUInt32Bits(motion.VelocityY).ToString("x8") });
            count++;
        }
        Assert.Equal(144, count);
    }

    private static int I(JsonElement x, string name) => x.GetProperty(name).GetInt32();
    private static float F(JsonElement x) => BitConverter.UInt32BitsToSingle(uint.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static double D(JsonElement x) => BitConverter.UInt64BitsToDouble(ulong.Parse(x.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    private static NpcAiState A(JsonElement x) => new(F(x[0]), F(x[1]), F(x[2]), F(x[3]));
}
