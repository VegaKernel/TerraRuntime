using TerraRuntime.Application;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class GemBunnyOwnership1458Tests
{
    [Fact]
    public void Unsupported_conveyor_and_first_shimmer_overlap_reject_before_rng_status_or_visible_motion()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyRuntimeLinux1458");
        var initial = json.RootElement.EnumerateArray().First(row => row.GetProperty("outer").GetBoolean() &&
            row.GetProperty("scene").GetString() == "walk").GetProperty("before");
        foreach (bool shimmer in new[] { false, true })
        {
            var f = new GemBunnyLifecycle1458Tests.Fixture(646, "walk", 1458, initial);
            Assert.True(f.status.TryApply(f.Birth.Handle, 2));
            int checkpoint = f.Random.Clone().Next();
            if (shimmer)
                f.tiles.Set(40, 99, new WorldTile { LiquidAmount = 255, LiquidKind = WorldLiquidKind.Shimmer });
            else
                f.tiles.Set(40, 100, new WorldTile { Type = 421, Flags = WorldTileFlags.Active });
            f.Tick();
            Assert.True(f.Npcs.TryGet(f.Birth.Handle, out var current));
            Assert.Equal(f.Birth.PositionX, current.PositionX);
            Assert.Equal(f.Birth.PositionY, current.PositionY);
            Assert.Equal(f.Birth.Ai, current.Ai);
            Assert.Equal(checkpoint, f.Random.Clone().Next());
            Assert.True(f.status.TryGetWireDuration(current.Handle, out int duration));
            Assert.Equal(2, duration);
            Assert.Empty(f.Commits);
        }
    }

    [Fact]
    public void Reentrant_planning_and_completion_preserve_newer_revisions_generations_rng_and_publication()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyRuntimeLinux1458");
        var initial = json.RootElement.EnumerateArray().First(row => row.GetProperty("outer").GetBoolean() &&
            row.GetProperty("scene").GetString() == "expiry").GetProperty("before");
        foreach (bool duringPlan in new[] { true, false })
        foreach (bool reuse in new[] { true, false })
        {
            var replication = new RuntimeNpcReplicationRegistry();
            var f = new GemBunnyLifecycle1458Tests.Fixture(646, "expiry", 1458, initial, replication);
            var outbound = GemBunnyWire1458Tests.Endpoint(replication, 9020, 0);
            GemBunnyWire1458Tests.Drain(outbound);
            int expectedRandom = f.Random.Clone().Next();
            NpcSnapshot replacement = default;
            void Reenter()
            {
                f.DuringPlan = f.BeforeCompletion = null;
                Assert.True(f.Npcs.TryGet(f.Birth.Handle, out var current));
                var update = new NpcStateUpdate(646, 646, 701, 1580, 0, 0, 0, current.Ai, current.Simulation);
                if (reuse)
                {
                    Assert.True(f.Npcs.TryDespawn(current.Handle));
                    Assert.True(f.Npcs.TrySpawn(current.Handle.Slot, in update, out replacement));
                }
                else Assert.True(f.Npcs.TryUpdate(current.Handle, in update, out replacement));
                GemBunnyWire1458Tests.Drain(outbound);
            }
            if (duringPlan) f.DuringPlan = Reenter; else f.BeforeCompletion = Reenter;
            f.Tick();
            Assert.True(f.Npcs.TryGet(replacement.Handle, out var retained));
            Assert.Equal(replacement, retained);
            Assert.Equal(expectedRandom, f.Random.Clone().Next());
            Assert.Empty(GemBunnyWire1458Tests.Drain(outbound));
            if (reuse) Assert.NotEqual(f.Birth.Handle, replacement.Handle);
        }
    }

    [Fact]
    public void Changed_collision_section_rejects_the_whole_phase_without_adopting_random_or_status()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyRuntimeLinux1458");
        var initial = json.RootElement.EnumerateArray().First(row => row.GetProperty("outer").GetBoolean() &&
            row.GetProperty("scene").GetString() == "expiry").GetProperty("before");
        var f = new GemBunnyLifecycle1458Tests.Fixture(646, "expiry", 1458, initial);
        int expectedRandom = f.Random.Clone().Next();
        Assert.True(f.status.TryApply(f.Birth.Handle, 2));
        f.BeforeCompletion = () => f.tiles.Set(40, 100, new WorldTile { Type = 2, Flags = WorldTileFlags.Active });
        f.Tick();
        Assert.True(f.Npcs.TryGet(f.Birth.Handle, out var current));
        Assert.Equal(f.Birth.PositionX, current.PositionX);
        Assert.Equal(f.Birth.Ai, current.Ai);
        Assert.Equal(expectedRandom, f.Random.Clone().Next());
        Assert.True(f.status.TryGetWireDuration(current.Handle, out int duration));
        Assert.Equal(2, duration);
        Assert.Empty(f.Commits);
    }

    [Fact]
    public void Publication_observer_sees_adopted_rng_and_cannot_resurrect_the_retired_home()
    {
        using var json = GemBunnyLifecycle1458Tests.Read("GemBunnyRuntimeLinux1458");
        var row = json.RootElement.EnumerateArray().First(value => value.GetProperty("outer").GetBoolean() &&
            value.GetProperty("scene").GetString() == "expiry");
        var replication = new RuntimeNpcReplicationRegistry();
        var f = new GemBunnyLifecycle1458Tests.Fixture(646, "expiry", 1458, row.GetProperty("before"), replication);
        bool observed = false;
        f.OnCommit = npc =>
        {
            f.OnCommit = null;
            observed = true;
            Assert.Equal(row.GetProperty("after").GetProperty("next").GetInt32(), f.Random.Clone().Next());
            Assert.True(f.Npcs.TryDespawn(npc.Handle));
            Assert.Empty(GemBunnyWire1458Tests.Drain(GemBunnyWire1458Tests.Endpoint(replication, 9021, 0)));
        };
        f.Tick();
        Assert.True(observed);
        Assert.False(f.Npcs.TryGet(f.Birth.Handle, out _));
    }
}
