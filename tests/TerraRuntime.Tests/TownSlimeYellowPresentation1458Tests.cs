using System.IO.Compression;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class TownSlimeYellowPresentation1458Tests
{
    public static IEnumerable<object[]> OriginalFrames()
    {
        foreach (string resource in new[] { "TownSlimeYellowFindFrameLinux1458", "TownSlimeYellowFindFrameEdgesLinux1458" })
        {
            using var stream = typeof(TownSlimeYellowPresentation1458Tests).Assembly.GetManifestResourceStream(resource)!;
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var document = JsonDocument.Parse(gzip);
            foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
                yield return [row.GetRawText()];
        }
    }

    [Theory, MemberData(nameof(OriginalFrames))]
    public void Actual_source_town_slime_pose_preserves_just_hit_and_nonpose_state(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var before = row.GetProperty("input");
        var expected = row.GetProperty("after");
        var local = before.GetProperty("localAi").EnumerateArray().Select(Single).ToArray();
        var ai = before.GetProperty("ai").EnumerateArray().Select(Single).ToArray();
        var simulation = NpcSimulationState.Initial with
        {
            FrameIndex = before.GetProperty("frameIndex").GetInt32(),
            FrameCounter = BitConverter.UInt64BitsToDouble(Convert.ToUInt64(before.GetProperty("frameCounter").GetString(), 16)),
            DirectionX = before.GetProperty("direction").GetInt32(),
            SpriteDirection = before.GetProperty("spriteDirection").GetInt32(),
            Rotation = Single(before.GetProperty("rotation")),
            JustHit = before.GetProperty("justHit").GetBoolean(),
            Wet = before.GetProperty("wet").GetBoolean(),
            LocalAi = new(local[0], local[1], local[2], local[3]),
            Life = before.GetProperty("life").GetInt32(),
            LifeMax = before.GetProperty("lifeMax").GetInt32(),
            DefenseOverride = before.GetProperty("defense").GetInt32(),
            DamageOverride = before.GetProperty("damage").GetInt32(),
            Scale = Single(before.GetProperty("scale")),
            Friendly = before.GetProperty("friendly").GetBoolean(),
            TownNpc = before.GetProperty("townNpc").GetBoolean(),
            CatchableNpcTempImmunityCounter = before.GetProperty("catchCounter").GetInt32()
        };
        var motion = new NpcStateUpdate(683, 683,
            Single(before.GetProperty("position")[0]), Single(before.GetProperty("position")[1]),
            Single(before.GetProperty("velocity")[0]), Single(before.GetProperty("velocity")[1]),
            255, new(ai[0], ai[1], ai[2], ai[3]), simulation);
        Assert.True(RuntimeTownNpcSchedule1458.TryFinishSlimeYellowPresentation(in motion, out var actual));
        Assert.Equal(expected.GetProperty("frameIndex").GetInt32(), actual.Simulation.FrameIndex);
        Assert.Equal(expected.GetProperty("frameCounter").GetString(), BitConverter.DoubleToUInt64Bits(actual.Simulation.FrameCounter).ToString("x16"));
        Assert.Equal(expected.GetProperty("spriteDirection").GetInt32(), actual.Simulation.SpriteDirection);
        Assert.Equal(simulation with
        {
            FrameIndex = actual.Simulation.FrameIndex,
            FrameCounter = actual.Simulation.FrameCounter,
            SpriteDirection = actual.Simulation.SpriteDirection
        }, actual.Simulation);
        Assert.Equal(motion, actual with { Simulation = simulation });
        Assert.True(actual.Simulation.JustHit);
        var aliased = motion;
        Assert.True(RuntimeTownNpcSchedule1458.TryFinishSlimeYellowPresentation(in aliased, out aliased));
        Assert.Equal(actual, aliased);
        Assert.Equal(row.GetProperty("rngBefore").GetRawText(), row.GetProperty("rngAfter").GetRawText());
        foreach (var field in before.EnumerateObject())
            if (field.Name is not ("frameIndex" or "frameCounter" or "spriteDirection"))
                Assert.Equal(field.Value.GetRawText(), expected.GetProperty(field.Name).GetRawText());
    }

    private static float Single(JsonElement value) =>
        BitConverter.UInt32BitsToSingle(Convert.ToUInt32(value.GetString(), 16));
}
