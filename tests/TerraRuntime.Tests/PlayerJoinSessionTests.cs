using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class PlayerJoinSessionTests
{
    [Fact]
    public void Follows_vanilla_1_2_3_10_bootstrap_states()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));

        Assert.Equal(PlayerJoinState.AwaitingWorldRequest, session.State);
        Assert.Equal((byte)0, session.Slot.Value);

        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinState.AwaitingSectionRequest, session.State);

        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        Assert.Equal(PlayerJoinState.AwaitingSpawn, session.State);

        Assert.Equal(PlayerSpawnCommitResult.Committed, session.TryCommitSpawn(new PlayerSlotId(0)));
        Assert.Equal(PlayerJoinState.Playing, session.State);
    }

    [Fact]
    public void Rejects_spawn_for_different_leased_slot_without_advancing()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());

        Assert.Equal(PlayerSpawnCommitResult.SlotMismatch, session.TryCommitSpawn(new PlayerSlotId(1)));
        Assert.Equal(PlayerJoinState.AwaitingSpawn, session.State);
        Assert.Equal(PlayerSpawnCommitResult.Committed, session.TryCommitSpawn(new PlayerSlotId(0)));
        Assert.Equal(PlayerJoinState.Playing, session.State);
        Assert.Equal(PlayerSpawnCommitResult.InvalidJoinState, session.TryCommitSpawn(new PlayerSlotId(0)));
    }

    [Fact]
    public void Repeated_or_early_bootstrap_messages_do_not_skip_vanilla_states()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        using var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));

        Assert.Equal(PlayerJoinTransition.None, session.ObserveSpawn());
        Assert.Equal(PlayerJoinTransition.None, session.ObserveSectionRequest());
        Assert.Equal(PlayerJoinState.AwaitingWorldRequest, session.State);

        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.None, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinState.AwaitingSectionRequest, session.State);

        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        Assert.Equal(PlayerJoinTransition.None, session.ObserveSectionRequest());
        Assert.Equal(PlayerJoinState.AwaitingSpawn, session.State);

        Assert.Equal(PlayerJoinTransition.EnteredPlayingState, session.ObserveSpawn());
        Assert.Equal(PlayerJoinTransition.None, session.ObserveSpawn());
        Assert.Equal(PlayerJoinState.Playing, session.State);
    }

    [Fact]
    public void Closing_session_releases_slot_exactly_once()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));

        Assert.Equal(1, pool.LeasedCount);
        Assert.Equal(PlayerJoinTransition.Closed, session.Close());
        Assert.Equal(PlayerJoinState.Closed, session.State);
        Assert.Equal(0, pool.LeasedCount);
        Assert.Equal(PlayerJoinTransition.None, session.Close());
        Assert.Equal(0, pool.LeasedCount);
        Assert.Throws<ObjectDisposedException>(() => _ = session.Slot);
    }

    [Fact]
    public void Refuses_released_slot_lease()
    {
        var pool = new PlayerSlotPool(1);
        Assert.True(pool.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        PlayerSlotPool.PlayerSlotLease acquired = Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease);
        acquired.Dispose();

        Assert.Throws<ArgumentException>(() => new PlayerJoinSession(acquired));
    }
}
