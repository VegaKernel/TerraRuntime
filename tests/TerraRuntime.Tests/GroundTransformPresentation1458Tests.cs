using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GroundTransformPresentation1458Tests
{
    public static IEnumerable<object[]> OriginalFrames()
    {
        using var stream = typeof(GroundTransformPresentation1458Tests).Assembly
            .GetManifestResourceStream("GroundTransformFramesLinux1458")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalFrames))]
    public void Actual_source_pose_includes_swimmer_direction_and_falling_walker_rotation(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var before = row.GetProperty("input");
        var expected = row.GetProperty("after");
        var localAi = before.GetProperty("localAi").EnumerateArray().Select(Single).ToArray();
        var ai = before.GetProperty("ai").EnumerateArray().Select(Single).ToArray();
        var simulation = NpcSimulationState.Initial with
        {
            FrameIndex = before.GetProperty("frameIndex").GetInt32(),
            FrameCounter = Wide(before.GetProperty("frameCounter")),
            DirectionX = before.GetProperty("direction").GetInt32(),
            SpriteDirection = before.GetProperty("spriteDirection").GetInt32(),
            Rotation = Single(before.GetProperty("rotation")),
            JustHit = before.GetProperty("justHit").GetBoolean(),
            Wet = before.GetProperty("wet").GetBoolean(),
            LocalAi = new(localAi[0], localAi[1], localAi[2], localAi[3]),
            Life = before.GetProperty("life").GetInt32(),
            LifeMax = before.GetProperty("lifeMax").GetInt32(),
            DefenseOverride = before.GetProperty("defense").GetInt32(),
            DamageOverride = before.GetProperty("damage").GetInt32(),
            Scale = Single(before.GetProperty("scale")),
            Friendly = before.GetProperty("friendly").GetBoolean(),
            TownNpc = before.GetProperty("townNpc").GetBoolean(),
            CatchableNpcTempImmunityCounter = before.GetProperty("catchCounter").GetInt32()
        };
        int type = before.GetProperty("type").GetInt32();
        var motion = new NpcStateUpdate(type, checked((short)before.GetProperty("netId").GetInt32()),
            Single(before.GetProperty("position")[0]), Single(before.GetProperty("position")[1]),
            Single(before.GetProperty("velocity")[0]), Single(before.GetProperty("velocity")[1]),
            255, new(ai[0], ai[1], ai[2], ai[3]), simulation);

        NpcStateUpdate actual;
        bool accepted = type is 230 or 593 or 687
            ? VanillaGroundCritterPresentation1458.TryFinish(in motion, out actual)
            : VanillaGroundCombatPresentation1458.TryFinish(in motion, out actual);
        Assert.True(accepted);
        Assert.Equal(expected.GetProperty("frameIndex").GetInt32(), actual.Simulation.FrameIndex);
        Assert.Equal(expected.GetProperty("frameCounter").GetString(),
            BitConverter.DoubleToUInt64Bits(actual.Simulation.FrameCounter).ToString("x16"));
        Assert.Equal(expected.GetProperty("direction").GetInt32(), actual.Simulation.DirectionX);
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        float rotation = Assert.IsType<float>(actual.Simulation.Rotation);
        Assert.Equal(expected.GetProperty("rotation").GetString(),
            BitConverter.SingleToUInt32Bits(rotation).ToString("x8"));
        Assert.Equal(motion, actual with { Simulation = simulation });
        Assert.Equal(simulation with
        {
            FrameIndex = actual.Simulation.FrameIndex,
            FrameCounter = actual.Simulation.FrameCounter,
            DirectionX = actual.Simulation.DirectionX,
            SpriteDirection = actual.Simulation.SpriteDirection,
            Rotation = actual.Simulation.Rotation,
            JustHit = false
        }, actual.Simulation);
        // FindFrame keeps JustHit. The retained outer phase clears it after presentation.
        Assert.False(actual.Simulation.JustHit);
        Assert.Equal(row.GetProperty("rngBefore").GetRawText(), row.GetProperty("rngAfter").GetRawText());
        foreach (var field in before.EnumerateObject())
            if (field.Name is not ("frameIndex" or "frameCounter" or "spriteDirection" or "rotation" or "direction"))
                Assert.Equal(field.Value.GetRawText(), expected.GetProperty(field.Name).GetRawText());
    }

    private static float Single(JsonElement value) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));

    private static double Wide(JsonElement value) =>
        BitConverter.UInt64BitsToDouble(Convert.ToUInt64(value.GetString(), 16));
}
