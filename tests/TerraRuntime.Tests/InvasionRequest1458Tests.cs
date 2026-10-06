using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class InvasionRequest1458Tests
{
    [Fact]
    public void Real_typed_requests_use_source_base_life_census_including_dead_players()
    {
        using var source = OpenSource();
        int compared = 0;
        foreach (var row in source.RootElement.GetProperty("rows").EnumerateArray())
        {
            if (row.GetProperty("phase").GetString() != "actual MessageBuffer.GetData61") continue;
            foreach (bool dead in new[] { false, true })
            {
                using var fixture = new Fixture();
                if (dead) fixture.State.Apply(new PlayerHealthRuntimeCommand(fixture.Connection,
                    new(fixture.Session.Slot, 0, 200)));
                fixture.Request(row.GetProperty("request").GetInt16());
                var accepted = Assert.Single(fixture.Published);
                Assert.Equal(row.GetProperty("after").GetProperty("type").GetInt32(), accepted.State.Type);
                Assert.Equal(row.GetProperty("after").GetProperty("size").GetInt32(), accepted.State.Size);
                Assert.Equal(row.GetProperty("after").GetProperty("sizeStart").GetInt32(), accepted.State.SizeStart);
                Assert.Equal(row.GetProperty("after").GetProperty("x").GetDouble(), accepted.State.X);
                Assert.Equal(row.GetProperty("after").GetProperty("maximum").GetInt32(), accepted.State.ProgressMax);
                Assert.Equal(row.GetProperty("after").GetProperty("icon").GetInt32(), accepted.State.ProgressIcon);
                // This typed command owns the start callback. WorldRuntime owns its distinct 7 -> 78 publication.
                Assert.Equal(new[] { 7, 78 }, row.GetProperty("allPacketIds").EnumerateArray().Select(value => value.GetInt32()));
                compared++;
            }
        }
        Assert.Equal(6, compared);
    }

    [Fact]
    public void Generic_requests_preserve_active_event_but_minus_seven_restarts_an_empty_one()
    {
        foreach (short request in new short[] { -1, -3, -7 })
        {
            using var fixture = new Fixture();
            fixture.SetEvent(new(1, 12, 120, 9, 50, 2, 0, 120, 4, 0, false, false, false, false));
            var randomBefore = fixture.Random.Clone();
            fixture.Request(request);
            var accepted = Assert.Single(fixture.Published);
            Assert.Equal(1, accepted.State.Type);
            Assert.Equal(12, accepted.State.Size);
            Assert.Equal(request == -7 ? 0 : 9, accepted.State.Delay);
            Assert.True(fixture.Random.HasSameState(randomBefore));
        }
        using var restart = new Fixture();
        restart.SetEvent(new(1, 0, 120, 9, 50, 2, 0, 120, 4, 0, false, false, false, false));
        var beforeRestart = restart.Random.Clone();
        restart.Request(-7);
        var restarted = Assert.Single(restart.Published);
        Assert.Equal(4, restarted.State.Type);
        Assert.Equal(200, restarted.State.Size);
        Assert.Equal(49, restarted.State.X);
        Assert.True(restart.Random.HasSameState(beforeRestart));
    }

    [Fact]
    public void Unknown_base_life_and_unassigned_requester_are_not_a_trusted_start_census()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Graph.Players.TryGet(fixture.Connection, out var player));
        player.BaseLifeMax = null; // Explicit unknown import, not a constructor assumption.
        fixture.SetEvent(default(InvasionState1458) with { Delay = 7 });
        var beforeRandom = fixture.Random.Clone();
        fixture.Request(-1);
        Assert.Empty(fixture.Published);
        Assert.True(fixture.Random.HasSameState(beforeRandom));
        fixture.State.Apply(new ClientBossSummonRuntimeCommand(new(GameCommandSourceId.FromConnection(999),
            new(new PlayerSlotId(255), new PlayerSessionGeneration(1))), -7));
        Assert.Empty(fixture.Published);
        Assert.True(fixture.Owner.TryCapture(out var current));
        Assert.Equal(0, current.State.Type);
        Assert.Equal(7, current.State.Delay);
    }

    [Fact]
    public void Derived_maximum_does_not_replace_the_source_base_maximum()
    {
        using var fixture = new Fixture();
        fixture.State.Apply(new PlayerHealthRuntimeCommand(fixture.Connection, new(fixture.Session.Slot, 199, 199)));
        Assert.True(fixture.Graph.Players.TryGet(fixture.Connection, out var player));
        player.DerivedLifeMax = 280; // Same represented base/derived distinction as the source census probe.
        var beforeRandom = fixture.Random.Clone();
        fixture.Request(-1);
        Assert.Equal(0, Assert.Single(fixture.Published).State.Type);
        Assert.True(fixture.Random.HasSameState(beforeRandom));
    }

    [Fact]
    public void Accepted_start_adopts_rng_before_publication_and_preserves_publication_reentry()
    {
        using var fixture = new Fixture();
        var expectedRandom = fixture.Random.Clone();
        expectedRandom.Next(0, 2);
        expectedRandom.Next();
        fixture.OnPublished = () =>
        {
            fixture.Random.Next();
            fixture.SetEvent(new(3, 8, 180, 0, 50, 0, 0, 180, 6, 0, false, false, false, false));
        };

        fixture.Request(-1);

        Assert.Single(fixture.Published);
        Assert.True(fixture.Random.HasSameState(expectedRandom));
        Assert.True(fixture.Owner.TryCapture(out var newer));
        Assert.Equal(3, newer.State.Type);
        Assert.Equal(8, newer.State.Size);
    }

    [Fact]
    public void Census_callback_event_or_rng_changes_are_not_overwritten_by_a_retained_start()
    {
        foreach (bool changeRandom in new[] { false, true })
        {
            using var fixture = new Fixture();
            var expectedRandom = fixture.Random.Clone();
            if (changeRandom) expectedRandom.Next();
            bool called = false;
            fixture.InterceptCensus(() =>
            {
                called = true;
                if (changeRandom) fixture.Random.Next();
                else fixture.SetEvent(new(3, 8, 180, 0, 50, 0, 0, 180, 6, 0, false, false, false, false));
            });
            fixture.Request(-1);
            Assert.True(called);
            Assert.Empty(fixture.Published);
            Assert.True(fixture.Random.HasSameState(expectedRandom));
            Assert.True(fixture.Owner.TryCapture(out var retained));
            Assert.Equal(changeRandom ? 0 : 3, retained.State.Type);
            Assert.Equal(changeRandom ? 0 : 8, retained.State.Size);
        }
    }

    private static JsonDocument OpenSource()
    {
        using var resource = typeof(InvasionRequest1458Tests).Assembly.GetManifestResourceStream("InvasionWorldWire1458")!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly RuntimeWorldInvasion1458 Owner = new(default(InvasionState1458), 100, 50);
        internal readonly VanillaUnifiedRandom1458 Random = new(1);
        internal readonly List<RuntimeInvasionCapture1458> Published = [];
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal Action? OnPublished;

        internal Fixture()
        {
            State = new(invasion: Owner, naturalSpawnRandom: new SystemVanillaNpcRandom(Random),
                invasionProgressPublisher: _ => { },
                invasionStartPublisher: capture =>
                {
                    Assert.True(Owner.IsCurrent(in capture));
                    Published.Add(capture);
                    OnPublished?.Invoke();
                });
            Graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!);
            Session.ObserveWorldRequest();
            Session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(1458), Session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(Session.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
        }

        internal void Request(short request) => State.Apply(new ClientBossSummonRuntimeCommand(Connection, request));

        internal void SetEvent(InvasionState1458 state)
        {
            Assert.True(Owner.TryCapture(out var before));
            var change = new InvasionTransition1458(state, default);
            Assert.True(Owner.TryAdopt(in before, in change, out _));
        }

        internal void InterceptCensus(Action callback)
        {
            var raw = (RuntimeNpcRawPlayerSlots1458)typeof(NpcAuthority).GetField("rawPlayerSlots",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Graph.Npcs)!;
            var field = typeof(RuntimeNpcRawPlayerSlots1458).GetField("players", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var original = (IRuntimePlayerSlotSnapshotLookup)field.GetValue(raw)!;
            field.SetValue(raw, new CensusCallback(original, callback));
        }

        public void Dispose() => Session.Dispose();
    }

    private sealed class CensusCallback(IRuntimePlayerSlotSnapshotLookup inner, Action callback) : IRuntimePlayerSlotSnapshotLookup
    {
        private bool invoked;
        public bool TryGetPlayer(PlayerSlotId slot, out PlayerStateSnapshot snapshot)
        {
            if (slot.Value == 200 && !invoked)
            {
                invoked = true;
                callback();
            }
            return inner.TryGetPlayer(slot, out snapshot);
        }
    }
}
