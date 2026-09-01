using TerraRuntime.Gameplay.Npcs;
using TerraRuntime.Contracts.Gameplay;
using TerraRuntime.Contracts.Runtime;
using TerraRuntime.Core;

namespace TerraRuntime.Tests;

public sealed class RuntimeNpcActorControlRegistryConcurrencyTests
{
    [Fact]
    public async Task Concurrent_mutation_and_commit_preserve_the_latest_staged_intent()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));

        var controls = new RuntimeNpcActorControlRegistry(store);
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(
                npc.Handle,
                new ActorControllerId("test:lock-free-staging"),
                out NpcActorControlLease? lease));
        Assert.NotNull(lease);
        Assert.True(controls.CommitPending());

        Task mutator = Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                Assert.True(lease!.TryMoveTo(i, -i));
            }
        }, cancellationToken);

        Task committer = Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                controls.CommitPending();
            }
        }, cancellationToken);

        await Task.WhenAll(mutator, committer);

        Assert.True(lease!.TryMoveTo(12_345f, -54_321f));
        Assert.True(controls.CommitPending());
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out NpcActorControlBinding binding));
        Assert.Equal(NpcActorIntentKind.MoveTo, binding.Intent.Kind);
        Assert.Equal(12_345f, binding.Intent.TargetX);
        Assert.Equal(-54_321f, binding.Intent.TargetY);
    }

    [Fact]
    public async Task Concurrent_commit_cannot_lose_a_staged_release()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        var store = new RuntimeNpcStore(capacity: 1);
        NpcStateUpdate initial = CreateNpc(VanillaNpcIds.Zombie.Value);
        Assert.True(store.TrySpawn(0, in initial, out NpcSnapshot npc));

        var controls = new RuntimeNpcActorControlRegistry(store);
        Assert.Equal(
            NpcActorControlAcquireResult.Acquired,
            controls.TryAcquire(
                npc.Handle,
                new ActorControllerId("test:lock-free-release"),
                out NpcActorControlLease? lease));
        Assert.NotNull(lease);
        Assert.True(controls.CommitPending());
        Assert.True(controls.Snapshot.TryGet(npc.Handle, out _));

        Task committer = Task.Run(() =>
        {
            for (int i = 0; i < 5_000; i++)
            {
                if ((i & 255) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                controls.CommitPending();
            }
        }, cancellationToken);

        Task releaser = Task.Run(lease!.Dispose, cancellationToken);
        await Task.WhenAll(committer, releaser);

        controls.CommitPending();
        Assert.False(controls.Snapshot.TryGet(npc.Handle, out _));
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
