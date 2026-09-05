using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;
using TerraRuntime.Protocol.Multiplicity;

namespace TerraRuntime.Tests;

public sealed class RuntimeConnectionRegistryGodModeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Godmode_change_broadcasts_exact_vanilla_creative_power_frame_to_playing_clients(bool enabled)
    {
        var registry = new RuntimeConnectionRegistry();
        var ownerOutbound = Queue();
        var peerOutbound = Queue();
        GameCommandSourceId ownerSource = GameCommandSourceId.FromConnection(81);
        GameCommandSourceId peerSource = GameCommandSourceId.FromConnection(82);
        var ownerSlot = new PlayerSlotId(7);
        var peerSlot = new PlayerSlotId(8);
        ConnectionHandle owner = Connection(ownerSource, ownerSlot);
        ConnectionHandle peer = Connection(peerSource, peerSlot);

        Assert.True(registry.TryRegister(ownerSource, ownerOutbound));
        Assert.True(registry.TryRegister(peerSource, peerOutbound));
        PlayerSpawnCommitRequest ownerSpawn = Spawn(ownerSlot);
        PlayerSpawnCommitRequest peerSpawn = Spawn(peerSlot);
        registry.PlayerSpawned(owner, in ownerSpawn);
        registry.PlayerSpawned(peer, in peerSpawn);
        int ownerBefore = ownerOutbound.QueuedFrames;
        int peerBefore = peerOutbound.QueuedFrames;

        registry.PlayerGodModeChanged(owner.Player, enabled);

        Assert.Equal(ownerBefore + 1, ownerOutbound.QueuedFrames);
        Assert.Equal(peerBefore + 1, peerOutbound.QueuedFrames);
    }

    private static TerrariaConnectionOutboundQueue Queue() =>
        new(new OutboundQueueOptions(maxFrames: 64, maxQueuedBytes: 64 * 1024, maxFrameBytes: 4 * 1024));

    private static ConnectionHandle Connection(GameCommandSourceId source, PlayerSlotId slot) =>
        new(source, new PlayerHandle(slot, new PlayerSessionGeneration(1)));

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) =>
        new(slot, SpawnX: 100, SpawnY: 200, RespawnTimer: 0, DeathsPve: 0, DeathsPvp: 0, Team: 0, SpawnContext: 0);
}
