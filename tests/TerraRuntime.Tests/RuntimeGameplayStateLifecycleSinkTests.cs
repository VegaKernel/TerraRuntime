using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeGameplayStateLifecycleSinkTests
{
    [Fact]
    public void Npc_extension_state_follows_authoritative_generation_lifetime()
    {
        var extensionState = new RuntimeNpcExtensionStateStore<string>(capacity: 2);
        var lifecycle = new RuntimeNpcExtensionStateLifecycleSink<string>(extensionState);
        var store = new RuntimeNpcStore(capacity: 2, commitSink: lifecycle);

        NpcStateUpdate initialUpdate = NpcUpdate();
        Assert.True(store.TrySpawn(0, in initialUpdate, out NpcSnapshot first));
        Assert.True(extensionState.TryGet(first.Handle, out string? initial));
        Assert.Null(initial);
        Assert.True(extensionState.TrySet(first.Handle, "phase-one"));

        NpcStateUpdate movedUpdate = NpcUpdate(positionX: 4f);
        Assert.True(store.TryUpdate(first.Handle, in movedUpdate, out _));
        Assert.True(extensionState.TryGet(first.Handle, out string? preserved));
        Assert.Equal("phase-one", preserved);

        Assert.True(store.TryDespawn(first.Handle));
        Assert.False(extensionState.TryGet(first.Handle, out _));

        NpcStateUpdate respawnUpdate = NpcUpdate(positionX: 8f);
        Assert.True(store.TrySpawn(0, in respawnUpdate, out NpcSnapshot second));
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);
        Assert.True(extensionState.TryGet(second.Handle, out string? reset));
        Assert.Null(reset);
        Assert.False(extensionState.TrySet(first.Handle, "stale"));
        Assert.Equal(0, lifecycle.MismatchCount);
    }

    [Fact]
    public void Projectile_extension_state_retires_on_silent_remove_and_resets_on_slot_reuse()
    {
        var extensionState = new RuntimeProjectileExtensionStateStore<string>(capacity: 4);
        var lifecycle = new RuntimeProjectileExtensionStateLifecycleSink<string>(extensionState);
        var store = new RuntimeProjectileStore(capacity: 4, commitSink: lifecycle);

        ProjectileStateUpdate initialUpdate = ProjectileUpdate();
        Assert.True(store.TrySpawn(1, in initialUpdate, out ProjectileSnapshot first));
        Assert.True(extensionState.TrySet(first.Handle, "armed"));
        Assert.True(store.TryRemove(first.Handle, out _));
        Assert.False(extensionState.TryGet(first.Handle, out _));

        ProjectileStateUpdate respawnUpdate = ProjectileUpdate(positionX: 12f);
        Assert.True(store.TrySpawn(1, in respawnUpdate, out ProjectileSnapshot second));
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);
        Assert.True(extensionState.TryGet(second.Handle, out string? reset));
        Assert.Null(reset);
        Assert.False(extensionState.TrySet(first.Handle, "stale"));
        Assert.Equal(0, lifecycle.MismatchCount);
    }

    [Fact]
    public void Fanout_preserves_commit_order_for_all_sinks()
    {
        var order = new List<string>();
        var fanout = new NpcStateCommitSinkFanout(
            new RecordingNpcSink("first", order),
            new RecordingNpcSink("second", order));
        var store = new RuntimeNpcStore(capacity: 1, commitSink: fanout);

        NpcStateUpdate update = NpcUpdate();
        Assert.True(store.TrySpawn(0, in update, out NpcSnapshot snapshot));
        Assert.True(store.TryDespawn(snapshot.Handle));

        Assert.Equal(["first:Spawn", "second:Spawn", "first:Despawn", "second:Despawn"], order);
        Assert.Equal(2, fanout.Count);
    }

    private static NpcStateUpdate NpcUpdate(float positionX = 0f) =>
        new(
            Type: 1,
            NetId: 1,
            PositionX: positionX,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: 0,
            Ai: new NpcAiState(0f, 0f, 0f, 0f),
            Simulation: NpcSimulationState.Initial);

    private static ProjectileStateUpdate ProjectileUpdate(float positionX = 0f) =>
        new(
            new ProjectileTypeId(3),
            Spawner: 2,
            PositionX: positionX,
            PositionY: 0f,
            VelocityX: 0f,
            VelocityY: 0f,
            Ai: new ProjectileAiState(0f, 0f, 0f),
            BannerIdToRespondTo: 0,
            Damage: 5,
            KnockBack: 1f,
            OriginalDamage: 5);

    private sealed class RecordingNpcSink(string name, List<string> order) : INpcStateCommitSink
    {
        public void NpcStateCommitted(NpcStateCommitKind kind, in NpcSnapshot snapshot) =>
            order.Add($"{name}:{kind}");
    }
}
