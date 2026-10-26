using System.Reflection;
using TerraRuntime.Application;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Core.Npcs;
using TerraRuntime.Core.Players;
using TerraRuntime.Gameplay.Worlds;
using TerraRuntime.World;

namespace TerraRuntime.Tests;

public sealed class PaidEventSummon1458Tests
{
    private static readonly (short Action, int Item)[] Items =
        [(-1, 361), (-2, 602), (-3, 1315), (-4, 1844), (-5, 1958), (-6, 2767), (-10, 4271)];

    [Fact]
    public void Packet_reports_never_fund_event_items_or_martian_probe_starts()
    {
        foreach (var (action, item) in Items)
        {
            using var f = new Fixture(day: action == -6);
            f.Grant(item, stack: 2, funded: 0);
            var random = f.Random.Clone();
            f.Request(action);
            f.Request(action);
            f.AssertIdle();
            Assert.Equal(2, f.Item.Stack);
            Assert.Equal(0, f.Funded);
            Assert.Empty(f.Notifications);
            Assert.True(f.Random.HasSameState(random));
        }
        using var martian = new Fixture();
        martian.Grant(361, 2, 2);
        martian.Request(-7);
        martian.AssertIdle();
        Assert.Equal(2, martian.Funded);
    }

    [Fact]
    public void Every_admitted_event_spends_one_owned_item_before_observers_and_replays_do_not_spend_twice()
    {
        foreach (var (action, item) in Items)
        {
            using var f = new Fixture(day: action == -6);
            f.Grant(item, stack: 3, funded: 2);
            if (action == -2 && !RuntimeInvasionSpawn1458.IsFrostCohortReady())
            {
                var before = f.Random.Clone();
                f.Request(action);
                f.AssertIdle();
                Assert.Equal(3, f.Item.Stack);
                Assert.Equal(2, f.Funded);
                Assert.Empty(f.Notifications);
                Assert.True(f.Random.HasSameState(before));
                continue;
            }
            f.OnNotification = () =>
            {
                Assert.Equal(2, f.Item.Stack);
                Assert.Equal(1, f.Funded);
                f.AssertStarted(action);
                f.Request(action); // Callback reentry sees the committed event and debit.
            };
            f.Request(action);
            f.Request(action);
            f.AssertStarted(action);
            Assert.Equal(2, f.Item.Stack);
            Assert.Equal(1, f.Funded);
            Assert.Equal(action is -1 or -2 or -3 ? 2 : 1, f.Notifications.Count);
        }
    }

    [Fact]
    public void Invalid_time_overlapping_events_wrong_item_and_dead_requesters_keep_budget_and_rng()
    {
        foreach (var (action, item) in Items)
        foreach (int invalid in new[] { 0, 1, 2 })
        {
            using var f = new Fixture(day: action is -4 or -5 or -10);
            f.Grant(invalid == 0 ? 1 : item, 2, 2);
            if (invalid == 1)
                f.State.Apply(new PlayerHealthRuntimeCommand(f.Connection, new(f.Session.Slot, 0, 200)));
            if (invalid == 2 && action is -1 or -2 or -3) f.SetInvasion(new(3, 10, 180, 0, 50, 0,
                0, 180, 6, 0, false, false, false, false));
            var before = f.Random.Clone();
            f.Request(action);
            Assert.Equal(2, f.Item.Stack);
            Assert.Equal(2, f.Funded);
            Assert.Empty(f.Notifications);
            Assert.True(f.Random.HasSameState(before));
        }
    }

    [Fact]
    public void Superseded_player_inventory_or_curse_invalidates_the_prepared_payment_without_a_debit()
    {
        foreach (int change in new[] { 0, 1, 2 })
        {
            using var f = new Fixture();
            f.Grant(361, 2, 2);
            Assert.True(f.Graph.Players.TryPrepareEventSummon(f.Connection, 361, out var payment));
            if (change == 0) f.Grant(361, 3, 2);
            else if (change == 1) f.State.Apply(new PlayerBuffTypesRuntimeCommand(f.Connection,
                new(f.Session.Slot, new BuffTypeId[] { new(23) })));
            else f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
            Assert.False(payment!.IsCurrent);
            Assert.False(payment.TryAdoptUnpublished());
            payment.Publish();
            Assert.Empty(f.Notifications);
        }
    }

    [Fact]
    public void Accepted_payment_survives_callback_disconnect_without_rebinding_or_duplicate_publication()
    {
        using var f = new Fixture();
        f.Grant(361, 2, 1);
        f.OnNotification = () => f.State.Apply(new PlayerDisconnectRuntimeCommand(f.Connection));
        f.Request(-1);
        Assert.True(f.Owner.TryCapture(out var accepted));
        Assert.Equal(1, accepted.State.Type);
        Assert.Single(f.Notifications); // Inventory observer for retired occupation is suppressed.
        f.Request(-1);
        Assert.Single(f.Notifications);
    }

    [Fact]
    public void Throwing_start_observer_cannot_leave_an_unpaid_event_or_repeat_its_debit()
    {
        using var f = new Fixture();
        f.Grant(361, 2, 2);
        bool first = true;
        f.OnNotification = () =>
        {
            if (!first) return;
            first = false;
            throw new InvalidOperationException("owned observer failure");
        };
        Assert.Throws<InvalidOperationException>(() => f.Request(-1));
        f.AssertStarted(-1);
        Assert.Equal(1, f.Item.Stack);
        Assert.Equal(1, f.Funded);
        Assert.Equal(new[] { "start", "inventory" }, f.Notifications);
        f.Request(-1);
        Assert.Equal(1, f.Funded);
        Assert.Equal(2, f.Notifications.Count);
    }

