using TerraRuntime.Application;

namespace TerraRuntime.Tests;

public sealed class GemBunnyStatus1458Tests
{
    [Fact]
    public void Source_stinky_expiry_repeated_tile_refresh_and_post_death_visual_draws_share_the_owned_phase()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyStinkyLinux1458");
        int updates = 0;
        foreach (var group in json.RootElement.EnumerateArray().GroupBy(row => (
                     Type: row.GetProperty("type").GetInt32(), Scene: row.GetProperty("scene").GetString()!,
                     Seed: row.GetProperty("seed").GetInt32())))
        {
            var replication = new RuntimeNpcReplicationRegistry();
            var f = new GemBunnyLifecycle1458Tests.Fixture(group.Key.Type, group.Key.Scene, group.Key.Seed,
                group.First().GetProperty("before"), replication);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9030, 0);
            GemBunnyWire1458Tests.Drain(outbound);
            foreach (var row in group)
            {
                f.Tick();
                bool alive = row.GetProperty("after").GetProperty("active").GetBoolean();
                Assert.Equal(alive, f.Npcs.TryGet(f.Birth.Handle, out var current));
                if (alive)
                {
                    GemBunnyLifecycle1458Tests.Same(row.GetProperty("after"), current);
                    Assert.True(f.status.TryGetWireDuration(current.Handle, out int duration));
                    int expectedDuration = row.GetProperty("buffTime")[0].GetInt32();
                    Assert.Equal(expectedDuration == 0 ? -1 : expectedDuration, duration);
                    Assert.True(f.status.TryGetStinky(current.Handle, out bool stinky));
                    Assert.Equal(row.GetProperty("stinky").GetBoolean(), stinky);
                }
                Assert.Equal(row.GetProperty("after").GetProperty("next").GetInt32(), f.Random.Clone().Next());
                var actual = GemBunnyWire1458Tests.Drain(outbound).Select(Convert.ToHexString).ToArray();
                var expected = row.GetProperty("frames").EnumerateArray().Select(frame => frame.GetString()!).ToArray();
                Assert.True(expected.SequenceEqual(actual), $"{group.Key} step{row.GetProperty("step")}: " +
                    $"expected {string.Join(';', expected)}, actual {string.Join(';', actual)}");
                updates++;
            }
        }
        Assert.Equal(1302, updates);
    }
}
