using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcActorControlRegistryTests
{
    [Fact]
    public void Acquire_and_command_changes_publish_only_at_safe_boundary()
    {
        var store = new RuntimeNpcStore(capacity: 4);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(2, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        ActorControllerId controller = new("test:escort");

        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, controller, out NpcActorControlLease? lease));
        Assert.NotNull(lease);
        Assert.False(controls.Snapshot.TryGet(npc.Handle, out _));

        Assert.True(controls.CommitPending());
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out NpcActorControlBinding stopped));
        Assert.Equal(NpcActorIntentKind.Stop, stopped.Intent.Kind);

        PlayerHandle target = new(new PlayerSlotId(7), new PlayerSessionGeneration(3));
        Assert.True(lease!.TryFollowPlayer(target));
        Assert.Equal(NpcActorIntentKind.Stop, controls.Snapshot.TryGet(npc.Handle, out stopped) ? stopped.Intent.Kind : default);

        Assert.True(controls.CommitPending());
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out NpcActorControlBinding following));
        Assert.Equal(NpcActorIntentKind.FollowPlayer, following.Intent.Kind);
        Assert.Equal(target, following.Intent.TargetPlayer);
    }

    [Fact]
    public void Disposing_lease_retires_control_on_next_commit()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));
        var controls = new RuntimeNpcActorControlRegistry(store);
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(npc.Handle, new ActorControllerId("test:owner"), out NpcActorControlLease? lease));
        controls.CommitPending();
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out _));

        lease!.Dispose();
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out _));
        Assert.True(controls.CommitPending());
        Assert.False(controls.Snapshot.TryGet(npc.Handle, out _));
    }

    [Fact]
    public void Slot_reuse_never_applies_control_to_new_generation()
    {
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot first));
        var controls = new RuntimeNpcActorControlRegistry(store);
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(first.Handle, new ActorControllerId("test:first"), out _));
        controls.CommitPending();

        Assert.True(store.TryDespawn(first.Handle));
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot second));
        Assert.NotEqual(first.Handle.Generation, second.Handle.Generation);
        Assert.False(controls.Snapshot.TryGet(second.Handle, out _));

        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(second.Handle, new ActorControllerId("test:second"), out _));
        Assert.True(controls.CommitPending());
        Assert.True(controls.Snapshot.TryGet(second.Handle, out NpcActorControlBinding rebound));
        Assert.Equal(new ActorControllerId("test:second"), rebound.ControllerId);
    }

    [Fact]
    public void Initial_slice_rejects_npc_families_without_verified_walking_actor_physics()
    {
        var store = new RuntimeNpcStore(capacity: 2);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.BlueSlime.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot slime));
        var controls = new RuntimeNpcActorControlRegistry(store);

        Assert.Equal(
            NpcActorControlAcquireResult.UnsupportedNpcType,
            controls.TryAcquire(slime.Handle, new ActorControllerId("test:slime"), out _));
    }

    private static NpcStateUpdate CreateNpc(int type) =>
        new(
            Type: type,
            NetId: checked((short)type),
            PositionX: 100f,
            PositionY: 100f,
            VelocityX: 0f,
            VelocityY: 0f,
            Target: VanillaNpcDefinitionCatalog.DefaultTarget,
            Ai: default,
            Simulation: NpcSimulationState.Initial);
}
