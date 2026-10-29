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
    public static IEnumerable<object[]> OriginalHomePhases()
    {
        using var stream = typeof(NpcTransformInteractions1458Tests).Assembly
            .GetManifestResourceStream("TownTransformHomePhaseLinux1458.v1")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [row.GetRawText()];
    }

    [Theory, MemberData(nameof(OriginalHomePhases))]
    public void Source_home_call_and_network_tail_keep_their_order_after_powder_and_on_subsequent_ticks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var row = document.RootElement;
        var input = row.GetProperty("input");
        var original = input.GetProperty("npc");
        using var f = new Fixture(row.GetProperty("seed").GetInt32(), [original], withWorldInfo: true,
            clock: new RuntimeWorldClock(0d, false, VanillaMoonPhase.Full, 0d, 0));
        var player = input.GetProperty("players")[0];
        f.MovePlayer(F(player.GetProperty("position")[0]), F(player.GetProperty("position")[1]));
        var appearance = new PlayerAppearanceCommitRequest(f.Connection.Player.Slot, 0, 0, 0, 0,
            player.GetProperty("name").GetString()!, 0, 0, 0,
            default, default, default, default, default, default, default, 0, 0, 0);
        f.World.Apply(new PlayerAppearanceRuntimeCommand(f.Connection, appearance));
        var types = original.GetProperty("rawBuffTypes");
        var durations = original.GetProperty("rawBuffTimes");
        for (int i = 0; i < 20; i++)
            if (types[i].GetInt32() != 0)
                Assert.True(f.Status.TryApply(f.Actors[0].Handle, new(types[i].GetInt32()), durations[i].GetInt32()));
        AssertProjection(original, f.Current(0));
        Assert.Equal(original.GetProperty("stairFall").GetBoolean(), f.Current(0).Simulation.StairFall);
        GoldCritterHitEffect1458Tests.AssertRandom(input.GetProperty("rng"), f.Random);
        GemBunnyWire1458Tests.Drain(f.Outbound);
        Assert.True(f.Powder.TryApply(10, (int)F(original.GetProperty("position")[0]),
            (int)F(original.GetProperty("position")[1]), original.GetProperty("width").GetInt32(),
            original.GetProperty("height").GetInt32(), null, out int transformed));
        Assert.Equal(1, transformed);
        var actualFrames = new List<byte[]>(GemBunnyWire1458Tests.Drain(f.Outbound));
        Check(row.GetProperty("component").GetProperty("after"));

        var graph = (ServerRuntimeComposition)typeof(ServerRuntimeState)
            .GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.World)!;
        object cycle = typeof(NpcAuthority).GetField("sourceSpawnCycle", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(graph.Npcs)!;
        foreach (var frame in row.GetProperty("frames").EnumerateArray())
        {
            cycle.GetType().GetMethod("SuppressNext", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cycle, null);
            graph.Npcs.AdvanceWorldTick();
            graph.Npcs.TickSimulation();
            actualFrames.AddRange(GemBunnyWire1458Tests.Drain(f.Outbound));
            Check(frame.GetProperty("after"));
        }

        void Check(JsonElement expected)
        {
            AssertProjection(expected.GetProperty("npc"), f.Current(0));
            GoldCritterHitEffect1458Tests.AssertRandom(expected.GetProperty("rng"), f.Random);
            // This home fixture has its own bootstrap identity. Production packet7
            // is compared separately against the source in the full phase carrier.
            var sourceFrames = expected.GetProperty("notifications").EnumerateArray()
                .Select(v => Convert.FromHexString(v.GetString()!)).Where(v => v[2] != 7)
                .Select(Convert.ToHexString).ToArray();
            Assert.Equal(sourceFrames, actualFrames.Where(v => v[2] != 7).Select(Convert.ToHexString).ToArray());
        }
    }
}
