using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundCombatPresentation1458Tests
{
    public static IEnumerable<object[]> OriginalFrames()
    {
        using var stream = typeof(GroundCombatPresentation1458Tests).Assembly.GetManifestResourceStream("GroundCombatFramesLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray()) yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFrames))]
    public void Dedicated_pose_matches_actual_source_frame_counter_and_facing_without_motion_changes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var before = row.GetProperty("input");
        var expected = row.GetProperty("after");
        var simulation = NpcSimulationState.Initial with
        {
            FrameIndex = before.GetProperty("frameIndex").GetInt32(),
            FrameCounter = Wide(before.GetProperty("frameCounter")),
            SpriteDirection = before.GetProperty("spriteDirection").GetInt32(),
            DirectionX = before.GetProperty("direction").GetInt32(),
            JustHit = before.GetProperty("justHit").GetBoolean()
        };
        var ai = before.GetProperty("ai").EnumerateArray().Select(Single).ToArray();
        var motion = new NpcStateUpdate(before.GetProperty("type").GetInt32(),
            checked((short)before.GetProperty("netId").GetInt32()),
            Single(before.GetProperty("position")[0]), Single(before.GetProperty("position")[1]),
            Single(before.GetProperty("velocity")[0]), Single(before.GetProperty("velocity")[1]),
            255, new NpcAiState(ai[0], ai[1], ai[2], ai[3]), simulation);

        Assert.True(VanillaGroundCombatPresentation1458.TryFinish(in motion, out var actual));
        Assert.Equal(expected.GetProperty("frameIndex").GetInt32(), actual.Simulation.FrameIndex);
        Assert.Equal(expected.GetProperty("frameCounter").GetString(), BitConverter.DoubleToUInt64Bits(actual.Simulation.FrameCounter).ToString("x16"));
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(motion, actual with { Simulation = motion.Simulation });
        Assert.Equal(motion.Simulation with { FrameIndex = actual.Simulation.FrameIndex,
            FrameCounter = actual.Simulation.FrameCounter, SpriteDirection = actual.Simulation.SpriteDirection,
            JustHit = false }, actual.Simulation);
        // FindFrame itself keeps JustHit; the retained phase clears it after AI.
        // This differential qualifies pose fields, not a whole UpdateNPC call.
        Assert.False(actual.Simulation.JustHit);
        Assert.Equal(row.GetProperty("rngBefore").GetRawText(), row.GetProperty("rngAfter").GetRawText());
        foreach (var field in before.EnumerateObject())
            if (field.Name is not ("frameIndex" or "frameCounter" or "spriteDirection"))
                Assert.Equal(field.Value.GetRawText(), expected.GetProperty(field.Name).GetRawText());
    }

    private static float Single(JsonElement value) => BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
    private static double Wide(JsonElement value) => BitConverter.UInt64BitsToDouble(Convert.ToUInt64(value.GetString(), 16));
}
