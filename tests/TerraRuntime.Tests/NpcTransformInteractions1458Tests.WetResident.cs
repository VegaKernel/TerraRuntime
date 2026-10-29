using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed partial class NpcTransformInteractions1458Tests
{
    public static IEnumerable<object[]> OriginalWetResidents()
    {
        using var stream = typeof(NpcTransformInteractions1458Tests).Assembly
            .GetManifestResourceStream("WetResidentPhaseLinux1458.v1")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalWetResidents))]
    public void Actual_powder_then_wet_resident_phase_matches_source_drowning_rollover_pose_and_random(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var input = row.GetProperty("input");
        var original = input.GetProperty("npc");
        var clock = new RuntimeWorldClock(0d, false, VanillaMoonPhase.Full, 0d, 0);
        using var f = new Fixture(row.GetProperty("seed").GetInt32(), [original], withWorldInfo: true, clock: clock);
        for (int y = 97; y < 100; y++)
        for (int x = 0; x < 192; x++)
            f.Tiles.Set(x, y, f.Tiles.Get(x, y) with { LiquidAmount = 255 });
        var rawPlayer = input.GetProperty("players")[0];
        f.MovePlayer(F(rawPlayer.GetProperty("position")[0]), F(rawPlayer.GetProperty("position")[1]));
        var appearance = new PlayerAppearanceCommitRequest(f.Connection.Player.Slot, 0, 0, 0, 0,
            rawPlayer.GetProperty("name").GetString()!, 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0);
        f.World.Apply(new PlayerAppearanceRuntimeCommand(f.Connection, appearance));
        AssertProjection(original, f.Current(0));
        GoldCritterHitEffect1458Tests.AssertRandom(input.GetProperty("rng"), f.Random);
        Assert.True(f.Powder.TryApply(10,
            (int)F(original.GetProperty("position")[0]), (int)F(original.GetProperty("position")[1]),
            original.GetProperty("width").GetInt32(), original.GetProperty("height").GetInt32(), null, out int transformed));
        Assert.Equal(1, transformed);
        var component = row.GetProperty("component").GetProperty("after");
        AssertProjection(component.GetProperty("npc"), f.Current(0));
        Assert.Equal(component.GetProperty("npc").GetProperty("breathCounter").GetInt32(), f.Current(0).Simulation.BreathCounter);
        GoldCritterHitEffect1458Tests.AssertRandom(component.GetProperty("rng"), f.Random);

        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.World)!;
        object cycle = typeof(NpcAuthority).GetField("sourceSpawnCycle", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(graph.Npcs)!;
        int[] selected = [0, 5, 6, 13];
        foreach (var sourceFrame in row.GetProperty("frames").EnumerateArray())
        {
            // The source capture calls NPC.UpdateNPC with noSpawnCycle, without advancing world/player time.
            cycle.GetType().GetMethod("SuppressNext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cycle, null);
            graph.Npcs.AdvanceWorldTick();
            graph.Npcs.TickSimulation();
            var expected = sourceFrame.GetProperty("after");
            GoldCritterHitEffect1458Tests.AssertRandom(expected.GetProperty("rng"), f.Random);
            if (!selected.Contains(sourceFrame.GetProperty("index").GetInt32())) continue;
            var npc = expected.GetProperty("npc");
            var actual = f.Current(0);
            AssertProjection(npc, actual);
            Assert.Equal(npc.GetProperty("breath").GetInt32(), actual.Simulation.Breath);
            Assert.Equal(npc.GetProperty("breathCounter").GetInt32(), actual.Simulation.BreathCounter);
            Assert.Equal(npc.GetProperty("frameIndex").GetInt32(), actual.Simulation.FrameIndex);
            Assert.Equal(npc.GetProperty("frameCounter").GetString(), BitConverter.DoubleToUInt64Bits(actual.Simulation.FrameCounter).ToString("x16"));
            Assert.Equal(npc.GetProperty("wet").GetBoolean(), actual.Simulation.Wet);
            Assert.Equal(npc.GetProperty("justHit").GetBoolean(), actual.Simulation.JustHit);
            Assert.Equal(npc.GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
            Assert.Equal(npc.GetProperty("rotation").GetString(), BitConverter.SingleToUInt32Bits(Assert.IsType<float>(actual.Simulation.Rotation)).ToString("x8"));
            Assert.Equal(npc.GetProperty("position")[0].GetString(), BitConverter.SingleToUInt32Bits(actual.PositionX).ToString("x8"));
            Assert.Equal(npc.GetProperty("position")[1].GetString(), BitConverter.SingleToUInt32Bits(actual.PositionY).ToString("x8"));
            Assert.Equal(npc.GetProperty("velocity")[0].GetString(), BitConverter.SingleToUInt32Bits(actual.VelocityX).ToString("x8"));
            Assert.Equal(npc.GetProperty("velocity")[1].GetString(), BitConverter.SingleToUInt32Bits(actual.VelocityY).ToString("x8"));
        }
    }
}
