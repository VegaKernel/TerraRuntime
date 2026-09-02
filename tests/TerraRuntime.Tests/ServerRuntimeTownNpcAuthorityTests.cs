using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class ServerRuntimeTownNpcAuthorityTests
{
    [Fact]
    public void Npc_talk_state_is_owned_by_current_player_generation_after_town_authority_extraction()
    {
        var slots = new PlayerSlotPool(1);
        var state = new ServerRuntimeState();

        using PlayerJoinSession firstSession = CreateAwaitingSpawnSession(slots);
        ConnectionHandle first = Connection(1201, firstSession.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(first, firstSession, Spawn(first.Player.Slot)));
        state.Apply(new ClientNpcTalkRuntimeCommand(
            first,
            new TerrariaNpcTalkState(first.Player.Slot.Value, NpcSlot: 12)));

        Assert.True(state.TryGetPlayerTalkNpc(first.Player, out short firstNpc));
        Assert.Equal(12, firstNpc);

        state.Apply(new PlayerDisconnectRuntimeCommand(first));
        firstSession.Dispose();

        using PlayerJoinSession replacementSession = CreateAwaitingSpawnSession(slots);
        ConnectionHandle replacement = Connection(1202, replacementSession.Handle);
        state.Apply(new PlayerSpawnRuntimeCommand(replacement, replacementSession, Spawn(replacement.Player.Slot)));
        state.Apply(new ClientNpcTalkRuntimeCommand(
            first,
            new TerrariaNpcTalkState(first.Player.Slot.Value, NpcSlot: 33)));

        Assert.False(state.TryGetPlayerTalkNpc(first.Player, out _));
        Assert.True(state.TryGetPlayerTalkNpc(replacement.Player, out short replacementNpc));
        Assert.Equal(TerrariaNpcTalkCodec.NoNpc, replacementNpc);
    }

    private static PlayerJoinSession CreateAwaitingSpawnSession(PlayerSlotPool slots)
    {
        Assert.True(slots.TryAcquire(out PlayerSlotPool.PlayerSlotLease? lease));
        var session = new PlayerJoinSession(Assert.IsType<PlayerSlotPool.PlayerSlotLease>(lease));
        Assert.Equal(PlayerJoinTransition.WorldRequestAccepted, session.ObserveWorldRequest());
        Assert.Equal(PlayerJoinTransition.SectionRequestAccepted, session.ObserveSectionRequest());
        return session;
    }

    private static ConnectionHandle Connection(long id, PlayerHandle player) =>
        new(GameCommandSourceId.FromConnection(id), player);

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId player) =>
        new(player, SpawnX: 20, SpawnY: 20, RespawnTimer: 0, DeathsPve: 0, DeathsPvp: 0, Team: 0, SpawnContext: 0);
}
