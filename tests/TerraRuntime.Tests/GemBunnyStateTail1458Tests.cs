using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;

namespace TerraRuntime.Tests;

public sealed class GemBunnyStateTail1458Tests
{
    [Fact]
    public void Source_temporary_friendly_immunity_expires_at_update91_and_statue_or_release_clears_it_immediately() =>
        Replay("GemBunnyImmunityLinux1458", 1974);

    [Fact]
    public void Source_water_lava_and_lethal_contact_remove_stinky_after_collision_and_loot() =>
        Replay("GemBunnyWetStatusLinux1458", 546);

    [Fact]
    public void Source_danger_and_drowning_speed_observe_pre_AI_Friendly_across_grace_expiry() =>
        Replay("GemBunnyGraceMovementLinux1458", 168);

    private static void Replay(string resource, int expectedUpdates)
    {
        using var json = GemBunnyLifecycle1458Tests.Read(resource);
        int updates = 0;
        foreach (var group in json.RootElement.EnumerateArray().GroupBy(row => (
                     Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!,
                     Seed: row.TryGetProperty("seed", out var seed) ? seed.GetInt32() : 1458)))
        {
            var replication = new RuntimeNpcReplicationRegistry();
            var f = new GemBunnyLifecycle1458Tests.Fixture(group.Key.Type, group.Key.Scene, group.Key.Seed,
                group.First().GetProperty("before"), replication);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9070, 0);
            var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(9070), new(new(0), new(1)));
            replication.PlayerMoved(connection, new(new(0), 0, 0, 0, 0, 0, 660f, 1558f, false, 0, 0, false, 0,
                false, 0, 0, 0, 0, false, 0, 0));
            GemBunnyWire1458Tests.Drain(outbound);
            foreach (var row in group)
            {
                f.Tick();
                var expected = row.GetProperty("after");
                bool alive = expected.GetProperty("active").GetBoolean();
                Assert.Equal(alive, f.Npcs.TryGet(f.Birth.Handle, out var current));
                if (alive)
                {
                    GemBunnyLifecycle1458Tests.Same(expected, current);
                    if (row.TryGetProperty("buffTime", out var times))
                    {
                        Assert.True(f.status.TryGetWireDuration(current.Handle, out int duration));
                        int source = times[0].GetInt32();
                        Assert.Equal(source == 0 ? -1 : source, duration);
                    }
                }
                Assert.Equal(expected.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                var expectedFrames = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
                var actual = GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray();
                Assert.True(expectedFrames.SequenceEqual(actual), $"{group.Key} step{row.GetProperty("step")}: " +
                    $"expected {string.Join(';', expectedFrames)}, actual {string.Join(';', actual)}");
                if (alive && row.GetProperty("step").GetInt32() == 0 && row.TryGetProperty("buffTime", out _))
                {
                    var finalBuffFrame = expectedFrames.Last(frame => Convert.FromHexString(frame)[2] == 54);
                    var replay = GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9071, 1));
                    Assert.Equal(Convert.FromHexString(finalBuffFrame), Assert.Single(replay, frame => frame[2] == 54));
                }
                updates++;
            }
        }
        Assert.Equal(expectedUpdates, updates);
    }

    [Fact]
    public void Counter_and_release_ownership_survive_omission_reset_on_identity_change_and_reject_unknown_imports()
    {
        var npcs = new RuntimeNpcStore();
        Assert.True(npcs.TrySpawn(0, new(646, 646, 639f, 1580f, 0, 0, 0, default,
            NpcSimulationState.Initial), out var birth));
        Assert.Equal(90, birth.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)255, birth.Simulation.ReleaseOwner);
        var changed = new NpcStateUpdate(646, 646, 639f, 1580f, 0, 0, 0, default,
            birth.Simulation with { CatchableNpcTempImmunityCounter = 23, ReleaseOwner = 7 });
        Assert.True(npcs.TryUpdate(birth.Handle, in changed, out _));
        var omission = changed with { Simulation = NpcSimulationState.Initial };
        Assert.True(npcs.TryUpdate(birth.Handle, in omission, out var preserved));
        Assert.Equal(23, preserved.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)7, preserved.Simulation.ReleaseOwner);
        var otherGem = omission with { Type = 647, NetId = 647 };
        Assert.True(npcs.TryUpdate(birth.Handle, in otherGem, out var reset));
        Assert.Equal(90, reset.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Equal((short)255, reset.Simulation.ReleaseOwner);
        var otherType = omission with { Type = 3, NetId = 3 };
        Assert.True(npcs.TryUpdate(birth.Handle, in otherType, out var unrelated));
        Assert.Null(unrelated.Simulation.CatchableNpcTempImmunityCounter);
        Assert.Null(unrelated.Simulation.ReleaseOwner);

        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyRuntimeLinux1458");
        var initial = json.RootElement.EnumerateArray().First(row => row.GetProperty("outer").GetBoolean() &&
            row.GetProperty("scene").GetString() == "walk").GetProperty("before");
        foreach (bool invalidCounter in new[] { true, false })
        {
            var f = new GemBunnyLifecycle1458Tests.Fixture(646, "walk", 1458, initial);
            var imported = new NpcStateUpdate(646, 646, 639f, 1580f, .5f, 0, 0, f.Birth.Ai,
                f.Birth.Simulation with { CatchableNpcTempImmunityCounter = invalidCounter ? 91 : 90,
                    ReleaseOwner = invalidCounter ? (short)255 : (short)256 });
            Assert.True(f.Npcs.TryUpdate(f.Birth.Handle, in imported, out var source));
            f.Commits.Clear();
            int checkpoint = f.Random.Clone().Next();
            f.Tick();
            Assert.True(f.Npcs.TryGet(source.Handle, out var retained));
            Assert.Equal(source, retained);
            Assert.Equal(checkpoint, f.Random.Clone().Next());
            Assert.Empty(f.Commits);
        }
    }
}
