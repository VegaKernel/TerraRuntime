using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimePlayerLifecycleRelayTests
{
    [Fact]
    public void Spawn_and_disconnect_relay_official_player_active_transitions()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(1);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(2);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 1, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        registry.PlayerSpawned(first, in firstSpawn);
        registry.PlayerSpawned(second, in secondSpawn);

        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(2, registry.PlayerActiveBaselineFrames);

        Assert.True(registry.TryUnregister(secondSource, out PlayerHandle? unregistered));
        Assert.Equal(second.Player, unregistered);
        registry.PlayerDisconnected(second);

        Assert.Equal(2, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.PlayerDeactivationFrames);
    }


    [Fact]
    public void Respawn_relay_excludes_originating_client_to_avoid_spawn_feedback_loop()
    {
        var registry = new RuntimeConnectionRegistry();
        GameCommandSourceId firstSource = GameCommandSourceId.FromConnection(11);
        GameCommandSourceId secondSource = GameCommandSourceId.FromConnection(12);
        var firstOutbound = CreateOutbound();
        var secondOutbound = CreateOutbound();
        ConnectionHandle first = Connection(firstSource, slot: 1, generation: 1);
        ConnectionHandle second = Connection(secondSource, slot: 2, generation: 1);

        Assert.True(registry.TryRegister(firstSource, firstOutbound));
        Assert.True(registry.TryRegister(secondSource, secondOutbound));
        PlayerSpawnCommitRequest firstSpawn = CreateSpawn(first.Player.Slot);
        PlayerSpawnCommitRequest secondSpawn = CreateSpawn(second.Player.Slot);
        registry.PlayerSpawned(first, in firstSpawn);
        registry.PlayerSpawned(second, in secondSpawn);
        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(1, secondOutbound.QueuedFrames);

        PlayerSpawnCommitRequest respawn = CreateSpawn(first.Player.Slot);
        registry.PlayerRespawned(first, in respawn);

        Assert.Equal(1, firstOutbound.QueuedFrames);
        Assert.Equal(2, secondOutbound.QueuedFrames);
        Assert.Equal(1, registry.RelayedMovementFrames);
    }

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 8_192, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(
        GameCommandSourceId source,
        byte slot,
        ulong generation) =>
        new(
            source,
            new PlayerHandle(
                new PlayerSlotId(slot),
                new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest CreateSpawn(PlayerSlotId slot) =>
        new(slot, 100, 200, 0, 0, 0, 0, 0);

}
