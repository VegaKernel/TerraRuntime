using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeProjectileSpawnerInvariantTests
{
    [Fact]
    public void Authoritative_update_cannot_change_spawner_inside_generation()
    {
        var store = new RuntimeProjectileStore(capacity: 4);
        ProjectileStateUpdate initial = CreateState(spawner: 3, positionX: 10f);
        Assert.True(store.TrySpawn(1, in initial, out ProjectileSnapshot spawned));
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycleBefore));

        ProjectileStateUpdate foreignOwner = CreateState(spawner: 4, positionX: 20f);
        Assert.False(store.TryUpdate(spawned.Handle, in foreignOwner, out _));

        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot current));
        Assert.Equal((byte)3, current.Spawner);
        Assert.Equal(10f, current.PositionX);
        Assert.Equal(new ProjectileRevision(1), current.Revision);
        Assert.True(store.TryGetLifecycle(spawned.Handle, out ProjectileLifecycleState lifecycleAfter));
        Assert.Equal(lifecycleBefore, lifecycleAfter);
    }

    [Fact]
    public void Simulation_cannot_change_spawner_or_escalate_expiry_to_server_despawn()
    {
        var sink = new RecordingCommitSink();
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: sink);
        ProjectileStateUpdate initial = CreateState(spawner: 3, positionX: 10f);
        Assert.True(store.TrySpawn(1, in initial, out ProjectileSnapshot spawned));
        sink.Commits.Clear();

        ProjectileStateUpdate forged = CreateState(
            spawner: VanillaProjectileOwnership.ServerOwner,
            positionX: 20f);
        Assert.False(store.TryCommitSimulationStep(
            spawned.Handle,
            in forged,
            timeLeft: 0,
            out _,
            out _));

        Assert.True(store.TryGet(spawned.Handle, out ProjectileSnapshot current));
        Assert.Equal((byte)3, current.Spawner);
        Assert.Equal(10f, current.PositionX);
        Assert.Empty(sink.Commits);
    }

    private static ProjectileStateUpdate CreateState(byte spawner, float positionX) =>
        new(
            Type: new ProjectileTypeId(1122),
            Spawner: spawner,
            PositionX: positionX,
            PositionY: 20f,
            VelocityX: 1f,
            VelocityY: 0f,
            Ai: default,
            BannerIdToRespondTo: 0,
            Damage: 10,
            KnockBack: 1f,
            OriginalDamage: 10);

    private sealed class RecordingCommitSink : IProjectileStateCommitSink
    {
        public List<(ProjectileStateCommitKind Kind, ProjectileSnapshot Snapshot)> Commits { get; } = [];

        public void ProjectileStateCommitted(ProjectileStateCommitKind kind, in ProjectileSnapshot snapshot) =>
            Commits.Add((kind, snapshot));
    }
}
