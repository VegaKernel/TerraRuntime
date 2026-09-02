using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeWorldItemCommandTests
{
    [Fact]
    public async Task Authoritative_commands_allocate_merge_and_remove_one_world_item_generation()
    {
        var store = new RuntimeWorldItemStore();
        var runtime = new ServerRuntimeState(worldItems: store);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = Spawn(runtime, GameCommandSourceId.FromConnection(601), session);
        WorldItemDropStateUpdate initial = CreateDrop(positionX: 100f, stack: 2);
        var completion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);

        runtime.Apply(new WorldItemAllocateRuntimeCommand(connection, initial, completion));

        Assert.True(completion.Task.IsCompletedSuccessfully);
        WorldItemSnapshot allocated = Assert.IsType<WorldItemSnapshot>(await completion.Task);
        Assert.Equal((short)0, allocated.Handle.Slot);
        Assert.Equal((ulong)1, allocated.Handle.Generation.Value);
        Assert.Equal(1, runtime.AppliedWorldItemAllocations);
        Assert.Equal(0, runtime.RejectedWorldItemAllocations);

        byte playerSlot = connection.Player.Slot.Value;
        var owner = new WorldItemOwnerStateUpdate(
            OwnerPlayerId: playerSlot,
            TimeToKeepReservation: 300,
            GrabDelayPlayer: playerSlot,
            GrabDelayTime: 45,
            PositionX: 110f,
            PositionY: 210f);
        runtime.Apply(new WorldItemOwnerRuntimeCommand(connection, allocated.Handle, owner));

        WorldItemDropStateUpdate moved = CreateDrop(positionX: 130f, stack: 3) with
        {
            VelocityX = 2.5f,
            Shimmered = true,
            ShimmerTime = 8f,
            EnemyGrabDelayTime = 12
        };
        runtime.Apply(new WorldItemDropRuntimeCommand(connection, allocated.Handle, moved));

        Assert.True(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot updated));
        Assert.Equal(allocated.Handle, updated.Handle);
        Assert.Equal((ulong)3, updated.Revision.Value);
        Assert.Equal(130f, updated.PositionX);
        Assert.Equal((short)3, updated.Stack);
        Assert.Equal(playerSlot, updated.OwnerPlayerId);
        Assert.Equal(300, updated.TimeToKeepReservation);
        Assert.Equal(playerSlot, updated.GrabDelayPlayer);
        Assert.Equal(45, updated.GrabDelayTime);
        Assert.Equal(1, runtime.AppliedWorldItemOwners);
        Assert.Equal(1, runtime.AppliedWorldItemDrops);

        runtime.Apply(new WorldItemRemoveRuntimeCommand(connection, allocated.Handle));

        Assert.False(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out _));
        Assert.Equal(1, runtime.AppliedWorldItemRemovals);
        Assert.Equal(0, runtime.RejectedWorldItemRemovals);
    }

    [Fact]
    public async Task Invalid_world_item_commands_increment_rejection_counters()
    {
        var runtime = new ServerRuntimeState(worldItems: new RuntimeWorldItemStore());
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = Spawn(runtime, GameCommandSourceId.FromConnection(602), session);
        WorldItemDropStateUpdate invalid = CreateDrop(positionX: float.NaN, stack: 1);
        var completion = new TaskCompletionSource<WorldItemSnapshot?>();

        runtime.Apply(new WorldItemAllocateRuntimeCommand(connection, invalid, completion));
        runtime.Apply(new WorldItemDropRuntimeCommand(connection, default, invalid));
        runtime.Apply(new WorldItemOwnerRuntimeCommand(
            connection,
            default,
            new WorldItemOwnerStateUpdate(1, 10, 1, 20, 0f, 0f)));
        runtime.Apply(new WorldItemRemoveRuntimeCommand(connection, default));

        Assert.True(completion.Task.IsCompletedSuccessfully);
        Assert.Null(await completion.Task);
        Assert.Equal(1, runtime.RejectedWorldItemAllocations);
        Assert.Equal(1, runtime.RejectedWorldItemDrops);
        Assert.Equal(1, runtime.RejectedWorldItemOwners);
        Assert.Equal(1, runtime.RejectedWorldItemRemovals);
        Assert.Equal(0, runtime.AppliedWorldItemAllocations);
        Assert.Equal(0, runtime.AppliedWorldItemDrops);
        Assert.Equal(0, runtime.AppliedWorldItemOwners);
        Assert.Equal(0, runtime.AppliedWorldItemRemovals);
    }

    [Fact]
    public async Task Stale_connection_generation_cannot_mutate_world_items_after_slot_reuse()
    {
        var store = new RuntimeWorldItemStore();
        var runtime = new ServerRuntimeState(worldItems: store);
        var slots = new PlayerSlotPool(1);
        GameCommandSourceId source = GameCommandSourceId.FromConnection(603);
        ConnectionHandle stale;
        WorldItemSnapshot allocated;

        using (PlayerJoinSession first = CreateAwaitingSpawnSession(slots))
        {
            stale = Spawn(runtime, source, first);
            var completion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            runtime.Apply(new WorldItemAllocateRuntimeCommand(stale, CreateDrop(positionX: 100f, stack: 2), completion));
            allocated = Assert.IsType<WorldItemSnapshot>(await completion.Task);
            runtime.Apply(new PlayerDisconnectRuntimeCommand(stale));
        }

        using PlayerJoinSession second = CreateAwaitingSpawnSession(slots);
        ConnectionHandle current = Spawn(runtime, source, second);
        Assert.Equal(stale.Player.Slot, current.Player.Slot);
        Assert.NotEqual(stale.Player.Generation, current.Player.Generation);
        Assert.True(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot before));

        var staleAllocateCompletion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new WorldItemDropRuntimeCommand(stale, allocated.Handle, CreateDrop(positionX: 999f, stack: 9)));
        runtime.Apply(new WorldItemOwnerRuntimeCommand(
            stale,
            allocated.Handle,
            new WorldItemOwnerStateUpdate(current.Player.Slot.Value, 300, current.Player.Slot.Value, 30, 999f, 999f)));
        runtime.Apply(new WorldItemRemoveRuntimeCommand(stale, allocated.Handle));
        runtime.Apply(new WorldItemAllocateRuntimeCommand(stale, CreateDrop(positionX: 777f, stack: 7), staleAllocateCompletion));

        Assert.True(staleAllocateCompletion.Task.IsCompletedSuccessfully);
        Assert.Null(await staleAllocateCompletion.Task);
        Assert.Equal(1, runtime.RejectedWorldItemAllocations);
        Assert.Equal(1, runtime.RejectedWorldItemDrops);
        Assert.Equal(1, runtime.RejectedWorldItemOwners);
        Assert.Equal(1, runtime.RejectedWorldItemRemovals);
        Assert.True(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot afterStale));
        Assert.Equal(before, afterStale);

        runtime.Apply(new WorldItemDropRuntimeCommand(
            current,
            allocated.Handle,
            CreateDrop(positionX: 130f, stack: 3)));

        Assert.Equal(1, runtime.AppliedWorldItemDrops);
        Assert.True(runtime.TryCaptureWorldItemSnapshot(allocated.Handle.Slot, out WorldItemSnapshot afterCurrent));
        Assert.Equal(130f, afterCurrent.PositionX);
        Assert.Equal((short)3, afterCurrent.Stack);
    }

    [Fact]
    public async Task Queued_explicit_world_item_command_cannot_cross_slot_generation_reuse()
    {
        var store = new RuntimeWorldItemStore();
        var runtime = new ServerRuntimeState(worldItems: store);
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        ConnectionHandle connection = Spawn(runtime, GameCommandSourceId.FromConnection(604), session);
        var firstCompletion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new WorldItemAllocateRuntimeCommand(
            connection,
            CreateDrop(positionX: 100f, stack: 2),
            firstCompletion));
        WorldItemSnapshot first = Assert.IsType<WorldItemSnapshot>(await firstCompletion.Task);

        var captured = new CapturingCommandIngress();
        var ingress = new RuntimeWorldItemIngress(captured, store);
        Assert.True(ingress.TryPostRemove(connection, first.Handle.Slot));
        WorldItemRemoveRuntimeCommand delayed = Assert.IsType<WorldItemRemoveRuntimeCommand>(captured.Command);
        Assert.Equal(first.Handle, delayed.Target);

        runtime.Apply(new WorldItemRemoveRuntimeCommand(connection, first.Handle));
        var secondCompletion = new TaskCompletionSource<WorldItemSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.Apply(new WorldItemAllocateRuntimeCommand(
            connection,
            CreateDrop(positionX: 200f, stack: 4),
            secondCompletion));
        WorldItemSnapshot second = Assert.IsType<WorldItemSnapshot>(await secondCompletion.Task);
        Assert.Equal(first.Handle.Slot, second.Handle.Slot);
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);

        runtime.Apply(delayed);

        Assert.Equal(1, runtime.RejectedWorldItemRemovals);
        Assert.True(runtime.TryCaptureWorldItemSnapshot(second.Handle.Slot, out WorldItemSnapshot current));
        Assert.Equal(second.Handle, current.Handle);
        Assert.Equal(200f, current.PositionX);
        Assert.Equal((short)4, current.Stack);
    }

    private sealed class CapturingCommandIngress : IGameCommandIngress<RuntimeCommand>
    {
        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Command = command;
            return true;
        }
    }

    private static ConnectionHandle Spawn(
        ServerRuntimeState runtime,
        GameCommandSourceId source,
        PlayerJoinSession session)
    {
        var connection = new ConnectionHandle(source, session.Handle);
        var request = new PlayerSpawnCommitRequest(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        runtime.Apply(new PlayerSpawnRuntimeCommand(connection, session, request));
        Assert.Equal(PlayerSpawnCommitResult.Committed, runtime.LastSpawnCommitResult);
        return connection;
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquireConnection(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static WorldItemDropStateUpdate CreateDrop(float positionX, short stack) =>
        new(
            PositionX: positionX,
            PositionY: 200f,
            VelocityX: 1.5f,
            VelocityY: -2f,
            Stack: stack,
            Prefix: 4,
            Ownership: WorldItemOwnershipMode.None,
            ItemNetId: 100,
            Shimmered: false,
            ShimmerTime: 0f,
            EnemyGrabDelayTime: 0);
}
