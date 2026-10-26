using TerraRuntime.Application;

namespace TerraRuntime.Tests;

public sealed class GemBunnyTerrain1458Tests
{
    [Fact]
    public void Source_explicit_home_initialization_platform_step_dead_player_and_distance_removal_finish_real_updates()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyTerrainLinux1458");
        int updates = 0, removals = 0;
        foreach (var group in json.RootElement.EnumerateArray().GroupBy(row => (
                     Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!)))
        {
            var replication = new RuntimeNpcReplicationRegistry();
            var f = new GemBunnyLifecycle1458Tests.Fixture(group.Key.Type, group.Key.Scene, 1458,
                group.First().GetProperty("before"), replication);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9050, 0);
            GemBunnyWire1458Tests.Drain(outbound);
            foreach (var row in group)
            {
                f.Tick();
                var expected = row.GetProperty("after");
                bool alive = expected.GetProperty("active").GetBoolean();
                Assert.Equal(alive, f.Npcs.TryGet(f.Birth.Handle, out var current));
                if (alive) GemBunnyLifecycle1458Tests.Same(expected, current);
                else
                {
                    removals++;
                    Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9051, 1)));
                    Assert.Equal(0, f.Items.ActiveCount);
                }
                Assert.Equal(expected.GetProperty("next").GetInt32(), f.Random.Clone().Next());
                var expectedFrames = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
                var actual = GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray();
                Assert.True(expectedFrames.SequenceEqual(actual), $"{group.Key} step{row.GetProperty("step")}: " +
                    $"expected {string.Join(';', expectedFrames)}, actual {string.Join(';', actual)}");
                updates++;
            }
        }
        Assert.Equal(854, updates);
        Assert.Equal(14, removals);
    }
}
