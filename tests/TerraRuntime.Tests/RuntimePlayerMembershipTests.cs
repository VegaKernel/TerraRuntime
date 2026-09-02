using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerMembershipTests
{
    [Fact]
    public void Stale_connection_cannot_observe_or_remove_replacement_member()
    {
        var membership = new RuntimePlayerMembership(capacity: 1);
        ConnectionHandle first = Connection(connectionId: 1, generation: 1);
        ConnectionHandle replacement = Connection(connectionId: 2, generation: 2);

        membership.Commit(Member(first));
        Assert.True(membership.TryRemove(first, out _));
        membership.Commit(Member(replacement));

        Assert.False(membership.IsCurrent(first));
        Assert.False(membership.TryRemove(first, out _));
        Assert.True(membership.IsCurrent(replacement));
        Assert.True(membership.TryCapture(replacement.Player, out PlayerStateSnapshot snapshot));
        Assert.Equal(replacement.Player, snapshot.Player);
        Assert.Equal(new PlayerStateRevision(1), snapshot.Revision);
    }

    [Fact]
    public void Pending_vitals_are_scoped_to_connection_generation()
    {
        var membership = new RuntimePlayerMembership(capacity: 1);
        ConnectionHandle stale = Connection(connectionId: 1, generation: 1);
        ConnectionHandle current = Connection(connectionId: 2, generation: 2);
        RuntimePendingPlayerVitals staleVitals = membership.GetOrReplacePending(stale);
        staleVitals.HasHealth = true;
        staleVitals.Life = 7;

        RuntimePendingPlayerVitals currentVitals = membership.GetOrReplacePending(current);
        RuntimePendingPlayerVitals captured = Assert.IsType<RuntimePendingPlayerVitals>(
            membership.TakePending(current.Player.Slot));

        Assert.NotSame(staleVitals, currentVitals);
        Assert.Same(currentVitals, captured);
        Assert.False(captured.HasHealth);
        Assert.Null(membership.TakePending(current.Player.Slot));
    }

    [Fact]
    public void Conversation_state_is_reset_across_membership_lifetimes()
    {
        var membership = new RuntimePlayerMembership(capacity: 1);
        ConnectionHandle first = Connection(connectionId: 1, generation: 1);
        ConnectionHandle replacement = Connection(connectionId: 2, generation: 2);
        membership.Commit(Member(first));
        Assert.True(membership.TrySetTalkNpc(first, npcSlot: 12));
        Assert.True(membership.TryGetTalkNpc(first.Player, out short firstNpc));
        Assert.Equal(12, firstNpc);

        Assert.True(membership.TryRemove(first, out _));
        membership.Commit(Member(replacement));

        Assert.False(membership.TryGetTalkNpc(first.Player, out _));
        Assert.True(membership.TryGetTalkNpc(replacement.Player, out short replacementNpc));
        Assert.Equal(TerrariaNpcTalkCodec.NoNpc, replacementNpc);
    }

    [Fact]
    public void Commit_rejects_a_second_active_member_for_the_same_slot()
    {
        var membership = new RuntimePlayerMembership(capacity: 1);
        ConnectionHandle first = Connection(connectionId: 1, generation: 1);
        ConnectionHandle replacement = Connection(connectionId: 2, generation: 2);
        membership.Commit(Member(first));

        Assert.Throws<InvalidOperationException>(() => membership.Commit(Member(replacement)));
        Assert.True(membership.IsCurrent(first));
        Assert.False(membership.IsCurrent(replacement));
    }

    private static RuntimePlayerMember Member(ConnectionHandle connection) =>
        new()
        {
            Connection = connection,
            Slot = connection.Player.Slot,
            Revision = 1
        };

    private static ConnectionHandle Connection(long connectionId, ulong generation) =>
        new(
            GameCommandSourceId.FromConnection(connectionId),
            new PlayerHandle(new PlayerSlotId(0), new PlayerSessionGeneration(generation)));
}
