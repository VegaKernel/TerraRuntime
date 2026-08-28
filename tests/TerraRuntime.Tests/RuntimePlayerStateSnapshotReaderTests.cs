using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerStateSnapshotReaderTests
{
    [Fact]
    public async Task Completes_only_after_the_authoritative_command_is_applied()
    {
        var state = new ServerRuntimeState();
        var slots = new PlayerSlotPool(1);
        using PlayerJoinSession session = CreateAwaitingSpawnSession(slots);
        var connection = new ConnectionHandle(GameCommandSourceId.FromConnection(1), session.Handle);
        PlayerSpawnCommitRequest spawn = new(session.Slot, 100, 200, 0, 0, 0, 0, 0);
        state.Apply(new PlayerSpawnRuntimeCommand(connection, session, spawn));
        var ingress = new CapturingIngress(accept: true);
        var reader = new RuntimePlayerStateSnapshotReader(ingress);

        Task<PlayerStateSnapshot?> pending = reader.CaptureAsync(
            connection.Player,
            TestContext.Current.CancellationToken).AsTask();

        Assert.False(pending.IsCompleted);
        Assert.Equal(GameCommandSourceId.System, ingress.Source);
        state.Apply(Assert.IsType<PlayerStateSnapshotRuntimeCommand>(ingress.Command));
        PlayerStateSnapshot snapshot = Assert.IsType<PlayerStateSnapshot>(await pending);
        Assert.Equal(connection.Player, snapshot.Player);
        Assert.Equal(new PlayerStateRevision(1), snapshot.Revision);
    }

    [Fact]
    public async Task Returns_null_for_a_stale_generation()
    {
        var state = new ServerRuntimeState();
        var ingress = new CapturingIngress(accept: true);
        var reader = new RuntimePlayerStateSnapshotReader(ingress);
        var stale = new PlayerHandle(new(0), new(1));

        Task<PlayerStateSnapshot?> pending = reader.CaptureAsync(
            stale,
            TestContext.Current.CancellationToken).AsTask();
        state.Apply(Assert.IsType<PlayerStateSnapshotRuntimeCommand>(ingress.Command));

        Assert.Null(await pending);
    }

    [Fact]
    public async Task Reports_authoritative_queue_backpressure()
    {
        var reader = new RuntimePlayerStateSnapshotReader(new CapturingIngress(accept: false));
        var player = new PlayerHandle(new(0), new(1));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.CaptureAsync(player, TestContext.Current.CancellationToken).AsTask());
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        session.ObserveWorldRequest();
        session.ObserveSectionRequest();
        return session;
    }

    private sealed class CapturingIngress(bool accept) : IGameCommandIngress<RuntimeCommand>
    {
        public GameCommandSourceId Source { get; private set; }

        public RuntimeCommand? Command { get; private set; }

        public bool TryPost(GameCommandSourceId source, RuntimeCommand command)
        {
            Source = source;
            Command = command;
            return accept;
        }
    }
}
