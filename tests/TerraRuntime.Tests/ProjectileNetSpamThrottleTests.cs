using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;
using TerraRuntime.Network;

namespace TerraRuntime.Tests;

/// <summary>
/// The source's per-projectile packet-27 budget, <c>Projectile.netSpam</c>.
/// </summary>
/// <remarks>
/// TerrariaServer 1.4.5.8 spends five units on every update it sends, refuses to send once the budget reaches
/// sixty, and refunds one unit per world tick. A projectile moving continuously therefore settles at roughly one
/// update every five ticks rather than one per tick. A spawn is never charged, matching the unconditional send
/// in <c>NewProjectile</c>.
/// </remarks>
public sealed class ProjectileNetSpamThrottleTests
{
    [Fact]
    public void Continuous_updates_settle_at_the_source_budget_rate()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4201);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 2, generation: 1);
        PlayerSpawnCommitRequest spawn = Spawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        // Twelve sends spend the whole sixty-unit budget, so the thirteenth consecutive update is refused.
        for (int tick = 0; tick < 12; tick++)
            replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, Moving(tick));

        long afterBudget = replication.RelayedFrames;
        Assert.Equal(0, replication.ThrottledUpdateFrames);

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, Moving(12));
        Assert.Equal(afterBudget, replication.RelayedFrames);
        Assert.Equal(1, replication.ThrottledUpdateFrames);

        // Five refunds buy exactly one more send.
        for (int i = 0; i < 5; i++)
            replication.AdvanceNetSpamBudget();

        replication.ProjectileStateCommitted(ProjectileStateCommitKind.Update, Moving(13));
        Assert.Equal(afterBudget + 1, replication.RelayedFrames);
    }

    [Fact]
    public void A_spawn_is_never_charged_against_the_budget()
    {
        var replication = new RuntimeProjectileReplicationRegistry();
        GameCommandSourceId source = GameCommandSourceId.FromConnection(4202);
        TerrariaConnectionOutboundQueue outbound = CreateOutbound();
        Assert.True(replication.TryRegister(source, outbound));
        ConnectionHandle player = Connection(source, slot: 3, generation: 1);
        PlayerSpawnCommitRequest spawn = Spawn(player.Player.Slot);
        replication.PlayerSpawned(player, in spawn);

        for (int tick = 0; tick < 40; tick++)
            replication.ProjectileStateCommitted(ProjectileStateCommitKind.Spawn, Moving(tick));

        Assert.Equal(0, replication.ThrottledUpdateFrames);
        Assert.Equal(40, replication.RelayedFrames);
    }

    private static ProjectileSnapshot Moving(int tick) =>
        new(
            new ProjectileHandle(7, new ProjectileGeneration(1)),
            new ProjectileRevision((ulong)(tick + 1)),
            new ProjectileTypeId(1),
            Spawner: 2,
            PositionX: 100f + tick,
            PositionY: 200f,
            VelocityX: 1f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 0f,
            OriginalDamage: 10);

    private static TerrariaConnectionOutboundQueue CreateOutbound() =>
        new(new OutboundQueueOptions(maxFrames: 512, maxQueuedBytes: 256 * 1024, maxFrameBytes: 1_024));

    private static ConnectionHandle Connection(GameCommandSourceId source, byte slot, ulong generation) =>
        new(source, new PlayerHandle(new PlayerSlotId(slot), new PlayerSessionGeneration(generation)));

    private static PlayerSpawnCommitRequest Spawn(PlayerSlotId slot) => new(slot, 100, 100, 0, 0, 0, 0, 0);
}
