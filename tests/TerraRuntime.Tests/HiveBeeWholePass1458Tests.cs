using System.Reflection;
using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class HiveBeeWholePass1458Tests
{
    public static IEnumerable<object[]> Cases() => RawFlightFixture1458.Cases("HiveBeeWholePass1458");

    [Theory]
    [MemberData(nameof(Cases))]
    public void Genuine_Main_NPC_pass_preserves_Hive_birth_slot_order_checkactive_RNG_and_wire(int index, string capturedJson)
    {
        _ = index;
        using var document = JsonDocument.Parse(capturedJson);
        var row = document.RootElement;
        bool good = row.GetProperty("good").GetBoolean();
        var random = new SystemVanillaNpcRandom(row.GetProperty("seed").GetInt32());
        var replication = new RuntimeNpcReplicationRegistry();
        var queue = new TerrariaConnectionOutboundQueue(new OutboundQueueOptions(128, 65536, 1024));
        var source = GameCommandSourceId.FromConnection(8950);
        Assert.True(replication.TryRegister(source, queue));
        var connection = new ConnectionHandle(source, new(new(0), new(1)));
        var recipientSpawn = new PlayerSpawnCommitRequest(connection.Player.Slot, 20, 20, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(connection, in recipientSpawn);
        var commits = new RawFlightFixture1458.CommitRecorder { Replication = replication };
        var store = new RuntimeNpcStore(200, commits);
        store.SetVanillaSpawnRandomSource(random);
        store.SetVanillaSpawnContextSource(() => new(good ? 2f : 1f, 1, good));
        var input = RawFlightFixture1458.ReadState(row.GetProperty("birth"),
            row.GetProperty("parentScale").GetSingle(), row.GetProperty("parentDifficulty").GetSingle(),
            row.GetProperty("parentSprite").GetInt32());
        input = input with { Simulation = input.Simulation with { NoGravity = false } };
        int parentSlot = row.GetProperty("parentSlot").GetInt32();
        Assert.True(store.TrySpawnVanillaPending(in input, out var parent, parentSlot));
        Assert.Equal(parentSlot, parent.Handle.Slot);
        Assert.Equal(row.GetProperty("afterBirthNext").GetInt32(), random.SourceRandom.Clone().Next());
        var lookup = new RawFlightFixture1458.Players(row.GetProperty("layout").GetInt32(), false);
        var raw = new RuntimeNpcRawPlayerSlots1458(lookup);
        if (lookup.Exists)
            Assert.True(raw.TryAttach(lookup.Player.Player));
        var targeting = new VanillaNpcTargetingAiStepper(new Rejecting(), random: random);
        targeting.SetWorldBounds(600, 140, 200, 500);
        targeting.SetWorldConditions(true, false, good, false, false);
        targeting.SetPlayerSnapshotLookup(lookup);
        targeting.SetRawPlayerSlots(raw);
        targeting.SetCandidates(lookup.Exists ? [lookup.Candidate] : []);
        var tiles = new WorldTileStore(new(600, 500));
        Assert.True(tiles.TryAttachWorldSurface(140));
        for (int x = 0; x < 600; x++)
            tiles.Set(x, 80, new() { Flags = WorldTileFlags.Active, Type = 1 });
        var flying = new VanillaFlyingEyeWorldEnvironment(tiles);
        targeting.SetFlyingEyeEnvironment(flying);
        targeting.SetBeeOwner(store, flying);
        var facts = new VanillaSlimeContainedFacts1458(140, 200, good, false, false, false,
            false, false, false, false, false, false, false, false, false, (int)VanillaMoonPhase.Full);
        targeting.SetSlimeContainedOwner(store, () => facts, new VanillaSlimeContainedWorld1458(tiles));
        var cycle = new RuntimeNpcSpawnCycle1458();
        var executor = new RuntimeNpcAiStateExecutor(store, sourceSpawnCycle: cycle);
        var motion = new VanillaNpcWorldMotionAiStepper(targeting, tiles, 140);
        executor.Tick(motion);
        var parentAfter = commits.Events.Last(commit => commit.State.Handle == parent.Handle).State;
        RawFlightFixture1458.AssertState(row.GetProperty("after"), in parentAfter);
        Assert.Equal(row.GetProperty("after").GetProperty("timeLeft").GetInt32(), parentAfter.Simulation.TimeLeft);
        Assert.Equal(row.GetProperty("after").GetProperty("active").GetBoolean(), store.TryGet(parent.Handle, out _));
        foreach (var child in row.GetProperty("children").EnumerateArray())
        {
            byte slot = child.GetProperty("slot").GetByte();
            NpcSnapshot actual;
            if (slot < parentSlot)
            {
                Assert.True(store.TryGetActive(slot, out actual));
                Assert.True(store.HasPendingBirth(actual.Handle));
                Assert.DoesNotContain(commits.Events, commit => commit.State.Handle == actual.Handle);
            }
            else
                actual = commits.Events.Last(commit => commit.State.Handle.Slot == slot).State;
            RawFlightFixture1458.AssertState(child.GetProperty("state"), in actual);
            Assert.Equal(child.GetProperty("state").GetProperty("timeLeft").GetInt32(), actual.Simulation.TimeLeft);
            Assert.Equal(child.GetProperty("rotation").GetSingle(), actual.Simulation.Rotation);
            Assert.Equal(child.GetProperty("sprite").GetInt32(), actual.Simulation.SpriteDirection);
        }
        Assert.Equal(!row.GetProperty("noSpawnCycle").GetBoolean(), cycle.TryBeginCycle());
        Assert.Equal(row.GetProperty("next").GetInt32(), random.SourceRandom.Next());
        var expectedFrames = row.GetProperty("frames").EnumerateArray()
            .Select(frame => Convert.FromHexString(frame.GetString()!)).ToArray();
        var actualFrames = new List<byte[]>();
        var inner = (BoundedOutboundQueue)typeof(TerrariaConnectionOutboundQueue)
            .GetField("_queue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(queue)!;
        while (inner.TryRead(out OutboundFrame frame))
            actualFrames.Add(frame.Bytes.ToArray());
        Assert.Equal(expectedFrames, actualFrames);
    }

    private sealed class Rejecting : INpcAiStateStepper
    {
        public bool TryStepState(in NpcSnapshot npc, out NpcStateUpdate update) { update = default; return false; }
    }
}
