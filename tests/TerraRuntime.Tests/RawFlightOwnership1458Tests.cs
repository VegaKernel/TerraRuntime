using System.Text.Json;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RawFlightOwnership1458Tests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Bee_peer_same_value_revision_or_raw_lifetime_ABA_cannot_be_adopted(bool rawMutation, bool afterAcceptance)
    {
        using var json = FirstBee();
        var fixture = new RawFlightFixture1458(json.RootElement, true);
        var peerState = new NpcStateUpdate(3, 3, 1100, 1200, 0, 0, 255, default, NpcSimulationState.Initial);
        Assert.True(fixture.Store.TrySpawn(1, in peerState, out var peer));
        void Mutate()
        {
            if (rawMutation)
            {
                var handle = new PlayerHandle(new(7), new(1));
                Assert.True(fixture.Raw.TryAttach(handle));
                Assert.True(fixture.Raw.TryReset(handle));
            }
            else
            {
                var same = new NpcStateUpdate(peer.Type, peer.NetId, peer.PositionX, peer.PositionY,
                    peer.VelocityX, peer.VelocityY, peer.Target, peer.Ai, peer.Simulation);
                Assert.True(fixture.Store.TryUpdateUnpublished(peer.Handle, in same, out _));
            }
        }
        var world = new World { Callback = afterAcceptance ? null : Mutate };
        fixture.Targeting.SetBeeOwner(fixture.Store, world);
        var random = fixture.Random.SourceRandom.Clone();
        var before = fixture.Before;
        if (afterAcceptance)
        {
            Assert.True(fixture.Targeting.TryStepState(in before, out var placeholder));
            Assert.True(fixture.Store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
            Mutate();
            Assert.False(fixture.Targeting.TryGetBeePlan(in before, in accepted, out _));
            Assert.True(fixture.Store.TryGet(before.Handle, out var current));
            Assert.Equal(accepted, current);
        }
        else
        {
            Assert.False(fixture.Targeting.TryStepState(in before, out _));
            Assert.True(fixture.Store.TryGet(before.Handle, out var current));
            Assert.Equal(before, current);
        }
        Assert.True(fixture.Random.SourceRandom.HasSameState(random));
        Assert.True(fixture.Store.HasPendingBirth(before.Handle));
        Assert.DoesNotContain(fixture.Commits.Events, commit => commit.State.Handle == before.Handle);
    }

    [Fact]
    public void Unknown_peer_metadata_rejects_without_placeholder_or_publication()
    {
        using var json = FirstBee();
        var fixture = new RawFlightFixture1458(json.RootElement, true);
        var foreign = new NpcStateUpdate(999, 999, 1200, 1200, 0, 0, 255, default,
            NpcSimulationState.Initial with { Life = 20, LifeMax = 20 });
        Assert.True(fixture.Store.TrySpawn(1, in foreign, out _));
        var before = fixture.Before;
        Assert.False(fixture.Targeting.TryStepState(in before, out _));
        Assert.True(fixture.Store.TryGet(before.Handle, out var unchanged));
        Assert.Equal(before, unchanged);
        Assert.DoesNotContain(fixture.Commits.Events, commit => commit.State.Handle == before.Handle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bee_world_version_change_is_checked_before_and_after_placeholder(bool afterAcceptance)
    {
        using var json = FirstBee();
        var fixture = new RawFlightFixture1458(json.RootElement, true);
        var world = new World();
        if (!afterAcceptance)
            world.Callback = () => world.Current = false;
        fixture.Targeting.SetBeeOwner(fixture.Store, world);
        var before = fixture.Before;
        var random = fixture.Random.SourceRandom.Clone();
        if (afterAcceptance)
        {
            Assert.True(fixture.Targeting.TryStepState(in before, out var placeholder));
            Assert.True(fixture.Store.TryUpdateUnpublished(before.Handle, in placeholder, out var accepted));
            world.Current = false;
            Assert.False(fixture.Targeting.TryGetBeePlan(in before, in accepted, out _));
        }
        else
            Assert.False(fixture.Targeting.TryStepState(in before, out _));
        Assert.True(fixture.Random.SourceRandom.HasSameState(random));
        Assert.Empty(fixture.Commits.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Imported_unknown_graveyard_is_fenced_only_when_the_current_daylight_branch_reads_it(bool day)
    {
        using var json = JsonDocument.Parse((string)RawFlightFixture1458.Cases("RawEyeFirst1458")
            .First(test =>
            {
                using var row = JsonDocument.Parse((string)test[1]);
                return row.RootElement.GetProperty("type").GetInt32() == 2 &&
                    row.RootElement.GetProperty("layout").GetInt32() == 1 &&
                    row.RootElement.GetProperty("target").GetInt32() == 0;
            })[1]);
        var fixture = new RawFlightFixture1458(json.RootElement, false);
        fixture.Lookup.Player = fixture.Lookup.Player with { Zones = null };
        fixture.Targeting.SetWorldConditions(day, false);
        var before = fixture.Before;
        Assert.Equal(!day, fixture.Targeting.TryStepState(in before, out _));
        Assert.True(fixture.Store.TryGet(before.Handle, out var unchanged));
        Assert.Equal(before, unchanged);
        Assert.Empty(fixture.Commits.Events);
    }

    private static JsonDocument FirstBee() => JsonDocument.Parse((string)RawFlightFixture1458.Cases("BeeRawAi0051458").First()[1]);

    private sealed class World : IVanillaFlyingEyeRetainedEnvironment1458, IVanillaFlyingEyeWorldFence1458
    {
        internal Action? Callback;
        internal bool Current = true;
        public bool IsCurrent => Current;
        public bool IsGraveyardAt(float x, float y) => false;
        public bool SolidCollision(float x, float y, int width, int height) => false;
        public bool CanHit(float x, float y, int width, int height, float tx, float ty, int tw, int th) => true;
        public bool TryCapture(in NpcSnapshot source, in VanillaNpcTargetCandidate current,
            in VanillaNpcTargetCandidate closest, out IVanillaFlyingEyeWorldFence1458 fence)
        {
            var callback = Callback;
            Callback = null;
            callback?.Invoke();
            fence = this;
            return true;
        }
    }
}
