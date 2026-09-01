using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcZombieReplicationTests
{
    [Fact]
    public void Playing_client_receives_verified_Zombie_commit()
    {
        var replication = new RuntimeNpcReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(77);
        var outbound = new TerrariaConnectionOutboundQueue(
            new OutboundQueueOptions(maxFrames: 8, maxQueuedBytes: 16_384, maxFrameBytes: 1_024));
        Assert.True(replication.TryRegister(source, outbound));
        var player = new ConnectionHandle(
            source,
            new PlayerHandle(new PlayerSlotId(2), new PlayerSessionGeneration(1)));
        var spawn = new PlayerSpawnCommitRequest(player.Player.Slot, 100, 200, 0, 0, 0, 0, 0);
        replication.PlayerSpawned(player, in spawn);

        NpcSnapshot zombie = new(
            Handle: new NpcHandle(7, new NpcGeneration(1)),
            Revision: new NpcRevision(1),
            Type: 3,
            NetId: 3,
            PositionX: 120f,
            PositionY: 200f,
            VelocityX: 0.7f,
            VelocityY: 0f,
            Target: 2,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial with
            {
                DirectionX = 1,
                DirectionY = -1,
                SpriteDirection = 1,
                Life = 45,
                LifeMax = 45,
                TimeLeft = VanillaNpcDefinitionCatalog.DefaultTimeLeft
            });

        replication.NpcStateCommitted(NpcStateCommitKind.Spawn, in zombie);

        Assert.Equal(1, outbound.QueuedFrames);
        Assert.Equal(1, replication.RelayedFrames);
        Assert.Equal(0, replication.UnsupportedCommits);
        Assert.Equal(0, replication.RejectedFrames);
    }
}