    [Fact]
    public void Moon_eclipse_and_blood_starts_keep_debit_when_inventory_observer_throws_and_replay_is_inert()
    {
        foreach (var (action, item) in Items.Where(row => row.Action is -4 or -5 or -6 or -10))
        {
            using var fixture = new Fixture(day: action == -6);
            fixture.Grant(item, 2, 1);
            fixture.OnNotification = () =>
            {
                fixture.AssertStarted(action);
                Assert.Equal(1, fixture.Item.Stack);
                Assert.Equal(0, fixture.Funded);
                throw new InvalidOperationException("accepted inventory observer");
            };
            Assert.Throws<InvalidOperationException>(() => fixture.Request(action));
            fixture.OnNotification = null;
            fixture.Request(action);
            fixture.AssertStarted(action);
            Assert.Equal(1, fixture.Item.Stack);
            Assert.Equal(0, fixture.Funded);
            Assert.Equal(new[] { "inventory" }, fixture.Notifications);
        }
    }

    private sealed class Fixture : IDisposable, IRuntimePlayerEventSink
    {
        internal readonly RuntimeWorldInvasion1458 Owner = new(default(InvasionState1458), 100, 50);
        internal readonly VanillaUnifiedRandom1458 Random = new(1458);
        internal readonly RuntimeWorldClock Clock;
        internal readonly ServerRuntimeState State;
        internal readonly ServerRuntimeComposition Graph;
        internal readonly PlayerJoinSession Session;
        internal readonly ConnectionHandle Connection;
        internal readonly List<string> Notifications = [];
        internal Action? OnNotification;
        private readonly RuntimePlayerInventoryStore inventory;

        internal Fixture(bool day = false)
        {
            Clock = new(100, day, default, 0, 0);
            var progression = new RuntimeWorldProgressionMutations();
            progression.MarkCompleted(VanillaWorldProgressionId.Hardmode);
            State = new(playerEvents: this, worldClock: Clock, invasion: Owner,
                worldProgression: progression,
                naturalSpawnRandom: new SystemVanillaNpcRandom(Random), invasionProgressPublisher: _ => { },
                invasionStartPublisher: _ => Notify("start"));
            Graph = (ServerRuntimeComposition)typeof(ServerRuntimeState).GetField("_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(State)!;
            inventory = (RuntimePlayerInventoryStore)typeof(PlayerAuthority).GetField("inventory",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Graph.Players)!;
            var pool = new PlayerSlotPool(1);
            Assert.True(pool.TryAcquireConnection(out var lease));
            Session = new(lease!);
            Session.ObserveWorldRequest(); Session.ObserveSectionRequest();
            Connection = new(GameCommandSourceId.FromConnection(1458), Session.Handle);
            State.Apply(new PlayerHealthRuntimeCommand(Connection, new(Session.Slot, 200, 200)));
            State.Apply(new PlayerSpawnRuntimeCommand(Connection, Session, new(Session.Slot, 40, 30, 0, 0, 0, 0, 0)));
        }

        internal RuntimePlayerInventoryItem Item { get { Assert.True(inventory.TryGet(Connection, 0, out var item)); return item; } }
        internal short Funded { get { Assert.True(inventory.TryGetFundedQuantity(Connection, 0, out var value)); return value; } }
        internal void Grant(int item, short stack, short funded)
        {
            Assert.True(inventory.TrySet(Connection, new(Session.Slot, 0, stack, 0, checked((short)item), 0)));
            var budget = inventory.CaptureFundedQuantities(Connection); budget[0] = funded;
            inventory.AdoptFundedQuantities(Connection, budget); // Owned fixture grant; never a network report.
        }
        internal void Request(short action) => State.Apply(new ClientBossSummonRuntimeCommand(Connection, action));
        internal void SetInvasion(InvasionState1458 state)
        {
            Assert.True(Owner.TryCapture(out var before));
            Assert.True(Owner.TryAdopt(in before, new(state, default), out _));
        }
        internal void AssertIdle()
        {
            Assert.True(Owner.TryCapture(out var capture)); Assert.Equal(0, capture.State.Type);
            Assert.False(Clock.MoonEventActive); Assert.False(Clock.BloodMoonActive); Assert.False(Clock.EclipseActive);
        }
        internal void AssertStarted(short action)
        {
            Assert.True(Owner.TryCapture(out var capture));
            if (action is -1 or -2 or -3) Assert.Equal(-action, capture.State.Type);
            else if (action == -4) Assert.True(Clock.PumpkinMoonActive);
            else if (action == -5) Assert.True(Clock.SnowMoonActive);
            else if (action == -6) Assert.True(Clock.EclipseActive);
            else Assert.True(Clock.BloodMoonActive);
        }
        private void Notify(string value) { Notifications.Add(value); OnNotification?.Invoke(); }
        public void PlayerEquipmentUpdated(ConnectionHandle connection, in PlayerEquipmentCommitRequest request) => Notify("inventory");
        public void PlayerAppearanceUpdated(ConnectionHandle connection, in PlayerAppearanceCommitRequest request) { }
        public void PlayerSpawned(ConnectionHandle connection, in PlayerSpawnCommitRequest request) { }
        public void PlayerMoved(ConnectionHandle connection, in PlayerMovementCommitRequest request) { }
        public void PlayerDisconnected(ConnectionHandle connection) { }
        public void Dispose() => Session.Dispose();
    }
}
